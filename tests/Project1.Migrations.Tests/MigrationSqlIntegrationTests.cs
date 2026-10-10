using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Project1.Api.Data;
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
        await AssertPracticeNoteColumnAsync(database);
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
        await AssertPracticeNoteColumnAsync(database);
        await AssertProductAsync(context, productId, null);
        Assert.Null((await context.Products.AsNoTracking().SingleAsync()).CicdPracticeNote);
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
        await AssertPracticeNoteColumnAsync(database);
        await AssertProductAsync(context, productId, product.Note);
    }

    [SqlServerCiFact]
    public async Task GeneratedSql_CurrentDatabaseUpgradePreservesBusinessNoteAndPracticeValue()
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
        await AssertPracticeNoteColumnAsync(database);
        await AssertProductAsync(context, productId, "Existing business note");
        var product = await context.Products.SingleAsync();
        Assert.Null(product.CicdPracticeNote);
        product.CicdPracticeNote = new string('x', 100);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        Assert.Equal(new string('x', 100), (await context.Products.SingleAsync()).CicdPracticeNote);

        await database.ExecuteScriptAsync(batches);

        context.ChangeTracker.Clear();
        await AssertAllMigrationsAppliedAsync(context);
        await AssertProductAsync(context, productId, "Existing business note");
        Assert.Equal(new string('x', 100), (await context.Products.AsNoTracking().SingleAsync()).CicdPracticeNote);
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
