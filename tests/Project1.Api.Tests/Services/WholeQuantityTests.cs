using System.ComponentModel.DataAnnotations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Project1.Api.Data;
using Project1.Api.DTOs.GoodsReceipts;
using Project1.Api.DTOs.Products;
using Project1.Api.DTOs.PurchaseRequests;
using Project1.Api.Entities;
using Project1.Api.Services.Products;

namespace Project1.Api.Tests.Services;

public sealed class WholeQuantityTests
{
    [Theory]
    [InlineData("1.004", false)]
    [InlineData("0.001", false)]
    [InlineData("-1", false)]
    [InlineData("0", true)]
    [InlineData("1", true)]
    [InlineData("1000", true)]
    [InlineData("999999999999999", true)]
    [InlineData("1000000000000000", false)]
    public void DraftAndReorderInputs_RequireNonNegativeWholeQuantities(string input, bool valid)
    {
        var quantity = decimal.Parse(input, System.Globalization.CultureInfo.InvariantCulture);
        object[] requests =
        [
            new PurchaseRequestItemRequest { ProductId = 1, Quantity = quantity },
            new CreateProductRequest { Name = "Monitor", ReorderLevel = quantity, DefaultUnitPrice = 12.50m },
            new UpdateProductRequest { Name = "Monitor", ReorderLevel = quantity, DefaultUnitPrice = 12.50m }
        ];
        foreach (var request in requests)
        {
            Assert.Equal(valid, Validator.TryValidateObject(request, new ValidationContext(request), [], true));
        }
    }

    [Theory]
    [InlineData("1.004", false)]
    [InlineData("0", false)]
    [InlineData("1", true)]
    [InlineData("4", true)]
    public void ReceiptInput_RequiresPositiveWholeQuantity(string input, bool valid)
    {
        var request = new GoodsReceiptItemInput
        {
            PurchaseOrderItemId = 1,
            QuantityReceived = decimal.Parse(input, System.Globalization.CultureInfo.InvariantCulture)
        };
        Assert.Equal(valid, Validator.TryValidateObject(request, new ValidationContext(request), [], true));
    }

    [Fact]
    public async Task ProductService_RejectsFractionalReorderLevelWithoutSaving()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        var service = new ProductService(context);
        var rejected = await service.CreateAsync(new CreateProductRequest
        {
            Name = "Monitor", ReorderLevel = 1.004m
        }, CancellationToken.None);
        Assert.Equal(ProductSaveStatus.InvalidReorderLevel, rejected.Status);
        Assert.Empty(await context.Products.ToListAsync());

        var created = await service.CreateAsync(new CreateProductRequest
        {
            Name = "Monitor", ReorderLevel = 2, DefaultUnitPrice = 12.50m
        }, CancellationToken.None);
        Assert.Equal(ProductSaveStatus.Success, created.Status);
        var updated = await service.UpdateAsync(created.Product!.Id, new UpdateProductRequest
        {
            Name = "Monitor", ReorderLevel = 2.5m
        }, CancellationToken.None);
        Assert.Equal(ProductSaveStatus.InvalidReorderLevel, updated.Status);
        context.ChangeTracker.Clear();
        var stored = await context.Products.SingleAsync();
        Assert.Equal(2m, stored.ReorderLevel);
        Assert.Equal(12.50m, stored.DefaultUnitPrice);
    }

    [Fact]
    public async Task Database_RejectsFractionalProductAndInventoryQuantities()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        var product = new Product { Code = "WHOLE", Name = "Whole quantity", ReorderLevel = 2 };
        context.Products.Add(product);
        await context.SaveChangesAsync();
        context.InventoryBalances.Add(new InventoryBalance { Product = product, QuantityOnHand = 4 });
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<SqliteException>(() => context.Database.ExecuteSqlRawAsync(
            "UPDATE Products SET ReorderLevel = 1.004"));
        await Assert.ThrowsAsync<SqliteException>(() => context.Database.ExecuteSqlRawAsync(
            "UPDATE InventoryBalances SET QuantityOnHand = 2.5"));
        context.ChangeTracker.Clear();
        Assert.Equal(2m, (await context.Products.SingleAsync()).ReorderLevel);
        Assert.Equal(4m, (await context.InventoryBalances.SingleAsync()).QuantityOnHand);
    }
}
