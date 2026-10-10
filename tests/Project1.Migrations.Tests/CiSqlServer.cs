using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Project1.Api.Data;

namespace Project1.Migrations.Tests;

// SQL integration cases run only on GitHub. Missing opt-in on GitHub fails,
// rather than silently skipping; running this project on a PC does no SQL work.
public sealed class SqlServerCiFactAttribute : FactAttribute
{
    public SqlServerCiFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true")
        {
            Skip = "Requires the disposable SQL Server container in GitHub Actions.";
        }
    }
}

internal sealed record CiSqlServerSettings(string Password)
{
    internal const string ServerName = "project1-ci-sqlserver";

    internal static CiSqlServerSettings Load(Func<string, string?> readEnvironment)
    {
        if (readEnvironment("GITHUB_ACTIONS") != "true" ||
            readEnvironment("PROJECT1_CI_MIGRATION_TESTS") != "true")
        {
            throw new InvalidOperationException("Migration tests require explicit GitHub CI opt-in.");
        }

        var password = readEnvironment("PROJECT1_CI_SQLSERVER_PASSWORD");
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("The disposable SQL Server password is missing.");
        }

        return new CiSqlServerSettings(password);
    }

    internal string ConnectionString(string databaseName) => new SqlConnectionStringBuilder
    {
        // No configurable remote host, port, application connection string, or user secrets.
        DataSource = "tcp:127.0.0.1,14333",
        InitialCatalog = databaseName,
        UserID = "sa",
        Password = Password,
        Encrypt = true,
        TrustServerCertificate = true, // Only the isolated, self-signed CI container.
        ConnectTimeout = 2,
        Pooling = false,
        ApplicationName = "Project1.CI.Migrations"
    }.ConnectionString;

    internal static void ValidateServerIdentity(string serverName, int majorVersion)
    {
        if (!string.Equals(serverName, ServerName, StringComparison.OrdinalIgnoreCase) ||
            majorVersion != 17)
        {
            throw new InvalidOperationException("Refusing SQL writes: this is not the SQL Server 2025 CI container.");
        }
    }

    internal static void ValidateDatabaseName(string databaseName)
    {
        if (!Regex.IsMatch(databaseName, @"\AProject1CiMigration_[0-9a-f]{32}_(fresh|upgrade)\z"))
        {
            throw new InvalidOperationException("Refusing to create or drop a non-CI database.");
        }
    }

    internal static void ValidateLoginName(string loginName)
    {
        if (!Regex.IsMatch(loginName, @"\AProject1CiExecutor_[0-9a-f]{32}\z"))
        {
            throw new InvalidOperationException("Refusing to create or drop a non-CI login.");
        }
    }

    internal string AccountConnectionString(string databaseName, string loginName, string password)
    {
        ValidateDatabaseName(databaseName);
        ValidateLoginName(loginName);
        return new SqlConnectionStringBuilder(ConnectionString(databaseName))
        {
            UserID = loginName,
            Password = password
        }.ConnectionString;
    }
}

internal sealed class CiDatabase : IAsyncDisposable
{
    private readonly CiSqlServerSettings settings;
    private readonly SqlConnection admin;

    private CiDatabase(CiSqlServerSettings settings, SqlConnection admin, string name)
    {
        this.settings = settings;
        this.admin = admin;
        Name = name;
    }

    internal string Name { get; }

    internal static async Task<CiDatabase> CreateAsync(string scenario)
    {
        var settings = CiSqlServerSettings.Load(Environment.GetEnvironmentVariable);
        var name = $"Project1CiMigration_{Guid.NewGuid():N}_{scenario}";
        CiSqlServerSettings.ValidateDatabaseName(name);

        var admin = new SqlConnection(settings.ConnectionString("master"));
        try
        {
            var stopwatch = Stopwatch.StartNew();
            // Container startup is bounded. Never fall back to another SQL Server.
            while (true)
            {
                try
                {
                    await admin.OpenAsync();
                    break;
                }
                catch (SqlException) when (stopwatch.Elapsed < TimeSpan.FromSeconds(120))
                {
                    await Task.Delay(1000);
                }
            }

            await ValidateServerAsync(admin);
            await using var command = admin.CreateCommand();
            command.CommandTimeout = 60;
            command.CommandText = $"CREATE DATABASE [{name}];";
            await command.ExecuteNonQueryAsync();
            return new CiDatabase(settings, admin, name);
        }
        catch
        {
            await admin.DisposeAsync();
            throw;
        }
    }

    internal AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlServer(settings.ConnectionString(Name), options => options.CommandTimeout(60))
        .Options);

    internal SqlConnection CreateConnection() => new(settings.ConnectionString(Name));

    internal SqlConnection CreateAccountConnection(string loginName, string password) =>
        new(settings.AccountConnectionString(Name, loginName, password));

    internal async Task ExecuteScriptAsync(IReadOnlyList<string> batches)
    {
        await using var connection = CreateConnection();
        await ExecuteScriptAsync(connection, batches);
    }

    internal static async Task ExecuteScriptAsync(
        SqlConnection connection, IReadOnlyList<string> batches, bool guardTransactions = false)
    {
        _ = CiSqlServerSettings.Load(Environment.GetEnvironmentVariable);
        var databaseName = new SqlConnectionStringBuilder(connection.ConnectionString).InitialCatalog;
        CiSqlServerSettings.ValidateDatabaseName(databaseName);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await connection.OpenAsync(timeout.Token);
        await ValidateServerAsync(connection);
        if (connection.Database != databaseName)
        {
            throw new InvalidOperationException("Unexpected CI database identity.");
        }
        // One connection preserves session state and transactions across GO batches.
        // Raw artifact tests retain their original execution path. Restricted-account tests
        // additionally guard errors INSIDE a batch, before a later COMMIT can execute.
        foreach (var batch in batches)
        {
            await using var command = CreateScriptBatchCommand(connection, batch, guardTransactions);
            await command.ExecuteNonQueryAsync(timeout.Token);
        }
    }

    internal static SqlCommand CreateScriptBatchCommand(
        SqlConnection connection, string batch, bool guardTransactions)
    {
        var command = connection.CreateCommand();
        command.CommandTimeout = 60;
        if (!guardTransactions)
        {
            command.CommandText = batch;
            return command;
        }

        // Keep the original batch unchanged as a parameter, not interpolated SQL.
        // The lower execution scope lets CATCH handle name-resolution/compile errors too.
        // No outer transaction is added: previously committed migrations stay committed.
        command.CommandText = """
            SET XACT_ABORT ON;
            BEGIN TRY
                EXEC sys.sp_executesql @migrationBatch;
            END TRY
            BEGIN CATCH
                IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
                THROW;
            END CATCH;
            """;
        command.Parameters.Add("@migrationBatch", System.Data.SqlDbType.NVarChar, -1).Value = batch;
        return command;
    }

    internal static async Task ValidateServerAsync(SqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 10;
        command.CommandText = "SELECT CONVERT(nvarchar(128), @@SERVERNAME), CONVERT(int, SERVERPROPERTY('ProductMajorVersion'));";
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException("Cannot verify the CI SQL Server identity.");
        }

        CiSqlServerSettings.ValidateServerIdentity(reader.GetString(0), reader.GetInt32(1));
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            // Only this instance's successfully created, randomly named database is removed.
            CiSqlServerSettings.ValidateDatabaseName(Name);
            await ValidateServerAsync(admin);
            await using var command = admin.CreateCommand();
            command.CommandTimeout = 30;
            command.CommandText = $"ALTER DATABASE [{Name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{Name}];";
            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            await admin.DisposeAsync();
        }
    }
}
