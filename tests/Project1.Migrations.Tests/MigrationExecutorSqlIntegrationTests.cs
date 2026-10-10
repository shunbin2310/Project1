using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using static Project1.Migrations.Tests.MigrationIntegrationTests;

namespace Project1.Migrations.Tests;

public sealed class MigrationExecutorSqlIntegrationTests
{
    [SqlServerCiFact]
    public async Task ScopedAccount_GeneratedSqlUpgradesAndRepeatsWithoutLosingProductOrNote()
    {
        var batches = MigrationSqlScript.ReadFromCiWorkspace();
        await using var database = await CiDatabase.CreateAsync("upgrade");
        await using var context = database.CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(BeforeNote);
        var productId = await InsertPreMigrationProductAsync(database);
        await using var account = await CiMigrationAccount.CreateAsync(
            database, CiMigrationPermissionProfile.ProductNoteExecutor);
        await AssertPermissionsAsync(account, database.Name, alterProducts: true, insertHistory: true);

        // Admin arranges the fixture; an actual separate SQL login executes the exact CI artifact.
        await account.ExecuteScriptAsync(batches);

        await AssertAllMigrationsAppliedAsync(context);
        await AssertNoteColumnAsync(database);
        await AssertProductAsync(context, productId, null);
        var product = await context.Products.SingleAsync();
        product.Note = "Keep this note across a restricted-account rerun";
        await context.SaveChangesAsync(); // Business writes use only the fixture's admin connection.
        var history = (await context.Database.GetAppliedMigrationsAsync()).ToArray();

        await account.ExecuteScriptAsync(batches);

        context.ChangeTracker.Clear();
        Assert.Equal(history, (await context.Database.GetAppliedMigrationsAsync()).ToArray());
        await AssertProductAsync(context, productId, product.Note);
        await AssertPermissionsAsync(account, database.Name, alterProducts: true, insertHistory: true);
    }

    [SqlServerCiFact]
    public async Task ReadOnlyAccount_CannotApplyGeneratedSqlAndLeavesOldSchemaHistoryAndProduct()
    {
        var batches = MigrationSqlScript.ReadFromCiWorkspace();
        await using var database = await CiDatabase.CreateAsync("upgrade");
        await using var context = database.CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(BeforeNote);
        var productId = await InsertPreMigrationProductAsync(database);
        var history = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        await using var account = await CiMigrationAccount.CreateAsync(
            database, CiMigrationPermissionProfile.HistoryReadOnly);
        await AssertPermissionsAsync(account, database.Name, alterProducts: false, insertHistory: false);

        var error = await Assert.ThrowsAsync<SqlException>(() => account.ExecuteScriptAsync(batches));
        AssertPermissionError(error);

        await AssertOldDatabaseUnchangedAsync(database, history, productId);
    }

    [SqlServerCiFact]
    public async Task SchemaOnlyAccount_MissingHistoryInsertFailsWithoutRecordingMigration()
    {
        var batches = MigrationSqlScript.ReadFromCiWorkspace();
        await using var database = await CiDatabase.CreateAsync("upgrade");
        await using var context = database.CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(BeforeNote);
        var productId = await InsertPreMigrationProductAsync(database);
        var history = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        await using var account = await CiMigrationAccount.CreateAsync(
            database, CiMigrationPermissionProfile.ProductNoteWithoutHistoryInsert);
        await AssertPermissionsAsync(account, database.Name, alterProducts: true, insertHistory: false);

        var error = await Assert.ThrowsAsync<SqlException>(() => account.ExecuteScriptAsync(batches));
        AssertPermissionError(error);

        await AssertOldDatabaseUnchangedAsync(database, history, productId);
    }

    [SqlServerCiFact]
    public async Task ScopedAccount_CannotWriteBusinessDataChangeOtherObjectsOrAccessAnotherTestDatabase()
    {
        await using var database = await CiDatabase.CreateAsync("upgrade");
        await using var context = database.CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(BeforeNote);
        var productId = await InsertPreMigrationProductAsync(database);
        var history = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        await using var otherDatabase = await CiDatabase.CreateAsync("fresh");
        await using var account = await CiMigrationAccount.CreateAsync(
            database, CiMigrationPermissionProfile.ProductNoteExecutor);
        await AssertPermissionsAsync(account, database.Name, alterProducts: true, insertHistory: true);

        string[] deniedSql =
        [
            "SELECT COUNT(*) FROM dbo.Products;",
            "INSERT INTO dbo.Products DEFAULT VALUES;",
            "UPDATE dbo.Products SET Name = N'Forbidden' WHERE 1 = 0;",
            "DELETE FROM dbo.Products WHERE 1 = 0;",
            "UPDATE dbo.__EFMigrationsHistory SET ProductVersion = N'Forbidden' WHERE 1 = 0;",
            "DELETE FROM dbo.__EFMigrationsHistory WHERE 1 = 0;",
            "ALTER TABLE dbo.Departments ADD CiForbidden int NULL;",
            "CREATE TABLE dbo.CiForbidden (Id int NULL);",
            "GRANT SELECT ON OBJECT::dbo.Products TO public;",
            $"ALTER ROLE db_owner ADD MEMBER [{account.Name}];"
        ];
        foreach (var sql in deniedSql)
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => account.ExecuteScriptAsync([sql]));
            AssertPermissionError(error);
        }

        var crossDatabaseError = await Assert.ThrowsAsync<SqlException>(() =>
            account.ExecuteScriptAsync([$"USE [{otherDatabase.Name}]; SELECT DB_NAME();"]));
        Assert.Contains(crossDatabaseError.Errors.Cast<SqlError>(), error => error.Number == 916);
        await AssertOldDatabaseUnchangedAsync(database, history, productId);
        await AssertPermissionsAsync(account, database.Name, alterProducts: true, insertHistory: true);
    }

    [SqlServerCiFact]
    public async Task ScopedAccount_FirstErrorStopsLaterBatchesAndRollsBackUncommittedDdlAndHistory()
    {
        await using var database = await CiDatabase.CreateAsync("upgrade");
        await using var context = database.CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(BeforeNote);
        var productId = await InsertPreMigrationProductAsync(database);
        var history = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        await using var account = await CiMigrationAccount.CreateAsync(
            database, CiMigrationPermissionProfile.ProductNoteExecutor);
        await AssertPermissionsAsync(account, database.Name, alterProducts: true, insertHistory: true);

        // Deliberate CI-only failure AFTER both DDL and history INSERT succeeded, across GO-like batches.
        var error = await Assert.ThrowsAsync<SqlException>(() => account.ExecuteScriptAsync(
        [
            $"""
            BEGIN TRANSACTION;
            ALTER TABLE dbo.Products ADD Note nvarchar(max) NULL;
            INSERT INTO dbo.__EFMigrationsHistory (MigrationId, ProductVersion)
                VALUES (N'{AddNote}', N'CI rollback probe');
            """,
            "THROW 51051, 'Deliberate disposable-CI failure.', 1;",
            """
            COMMIT;
            INSERT INTO dbo.__EFMigrationsHistory (MigrationId, ProductVersion)
                VALUES (N'20990101000000_CiMustNotContinue', N'CI stop probe');
            """
        ]));
        Assert.Equal(51051, error.Number);

        await AssertOldDatabaseUnchangedAsync(database, history, productId);
        // Closing an unpooled connection rolled back ONLY the open transaction, not prior commits.
        await account.ExecuteScriptAsync(MigrationSqlScript.ReadFromCiWorkspace());
        await AssertAllMigrationsAppliedAsync(context);
        await AssertNoteColumnAsync(database);
        await AssertProductAsync(context, productId, null);
    }

    private static void AssertPermissionError(SqlException error) =>
        Assert.Contains(error.Errors.Cast<SqlError>(), item => item.Number is 229 or 262 or 2760 or 4902 or 15151 or 15247);

    private static async Task AssertPermissionsAsync(
        CiMigrationAccount account, string databaseName, bool alterProducts, bool insertHistory)
    {
        await using var connection = account.CreateConnection();
        await connection.OpenAsync();
        await CiDatabase.ValidateServerAsync(connection);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 10;
        command.CommandText = """
            SELECT ORIGINAL_LOGIN(), SUSER_SNAME(), USER_NAME(), DB_NAME(),
                IS_SRVROLEMEMBER(N'sysadmin') AS [Role.Server.sysadmin],
                IS_SRVROLEMEMBER(N'serveradmin') AS [Role.Server.serveradmin],
                IS_SRVROLEMEMBER(N'securityadmin') AS [Role.Server.securityadmin],
                IS_MEMBER(N'db_owner') AS [Role.Database.db_owner],
                IS_MEMBER(N'db_ddladmin') AS [Role.Database.db_ddladmin],
                IS_MEMBER(N'db_securityadmin') AS [Role.Database.db_securityadmin],
                -- Server-level HAS_PERMS_BY_NAME checks use NULL for both securable and class.
                HAS_PERMS_BY_NAME(NULL, NULL, N'CONTROL SERVER') AS [Server.CONTROL SERVER],
                HAS_PERMS_BY_NAME(NULL, NULL, N'ALTER ANY LOGIN') AS [Server.ALTER ANY LOGIN],
                HAS_PERMS_BY_NAME(NULL, NULL, N'CREATE ANY DATABASE') AS [Server.CREATE ANY DATABASE],
                HAS_PERMS_BY_NAME(NULL, NULL, N'IMPERSONATE ANY LOGIN') AS [Server.IMPERSONATE ANY LOGIN],
                HAS_PERMS_BY_NAME(DB_NAME(), N'DATABASE', N'CONTROL') AS [Database.CONTROL],
                HAS_PERMS_BY_NAME(DB_NAME(), N'DATABASE', N'CREATE TABLE') AS [Database.CREATE TABLE],
                HAS_PERMS_BY_NAME(DB_NAME(), N'DATABASE', N'CREATE PROCEDURE') AS [Database.CREATE PROCEDURE],
                HAS_PERMS_BY_NAME(N'dbo', N'SCHEMA', N'ALTER') AS [Schema.dbo.ALTER],
                HAS_PERMS_BY_NAME(DB_NAME(), N'DATABASE', N'ALTER ANY USER') AS [Database.ALTER ANY USER],
                HAS_PERMS_BY_NAME(DB_NAME(), N'DATABASE', N'ALTER ANY ROLE') AS [Database.ALTER ANY ROLE],
                HAS_PERMS_BY_NAME(N'dbo.__EFMigrationsHistory', N'OBJECT', N'SELECT') AS [History.SELECT],
                HAS_PERMS_BY_NAME(N'dbo.__EFMigrationsHistory', N'OBJECT', N'INSERT') AS [History.INSERT],
                HAS_PERMS_BY_NAME(N'dbo.__EFMigrationsHistory', N'OBJECT', N'UPDATE') AS [History.UPDATE],
                HAS_PERMS_BY_NAME(N'dbo.__EFMigrationsHistory', N'OBJECT', N'DELETE') AS [History.DELETE],
                HAS_PERMS_BY_NAME(N'dbo.__EFMigrationsHistory', N'OBJECT', N'ALTER') AS [History.ALTER],
                COALESCE(HAS_PERMS_BY_NAME(N'dbo.Products', N'OBJECT', N'ALTER'), 0) AS [Products.ALTER],
                COALESCE(HAS_PERMS_BY_NAME(N'dbo.Products', N'OBJECT', N'SELECT'), 0) AS [Products.SELECT],
                COALESCE(HAS_PERMS_BY_NAME(N'dbo.Products', N'OBJECT', N'INSERT'), 0) AS [Products.INSERT],
                COALESCE(HAS_PERMS_BY_NAME(N'dbo.Products', N'OBJECT', N'UPDATE'), 0) AS [Products.UPDATE],
                COALESCE(HAS_PERMS_BY_NAME(N'dbo.Products', N'OBJECT', N'DELETE'), 0) AS [Products.DELETE],
                COALESCE(HAS_PERMS_BY_NAME(N'dbo.Products', N'OBJECT', N'CONTROL'), 0) AS [Products.CONTROL],
                COALESCE(HAS_PERMS_BY_NAME(N'dbo.Departments', N'OBJECT', N'ALTER'), 0) AS [Departments.ALTER];
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(account.Name, reader.GetString(0));
        Assert.Equal(account.Name, reader.GetString(1));
        Assert.Equal(account.Name, reader.GetString(2));
        Assert.Equal(databaseName, reader.GetString(3));
        int[] expected =
        [
            0, 0, 0, 0, 0, 0, // No privileged server/database roles.
            0, 0, 0, 0,       // No server control, login/database creation or impersonation.
            0, 0, 0, 0, 0, 0, // No database control, object creation, schema/user/role alteration.
            1, insertHistory ? 1 : 0, 0, 0, 0,
            alterProducts ? 1 : 0, 0, 0, 0, 0, 0, 0
        ];
        Assert.Equal(4 + expected.Length, reader.FieldCount);
        for (var i = 0; i < expected.Length; i++)
        {
            var ordinal = i + 4;
            AssertPermissionValue(reader.GetName(ordinal),
                reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal), expected[i]);
        }
    }

    internal static void AssertPermissionValue(string checkName, int? actual, int expected)
    {
        // An unknown role/server/database/history permission must still fail closed.
        Assert.True(actual.HasValue,
            $"Permission check '{checkName}' returned NULL (unknown); expected {expected}. " +
            "Check the query arguments and metadata visibility; NULL is not proof of denied permission.");
        Assert.True(actual == expected,
            $"Permission check '{checkName}' expected {expected}, but returned {actual}.");
    }

    private static async Task AssertOldDatabaseUnchangedAsync(CiDatabase database, string[] history, int productId)
    {
        await using var context = database.CreateContext();
        Assert.Equal(history, (await context.Database.GetAppliedMigrationsAsync()).ToArray());
        await using var connection = database.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 10;
        command.CommandText = """
            SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Products') AND name = N'Note';
            SELECT COUNT(*) FROM dbo.Products;
            SELECT COUNT(*) FROM dbo.Products
            WHERE Id = @id AND Code = N'CI-PRODUCT' AND Name = N'Product created before Note'
                AND Description = N'Existing description must survive'
                AND DefaultUnitPrice = 12.50 AND ReorderLevel = 3 AND IsActive = 1 AND CreatedAtUtc = @createdAt;
            """;
        command.Parameters.Add("@id", System.Data.SqlDbType.Int).Value = productId;
        command.Parameters.Add("@createdAt", System.Data.SqlDbType.DateTimeOffset).Value = FixtureCreatedAt;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(0, reader.GetInt32(0));
        Assert.True(await reader.NextResultAsync());
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1, reader.GetInt32(0));
        Assert.True(await reader.NextResultAsync());
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1, reader.GetInt32(0));
    }

    private static async Task AssertProductAsync(Project1.Api.Data.AppDbContext context, int productId, string? note)
    {
        Assert.Equal(1, await context.Products.CountAsync());
        var product = await context.Products.AsNoTracking().SingleAsync();
        Assert.Equal(productId, product.Id);
        Assert.Equal("CI-PRODUCT", product.Code);
        Assert.Equal("Product created before Note", product.Name);
        Assert.Equal("Existing description must survive", product.Description);
        Assert.Equal(12.50m, product.DefaultUnitPrice);
        Assert.Equal(3m, product.ReorderLevel);
        Assert.Equal(FixtureCreatedAt, product.CreatedAtUtc);
        Assert.True(product.IsActive);
        Assert.Equal(note, product.Note);
        Assert.Equal("CI-CATEGORY", (await context.ProductCategories.AsNoTracking().SingleAsync()).Code);
        Assert.Equal("CI-UNIT", (await context.UnitsOfMeasure.AsNoTracking().SingleAsync()).Code);
    }
}
