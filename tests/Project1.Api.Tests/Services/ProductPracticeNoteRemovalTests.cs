using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Project1.Api.Data;
using Project1.Api.DTOs.Products;
using Project1.Api.Entities;
using Project1.Api.Services.Products;

namespace Project1.Api.Tests.Services;

public sealed class ProductPracticeNoteRemovalTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NewService_WorksBeforeAndAfterPhysicalColumnRemoval(bool legacyColumnExists)
    {
        // Only an isolated in-memory database, never Project1Db.
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        if (legacyColumnExists)
            await context.Database.ExecuteSqlRawAsync("ALTER TABLE Products ADD CicdPracticeNote TEXT NULL;");

        Assert.Null(context.Model.FindEntityType(typeof(Product))!.FindProperty("CicdPracticeNote"));
        await context.SaveChangesAsync();
        var service = new ProductService(context);
        var created = await service.CreateAsync(new CreateProductRequest
        {
            Name = "Practice product",
            StorageLocation = " Shelf A1 "
        }, CancellationToken.None);
        Assert.Equal(ProductSaveStatus.Success, created.Status);
        var id = created.Product!.Id;
        Assert.Equal("Shelf A1", created.Product.StorageLocation);
        Assert.DoesNotContain("CicdPracticeNote", JsonSerializer.Serialize(created.Product));
        var stored = await context.Products.SingleAsync();
        stored.Note = "Keep business note";
        await context.SaveChangesAsync();
        if (legacyColumnExists)
            await context.Database.ExecuteSqlRawAsync("UPDATE Products SET CicdPracticeNote = 'Retained until manual deletion';");

        context.ChangeTracker.Clear();
        Assert.Equal(id, (await service.GetByIdAsync(id, CancellationToken.None))!.Id);
        Assert.Equal(id, Assert.Single(await service.GetAllAsync(false, CancellationToken.None)).Id);
        var updated = await service.UpdateAsync(id, new UpdateProductRequest
        {
            Name = "Updated product",
            StorageLocation = " Shelf B2 ", IsActive = true
        }, CancellationToken.None);
        Assert.Equal(ProductSaveStatus.Success, updated.Status);
        Assert.Equal("Shelf B2", updated.Product!.StorageLocation);
        var deactivated = await service.DeactivateAsync(id, CancellationToken.None);
        Assert.False(deactivated.Product!.IsActive);
        Assert.Empty(await service.GetAllAsync(false, CancellationToken.None));
        Assert.Single(await service.GetAllAsync(true, CancellationToken.None));
        context.ChangeTracker.Clear();
        stored = await context.Products.SingleAsync();
        Assert.Equal("Keep business note", stored.Note);
        Assert.Equal("Shelf B2", stored.StorageLocation);

        if (legacyColumnExists)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT CicdPracticeNote FROM Products;";
            Assert.Equal("Retained until manual deletion", await command.ExecuteScalarAsync());
        }
    }

    [Fact]
    public void SqlServerQueriesAndRequests_DoNotDependOnRetiredColumn()
    {
        using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=tcp:127.0.0.1,1;Database=QueryOnly;User Id=ci;Password=not-a-real-password")
            .Options);
        // ToQueryString generates text only; it does not connect to SQL Server.
        Assert.DoesNotContain("CicdPracticeNote", context.Products.ToQueryString());
        Assert.Null(typeof(CreateProductRequest).GetProperty("CicdPracticeNote"));
        Assert.Null(typeof(UpdateProductRequest).GetProperty("CicdPracticeNote"));
        Assert.Null(typeof(ProductResponse).GetProperty("CicdPracticeNote"));
    }
}
