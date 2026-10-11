using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Project1.Api.Data;
using Project1.Api.DTOs.Products;
using Project1.Api.Services.Products;
using static Project1.Migrations.Tests.MigrationIntegrationTests;

namespace Project1.Migrations.Tests;

public sealed class MigrationSqlIntegrationTests
{
    [SqlServerCiFact]
    public async Task GeneratedSql_EmptyDatabaseCreatesLatestSchema()
    {
        var batches = MigrationSqlScript.ReadFromCiWorkspace();
        await using var database = await CiDatabase.CreateAsync("fresh");
        await using var context = database.CreateContext();

        await database.ExecuteScriptAsync(batches);

        await AssertAllMigrationsAppliedAsync(context);
        await AssertNoteColumnAsync(database);
        await AssertPracticeNoteColumnAbsentAsync(database);
        await AssertStorageLocationColumnAsync(database);
        Assert.Empty(await context.Products.AsNoTracking().ToListAsync());
    }

    [SqlServerCiFact]
    public async Task GeneratedSql_UpgradePreservesOldProductAndNoteCanBeSaved()
    {
        var batches = MigrationSqlScript.ReadFromCiWorkspace();
        await using var database = await CiDatabase.CreateAsync("upgrade");
        await using var context = database.CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(BeforeNote);
        Assert.DoesNotContain(AddNote, await context.Database.GetAppliedMigrationsAsync());
        var productId = await InsertPreMigrationProductAsync(database);

        // EF is used only to arrange the old schema. The upgrade uses the generated file.
        await database.ExecuteScriptAsync(batches);

        await AssertAllMigrationsAppliedAsync(context);
        await AssertNoteColumnAsync(database);
        await AssertPracticeNoteColumnAbsentAsync(database);
        await AssertStorageLocationColumnAsync(database);
        await AssertProductAsync(context, productId, null);
        var product = await context.Products.SingleAsync();
        product.Note = "Note saved after executing generated SQL";
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        await AssertProductAsync(context, productId, product.Note);
    }

    [SqlServerCiFact]
    public async Task GeneratedSql_SecondExecutionPreservesHistoryProductAndNote()
    {
        var batches = MigrationSqlScript.ReadFromCiWorkspace();
        await using var database = await CiDatabase.CreateAsync("fresh");
        await using var context = database.CreateContext();
        await database.ExecuteScriptAsync(batches);
        var productId = await InsertPreMigrationProductAsync(database);
        var product = await context.Products.SingleAsync();
        product.Note = "Keep this note when the same SQL runs again";
        await context.SaveChangesAsync();
        var history = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.NotEmpty(history);

        await database.ExecuteScriptAsync(batches);

        context.ChangeTracker.Clear();
        Assert.Equal(history, (await context.Database.GetAppliedMigrationsAsync()).ToArray());
        await AssertAllMigrationsAppliedAsync(context);
        await AssertNoteColumnAsync(database);
        await AssertPracticeNoteColumnAbsentAsync(database);
        await AssertStorageLocationColumnAsync(database);
        await AssertProductAsync(context, productId, product.Note);
    }

    [SqlServerCiFact]
    public async Task GeneratedSql_CurrentDatabaseUpgradePreservesBusinessNoteAndStorageValue()
    {
        var batches = MigrationSqlScript.ReadFromCiWorkspace();
        await using var database = await CiDatabase.CreateAsync("upgrade");
        await using var context = database.CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(AddNote);
        var productId = await InsertPreMigrationProductAsync(database);
        // Arrange with raw SQL: the latest EF model already expects the new column.
        await using (var connection = database.CreateConnection())
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE dbo.Products SET Note = N'Existing business note';";
            await command.ExecuteNonQueryAsync();
        }

        await database.ExecuteScriptAsync(batches);

        await AssertAllMigrationsAppliedAsync(context);
        await AssertNoteColumnAsync(database);
        await AssertPracticeNoteColumnAbsentAsync(database);
        await AssertStorageLocationColumnAsync(database);
        await AssertProductAsync(context, productId, "Existing business note");
        var product = await context.Products.SingleAsync();
        product.StorageLocation = new string('x', 100);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        Assert.Equal(new string('x', 100), (await context.Products.SingleAsync()).StorageLocation);

        await database.ExecuteScriptAsync(batches);

        context.ChangeTracker.Clear();
        await AssertAllMigrationsAppliedAsync(context);
        await AssertProductAsync(context, productId, "Existing business note");
        Assert.Equal(new string('x', 100), (await context.Products.AsNoTracking().SingleAsync()).StorageLocation);
    }

    [SqlServerCiFact]
    public async Task GeneratedSql_StorageLocationUpgradePreservesRemainingNoteAndSupportsReplay()
    {
        var batches = MigrationSqlScript.ReadFromCiWorkspace();
        await using var database = await CiDatabase.CreateAsync("upgrade");
        await using var context = database.CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(AddPracticeNote);
        var productId = await InsertPreMigrationProductAsync(database);
        // Raw SQL arranges the historical schema, which lacks StorageLocation.
        await using (var connection = database.CreateConnection())
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE dbo.Products SET Note = N'Keep business note', CicdPracticeNote = N'Keep practice note';";
            await command.ExecuteNonQueryAsync();
        }

        await database.ExecuteScriptAsync(batches);
        await AssertAllMigrationsAppliedAsync(context);
        await AssertStorageLocationColumnAsync(database);
        await AssertProductAsync(context, productId, "Keep business note");
        var product = await context.Products.SingleAsync();
        Assert.Null(product.StorageLocation);
        await AssertPracticeNoteColumnAbsentAsync(database);
        var location = new string('位', 100);
        product.StorageLocation = location;
        await context.SaveChangesAsync();

        await database.ExecuteScriptAsync(batches);
        context.ChangeTracker.Clear();
        await AssertAllMigrationsAppliedAsync(context);
        await AssertProductAsync(context, productId, "Keep business note");
        product = await context.Products.SingleAsync();
        Assert.Equal(location, product.StorageLocation);
        await AssertPracticeNoteColumnAbsentAsync(database);
        product.StorageLocation = null;
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        Assert.Null((await context.Products.SingleAsync()).StorageLocation);
    }

    [SqlServerCiFact]
    public async Task GeneratedSql_RemovePracticeNote_NewApiWorksOnBothSchemasAndReplayPreservesRemainingData()
    {
        var batches = MigrationSqlScript.ReadFromCiWorkspace();
        await using var database = await CiDatabase.CreateAsync("upgrade");
        await using var context = database.CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(AddStorageLocation);
        var productId = await InsertPreMigrationProductAsync(database);
        await using (var connection = database.CreateConnection())
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE dbo.Products SET Note = N'Keep business note', CicdPracticeNote = N'Delete only this value', StorageLocation = N'Shelf A1';";
            await command.ExecuteNonQueryAsync();
        }
        var oldHistory = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        await AssertPracticeNoteColumnAsync(database);
        // Release A deploys this service while the old physical column still exists.
        var service = new ProductService(context);
        var product = (await service.GetByIdAsync(productId, CancellationToken.None))!;
        Assert.Equal("Shelf A1", product.StorageLocation);
        var request = new UpdateProductRequest
        {
            Name = product.Name, Description = product.Description,
            ProductCategoryId = product.ProductCategoryId, UnitOfMeasureId = product.UnitOfMeasureId,
            DefaultUnitPrice = product.DefaultUnitPrice, ReorderLevel = product.ReorderLevel,
            StorageLocation = "Shelf B2", IsActive = true
        };
        Assert.Equal(ProductSaveStatus.Success,
            (await service.UpdateAsync(productId, request, CancellationToken.None)).Status);
        await using (var connection = database.CreateConnection())
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT CicdPracticeNote FROM dbo.Products;";
            Assert.Equal("Delete only this value", await command.ExecuteScalarAsync());
        }

        // Release B uses the exact CI artifact to perform the destructive change.
        await database.ExecuteScriptAsync(batches);
        context.ChangeTracker.Clear();
        await AssertAllMigrationsAppliedAsync(context);
        var history = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.Equal(oldHistory.Length + 1, history.Length);
        Assert.EndsWith("_RemoveProductCicdPracticeNote", history.Last());
        await AssertPracticeNoteColumnAbsentAsync(database);
        await AssertNoteColumnAsync(database);
        await AssertStorageLocationColumnAsync(database);
        await AssertProductAsync(context, productId, "Keep business note");
        Assert.Equal("Shelf B2", (await service.GetByIdAsync(productId, CancellationToken.None))!.StorageLocation);
        Assert.Single(await service.GetAllAsync(false, CancellationToken.None));
        Assert.Equal(ProductSaveStatus.Success,
            (await service.UpdateAsync(productId, request, CancellationToken.None)).Status);
        Assert.False((await service.DeactivateAsync(productId, CancellationToken.None)).Product!.IsActive);
        Assert.True((await service.UpdateAsync(productId, request, CancellationToken.None)).Product!.IsActive);

        await database.ExecuteScriptAsync(batches);
        context.ChangeTracker.Clear();
        Assert.Equal(history, (await context.Database.GetAppliedMigrationsAsync()).ToArray());
        await AssertPracticeNoteColumnAbsentAsync(database);
        await AssertProductAsync(context, productId, "Keep business note");
        Assert.Equal("Shelf B2", (await context.Products.SingleAsync()).StorageLocation);
    }

    private static async Task AssertProductAsync(AppDbContext context, int productId, string? note)
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
