using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Project1.Api.Data;
using Project1.Api.Entities;
using Project1.Api.Services.Products;

namespace Project1.Api.Tests.Services;

public sealed class ProductServiceTests
{
    [Fact]
    public async Task GetAllAsync_ReturnsProductsOrderedByCode()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new AppDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        dbContext.Products.AddRange(
            new Product
            {
                Code = "ITEM-0002",
                Name = "Stapler",
            },
            new Product
            {
                Code = "ITEM-0001",
                Name = "A4 Paper",
            });
        await dbContext.SaveChangesAsync();

        var service = new ProductService(dbContext);

        var products = await service.GetAllAsync(
            includeInactive: false,
            CancellationToken.None);

        Assert.Equal(["ITEM-0001", "ITEM-0002"], products.Select(product => product.Code));
    }
}
