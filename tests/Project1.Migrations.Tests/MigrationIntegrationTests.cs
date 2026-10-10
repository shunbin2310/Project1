using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Project1.Migrations.Tests;

public sealed class MigrationIntegrationTests
{
    internal const string BeforeNote = "20260930150622_AddEmailAttachments";
    internal const string AddNote = "20261009164508_AddProductExampleAttr";
    internal static readonly DateTimeOffset FixtureCreatedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [SqlServerCiFact]
    public async Task EmptyDatabase_AllMigrationsApplyAndNoteColumnExists()
    {
        await using var database = await CiDatabase.CreateAsync("fresh");
        await using var context = database.CreateContext();

        await context.Database.MigrateAsync();

        await AssertAllMigrationsAppliedAsync(context);
        await AssertNoteColumnAsync(database);
        await AssertPracticeNoteColumnAsync(database);
        Assert.Empty(await context.Products.AsNoTracking().ToListAsync());
    }

    [SqlServerCiFact]
    public async Task ExistingDatabase_UpgradePreservesProductAndNoteCanBeSaved()
    {
        await using var database = await CiDatabase.CreateAsync("upgrade");
        await using var context = database.CreateContext();
        var migrations = context.Database.GetMigrations().ToArray();
        Assert.Contains(BeforeNote, migrations);
        Assert.Contains(AddNote, migrations);

        await context.GetService<IMigrator>().MigrateAsync(BeforeNote);
        Assert.DoesNotContain(AddNote, await context.Database.GetAppliedMigrationsAsync());
        var productId = await InsertPreMigrationProductAsync(database);

        await context.Database.MigrateAsync();

        await AssertAllMigrationsAppliedAsync(context);
        await AssertNoteColumnAsync(database);
        await AssertPracticeNoteColumnAsync(database);
        Assert.Equal(1, await context.Products.CountAsync());
        var product = await context.Products.SingleAsync();
        Assert.Equal(productId, product.Id);
        Assert.Equal("CI-PRODUCT", product.Code);
        Assert.Equal("Product created before Note", product.Name);
        Assert.Equal("Existing description must survive", product.Description);
        Assert.Equal(12.50m, product.DefaultUnitPrice);
        Assert.Equal(3m, product.ReorderLevel);
        Assert.Equal(FixtureCreatedAt, product.CreatedAtUtc);
        Assert.True(product.IsActive);
        Assert.Null(product.Note);
        Assert.Null(product.CicdPracticeNote);
        Assert.Equal("CI-CATEGORY", (await context.ProductCategories.SingleAsync()).Code);
        Assert.Equal("CI-UNIT", (await context.UnitsOfMeasure.SingleAsync()).Code);

        product.Note = "Note written after the CI migration";
        product.CicdPracticeNote = new string('x', 100);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        Assert.Equal("Note written after the CI migration", (await context.Products.SingleAsync()).Note);

        // Applying the same migrations again must not duplicate records or erase data.
        await context.Database.MigrateAsync();
        await AssertAllMigrationsAppliedAsync(context);
        Assert.Equal(1, await context.Products.CountAsync());
        Assert.Equal(new string('x', 100), (await context.Products.AsNoTracking().SingleAsync()).CicdPracticeNote);
    }

    internal static async Task AssertAllMigrationsAppliedAsync(Project1.Api.Data.AppDbContext context)
    {
        var expected = context.Database.GetMigrations().ToArray();
        Assert.NotEmpty(expected);
        Assert.Equal(expected, (await context.Database.GetAppliedMigrationsAsync()).ToArray());
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
    }

    internal static async Task AssertNoteColumnAsync(CiDatabase database)
    {
        await using var connection = database.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM sys.columns AS c
            JOIN sys.types AS t ON c.user_type_id = t.user_type_id
            WHERE c.object_id = OBJECT_ID(N'dbo.Products') AND c.name = N'Note'
              AND t.name = N'nvarchar' AND c.max_length = -1 AND c.is_nullable = 1;
            """;
        Assert.Equal(1, Convert.ToInt32(await command.ExecuteScalarAsync()));
    }

    internal static async Task AssertPracticeNoteColumnAsync(CiDatabase database)
    {
        await using var connection = database.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM sys.columns AS c
            JOIN sys.types AS t ON c.user_type_id = t.user_type_id
            WHERE c.object_id = OBJECT_ID(N'dbo.Products') AND c.name = N'CicdPracticeNote'
              AND t.name = N'nvarchar' AND c.max_length = 200 AND c.is_nullable = 1;
            """;
        Assert.Equal(1, Convert.ToInt32(await command.ExecuteScalarAsync()));
    }

    internal static async Task<int> InsertPreMigrationProductAsync(CiDatabase database)
    {
        await using var connection = database.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        // Raw SQL deliberately does not mention Note: the baseline schema lacks it.
        command.CommandText = """
            INSERT INTO dbo.ProductCategories (Code, Name, IsActive, CreatedAtUtc)
            VALUES (N'CI-CATEGORY', N'CI test category', 1, @createdAt);
            DECLARE @categoryId int = CONVERT(int, SCOPE_IDENTITY());
            INSERT INTO dbo.UnitsOfMeasure (Code, Name, IsActive, CreatedAtUtc)
            VALUES (N'CI-UNIT', N'CI test unit', 1, @createdAt);
            DECLARE @unitId int = CONVERT(int, SCOPE_IDENTITY());
            INSERT INTO dbo.Products
                (Code, Name, Description, ProductCategoryId, UnitOfMeasureId,
                 DefaultUnitPrice, ReorderLevel, IsActive, CreatedAtUtc)
            VALUES
                (N'CI-PRODUCT', N'Product created before Note', N'Existing description must survive',
                 @categoryId, @unitId, 12.50, 3, 1, @createdAt);
            SELECT CONVERT(int, SCOPE_IDENTITY());
            """;
        command.Parameters.Add("@createdAt", SqlDbType.DateTimeOffset).Value = FixtureCreatedAt;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
