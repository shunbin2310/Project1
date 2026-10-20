using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Project1.Api.Data;

namespace Project1.Migrations.Tests;

// Fixed CI-only host/port/server identity and random databases, just like the existing
// migration integration tests. Never reads application/user-secret connection strings.
public sealed class StartupMigrationIntegrationTests
{
    [SqlServerCiFact]
    public async Task EmptyDatabase_DbScopedDdlAccountCanApplyInitialSchemaWithoutServerAdmin()
    {
        await using var database = await CiDatabase.CreateAsync("fresh");
        await using var login = await FixtureLogin.CreateAsync(database, ddl: true);
        await using var context = login.CreateContext();
        await context.Database.OpenConnectionAsync();
        await using (var command = context.Database.GetDbConnection().CreateCommand())
        {
            command.CommandText = """
                SELECT COALESCE(IS_SRVROLEMEMBER(N'sysadmin'),1),
                       COALESCE(IS_MEMBER(N'db_owner'),1),
                       COALESCE(HAS_PERMS_BY_NAME(NULL,NULL,N'CONTROL SERVER'),1),
                       COALESCE(HAS_PERMS_BY_NAME(NULL,NULL,N'CREATE ANY DATABASE'),1),
                       COALESCE(IS_MEMBER(N'db_ddladmin'),0);
                """;
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            for (var index = 0; index < 4; index++) Assert.Equal(0, reader.GetInt32(index));
            Assert.Equal(1, reader.GetInt32(4));
        }
        await context.Database.CloseConnectionAsync();
        await Runner(new EfStartupMigrationDatabase(context), database.Name).ApplyAsync();
        await MigrationIntegrationTests.AssertAllMigrationsAppliedAsync(context);
        Assert.Empty(await context.Products.ToListAsync());
    }

    [SqlServerCiFact]
    public async Task StartupAppliesPendingWithDbScopedDdl_PreservesExistingRows_SecondStartSkips()
    {
        await using var database = await CiDatabase.CreateAsync("upgrade");
        await using var adminContext = database.CreateContext();
        await adminContext.GetService<IMigrator>().MigrateAsync(MigrationIntegrationTests.AddPracticeNote);
        var productId = await MigrationIntegrationTests.InsertPreMigrationProductAsync(database);
        await using var login = await FixtureLogin.CreateAsync(database, ddl: true);
        await using var context = login.CreateContext();
        var databaseAdapter = new EfStartupMigrationDatabase(context);
        var runner = Runner(databaseAdapter, database.Name);

        Assert.NotEmpty(await databaseAdapter.GetPendingAsync(default));
        await runner.ApplyAsync();
        await MigrationIntegrationTests.AssertAllMigrationsAppliedAsync(context);
        await MigrationIntegrationTests.AssertStorageLocationColumnAsync(database);
        var product = await context.Products.SingleAsync();
        Assert.Equal(productId, product.Id);
        Assert.Equal("Existing description must survive", product.Description);
        Assert.Equal(12.50m, product.DefaultUnitPrice);
        Assert.Null(product.StorageLocation);
        var history = (await context.Database.GetAppliedMigrationsAsync()).ToArray();

        await runner.ApplyAsync();
        Assert.Equal(history, (await context.Database.GetAppliedMigrationsAsync()).ToArray());
        Assert.Equal(1, await context.Products.CountAsync());
    }

    [SqlServerCiFact]
    public async Task MissingDdl_RefusesStartupWithoutCompletingPendingMigration()
    {
        await using var database = await CiDatabase.CreateAsync("upgrade");
        await using var adminContext = database.CreateContext();
        await adminContext.GetService<IMigrator>().MigrateAsync(MigrationIntegrationTests.AddPracticeNote);
        var productId = await MigrationIntegrationTests.InsertPreMigrationProductAsync(database);
        var before = (await adminContext.Database.GetAppliedMigrationsAsync()).ToArray();
        await using var login = await FixtureLogin.CreateAsync(database, ddl: false);
        await using var context = login.CreateContext();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Runner(new EfStartupMigrationDatabase(context), database.Name).ApplyAsync());
        Assert.Equal(before, (await adminContext.Database.GetAppliedMigrationsAsync()).ToArray());
        await using var connection = database.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM dbo.Products WHERE Id=@id;";
        command.Parameters.AddWithValue("@id", productId);
        Assert.Equal(1, Convert.ToInt32(await command.ExecuteScalarAsync()));
    }

    private static StartupDatabaseMigrator Runner(IStartupMigrationDatabase database, string name) =>
        new(database, Options.Create(new DatabaseMigrationOptions
        {
            MigrateOnStartup = true, ExpectedDatabase = name, CommandTimeoutSeconds = 60
        }), NullLogger<StartupDatabaseMigrator>.Instance);

    private sealed class FixtureLogin(CiDatabase database, string name, string password) : IAsyncDisposable
    {
        private readonly CiSqlServerSettings settings = CiSqlServerSettings.Load(Environment.GetEnvironmentVariable);

        public static async Task<FixtureLogin> CreateAsync(CiDatabase database, bool ddl)
        {
            var name = "Project1CiStartup_" + Guid.NewGuid().ToString("N");
            // PUBLIC disposable fixture credential, never a deployment password.
            var login = new FixtureLogin(database, name, "StartupCiOnly!2026-" + Guid.NewGuid().ToString("N"));
            try
            {
                await using var connection = database.CreateConnection();
                await connection.OpenAsync();
                await CiDatabase.ValidateServerAsync(connection);
                CiSqlServerSettings.ValidateDatabaseName(database.Name);
                await using var command = connection.CreateCommand();
                command.CommandText = $"""
                    CREATE LOGIN [{name}] WITH PASSWORD=N'{login.password}', CHECK_POLICY=OFF;
                    CREATE USER [{name}] FOR LOGIN [{name}];
                    ALTER ROLE db_datareader ADD MEMBER [{name}];
                    ALTER ROLE db_datawriter ADD MEMBER [{name}];
                    """ + (ddl ? $"ALTER ROLE db_ddladmin ADD MEMBER [{name}];" : "");
                await command.ExecuteNonQueryAsync();
                return login;
            }
            catch
            {
                await login.DisposeAsync();
                throw;
            }
        }

        private string password { get; } = password;

        public AppDbContext CreateContext()
        {
            var builder = new SqlConnectionStringBuilder(settings.ConnectionString(database.Name))
            {
                UserID = name, Password = password, ApplicationName = "Project1.CI.StartupMigrations"
            };
            return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(builder.ConnectionString).Options);
        }

        public async ValueTask DisposeAsync()
        {
            // Name is generated here, not accepted from configuration or test inputs.
            await using var connection = new SqlConnection(settings.ConnectionString("master"));
            await connection.OpenAsync();
            await CiDatabase.ValidateServerAsync(connection);
            await using var command = connection.CreateCommand();
            command.CommandText = $"IF SUSER_ID(N'{name}') IS NOT NULL DROP LOGIN [{name}];";
            await command.ExecuteNonQueryAsync();
        }
    }
}
