using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Project1.Api.Data;
using Project1.Api.Migrations;

namespace Project1.Migrations.Tests;

public sealed class WholeQuantityMigrationTests
{
    [Fact]
    public void Migration_ChecksAllQuantitiesBeforeDroppingCatalogDataAndNeverRoundsRecords()
    {
        var migration = new RemoveProductCategoryAndUnitEnforceWholeQuantities();
        var preflight = Assert.IsType<SqlOperation>(migration.UpOperations[0]);
        var constraints = migration.UpOperations.OfType<AddCheckConstraintOperation>().ToList();
        Assert.Equal(7, constraints.Count);
        foreach (var constraint in constraints)
        {
            Assert.Contains($"[dbo].[{constraint.Table}]", preflight.Sql);
        }
        Assert.Contains("THROW 51000", preflight.Sql);
        Assert.DoesNotContain("UPDATE", preflight.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<NotSupportedException>(() => migration.DownOperations);

        using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=tcp:127.0.0.1,1;Database=QuantityScriptOnly;User Id=ci;Password=not-a-real-password")
            .Options);
        var ids = context.Database.GetMigrations().ToArray();
        var quantityIndex = Array.FindIndex(ids, id => id.EndsWith("_RemoveProductCategoryAndUnitEnforceWholeQuantities", StringComparison.Ordinal));
        var script = context.GetService<IMigrator>().GenerateScript(ids[quantityIndex - 1], ids[quantityIndex],
            MigrationsSqlGenerationOptions.Idempotent);
        Assert.True(script.IndexOf("THROW 51000", StringComparison.Ordinal) <
                    script.IndexOf("DROP TABLE", StringComparison.Ordinal));
        Assert.Contains("DROP TABLE [ProductCategories]", script);
        Assert.Contains("DROP TABLE [UnitsOfMeasure]", script);
        Assert.NotEmpty(SqlBatchParser.SplitBatches(script));

        var historicalBackfill = Assert.Single(new AddInventoryLedger().UpOperations.OfType<SqlOperation>());
        // The guarded historic INSERT must not bind a unit column removed by the latest migration.
        Assert.StartsWith("EXEC(N'INSERT INTO [InventoryTransactions]", historicalBackfill.Sql);
    }

    [SqlServerCiFact]
    public async Task ExistingFractions_BlockUpgradeBeforeAnyCatalogDataIsRemoved()
    {
        await using var database = await CiDatabase.CreateAsync("upgrade");
        await using var context = database.CreateContext();
        var ids = context.Database.GetMigrations().ToArray();
        var quantityIndex = Array.FindIndex(ids, id => id.EndsWith("_RemoveProductCategoryAndUnitEnforceWholeQuantities", StringComparison.Ordinal));
        var previous = ids[quantityIndex - 1];
        await context.GetService<IMigrator>().MigrateAsync(previous);
        var id = await MigrationIntegrationTests.InsertPreMigrationProductAsync(database);
        await context.Database.ExecuteSqlRawAsync("UPDATE dbo.Products SET ReorderLevel = 1.004;");

        var failure = await Assert.ThrowsAsync<SqlException>(() => context.Database.MigrateAsync());
        Assert.Equal(51000, failure.Number);
        Assert.Contains("Non-integer quantities in Products", failure.Message);
        Assert.Contains(await context.Database.GetPendingMigrationsAsync(), id =>
            id.EndsWith("_RemoveProductCategoryAndUnitEnforceWholeQuantities", StringComparison.Ordinal));
        await using (var connection = database.CreateConnection())
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT ReorderLevel FROM dbo.Products WHERE Id = @id;";
            command.Parameters.AddWithValue("@id", id);
            Assert.Equal(1.004m, await command.ExecuteScalarAsync());
            command.CommandText = "SELECT COUNT(*) FROM dbo.ProductCategories;";
            Assert.Equal(1, Convert.ToInt32(await command.ExecuteScalarAsync()));
            command.CommandText = "SELECT COUNT(*) FROM dbo.UnitsOfMeasure;";
            Assert.Equal(1, Convert.ToInt32(await command.ExecuteScalarAsync()));
        }

        // Only this disposable fixture is corrected; the migration itself never edits quantities.
        await context.Database.ExecuteSqlRawAsync("UPDATE dbo.Products SET ReorderLevel = 2;");
        await context.Database.MigrateAsync();
        Assert.Equal(2m, (await context.Products.SingleAsync()).ReorderLevel);
        var rejectedWrite = await Assert.ThrowsAsync<SqlException>(() =>
            context.Database.ExecuteSqlRawAsync("UPDATE dbo.Products SET ReorderLevel = 2.5;"));
        Assert.Equal(547, rejectedWrite.Number);

        await using var verifiedConnection = database.CreateConnection();
        await verifiedConnection.OpenAsync();
        await using var verify = verifiedConnection.CreateCommand();
        verify.CommandText = "SELECT COUNT(*) FROM sys.tables WHERE name IN ('ProductCategories', 'UnitsOfMeasure');";
        Assert.Equal(0, Convert.ToInt32(await verify.ExecuteScalarAsync()));
        verify.CommandText = "SELECT COUNT(*) FROM sys.columns WHERE name IN ('ProductCategoryId', 'UnitOfMeasureId', 'UnitOfMeasureCode');";
        Assert.Equal(0, Convert.ToInt32(await verify.ExecuteScalarAsync()));
        verify.CommandText = "SELECT COUNT(*) FROM sys.check_constraints WHERE name LIKE 'CK_%_WholeQuantities' AND is_disabled = 0 AND is_not_trusted = 0;";
        Assert.Equal(7, Convert.ToInt32(await verify.ExecuteScalarAsync()));
    }
}
