using System.Security.Cryptography;
using Microsoft.Data.SqlClient;

namespace Project1.Migrations.Tests;

internal enum CiMigrationPermissionProfile
{
    HistoryReadOnly,
    ProductNoteWithoutHistoryInsert,
    ProductNoteExecutor
}

// TEST SUPPORT ONLY: disposable CI logins, not a production provisioning tool.
internal sealed class CiMigrationAccount : IAsyncDisposable
{
    private readonly CiDatabase database;
    private readonly SqlConnection admin;
    private readonly string password;
    private bool created;

    private CiMigrationAccount(CiDatabase database)
    {
        this.database = database;
        admin = database.CreateConnection();
        Name = $"Project1CiExecutor_{Guid.NewGuid():N}";
        password = "P1-CiOnly!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    }

    internal string Name { get; }

    internal static string GrantSql(string name, CiMigrationPermissionProfile profile)
    {
        CiSqlServerSettings.ValidateLoginName(name);
        var grants = $"GRANT CONNECT TO [{name}]; GRANT SELECT ON OBJECT::dbo.__EFMigrationsHistory TO [{name}];";
        return profile switch
        {
            CiMigrationPermissionProfile.HistoryReadOnly => grants,
            CiMigrationPermissionProfile.ProductNoteWithoutHistoryInsert =>
                grants + $" GRANT ALTER ON OBJECT::dbo.Products TO [{name}];",
            CiMigrationPermissionProfile.ProductNoteExecutor =>
                grants + $" GRANT ALTER ON OBJECT::dbo.Products TO [{name}];" +
                $" GRANT INSERT ON OBJECT::dbo.__EFMigrationsHistory TO [{name}];",
            _ => throw new InvalidOperationException("Unknown CI permission profile.")
        };
    }

    internal static async Task<CiMigrationAccount> CreateAsync(
        CiDatabase database, CiMigrationPermissionProfile profile)
    {
        _ = CiSqlServerSettings.Load(Environment.GetEnvironmentVariable);
        CiSqlServerSettings.ValidateDatabaseName(database.Name);
        var account = new CiMigrationAccount(database);
        try
        {
            var grants = GrantSql(account.Name, profile);
            await account.admin.OpenAsync();
            await CiDatabase.ValidateServerAsync(account.admin);
            if (account.admin.Database != database.Name)
            {
                throw new InvalidOperationException("Unexpected CI database identity.");
            }

            await using var command = account.admin.CreateCommand();
            command.CommandTimeout = 30;
            // Names and generated password have a fixed, injection-free alphabet. No caller SQL.
            // Failed provisioning rolls back on disposal of this unpooled connection.
            command.CommandText = $"""
                BEGIN TRANSACTION;
                CREATE LOGIN [{account.Name}] WITH PASSWORD = '{account.password}',
                    CHECK_POLICY = ON, CHECK_EXPIRATION = OFF, DEFAULT_DATABASE = [{database.Name}];
                CREATE USER [{account.Name}] FOR LOGIN [{account.Name}] WITH DEFAULT_SCHEMA = dbo;
                {grants}
                COMMIT;
                """;
            await command.ExecuteNonQueryAsync();
            account.created = true;
            return account;
        }
        catch
        {
            await account.admin.DisposeAsync();
            throw;
        }
    }

    internal SqlConnection CreateConnection() => database.CreateAccountConnection(Name, password);

    internal async Task ExecuteScriptAsync(IReadOnlyList<string> batches)
    {
        await using var connection = CreateConnection();
        await CiDatabase.ExecuteScriptAsync(connection, batches, guardTransactions: true);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!created) return;
            _ = CiSqlServerSettings.Load(Environment.GetEnvironmentVariable);
            CiSqlServerSettings.ValidateDatabaseName(database.Name);
            CiSqlServerSettings.ValidateLoginName(Name);
            await CiDatabase.ValidateServerAsync(admin);
            if (admin.Database != database.Name)
            {
                throw new InvalidOperationException("Unexpected CI cleanup database.");
            }

            await using var command = admin.CreateCommand();
            command.CommandTimeout = 30;
            // Only this instance's successfully created random user/login is removed.
            command.CommandText = $"DROP USER [{Name}]; DROP LOGIN [{Name}];";
            await command.ExecuteNonQueryAsync();
            created = false;
        }
        finally
        {
            await admin.DisposeAsync();
        }
    }
}
