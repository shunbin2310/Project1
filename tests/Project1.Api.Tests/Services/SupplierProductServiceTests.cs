using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Project1.Api.Data;
using Project1.Api.DTOs.SupplierProducts;
using Project1.Api.Entities;
using Project1.Api.Services.SupplierProducts;

namespace Project1.Api.Tests.Services;

public sealed class SupplierProductServiceTests
{
    [Fact]
    public async Task CreateAsync_CreatesRelationshipWithRelatedDetails()
    {
        await using var testContext = await CreateContextAsync();
        var service = new SupplierProductService(testContext.DbContext);

        var result = await service.CreateAsync(
            new CreateSupplierProductRequest
            {
                SupplierId = 1,
                ProductId = 1,
                IsPreferred = true
            },
            CancellationToken.None);

        Assert.Equal(SupplierProductSaveStatus.Success, result.Status);
        Assert.NotNull(result.SupplierProduct);
        Assert.Equal("SUP-0001", result.SupplierProduct.SupplierCode);
        Assert.Equal("ITEM-0001", result.SupplierProduct.ProductCode);
        Assert.Equal("UNIT", result.SupplierProduct.UnitOfMeasureCode);
        Assert.True(result.SupplierProduct.IsPreferred);
    }

    [Fact]
    public async Task CreateAsync_RejectsDuplicateRelationship()
    {
        await using var testContext = await CreateContextAsync();
        var service = new SupplierProductService(testContext.DbContext);
        var request = new CreateSupplierProductRequest
        {
            SupplierId = 1,
            ProductId = 1
        };

        await service.CreateAsync(request, CancellationToken.None);
        var duplicate = await service.CreateAsync(request, CancellationToken.None);

        Assert.Equal(SupplierProductSaveStatus.DuplicateRelationship, duplicate.Status);
        Assert.Single(testContext.DbContext.SupplierProducts);
    }

    [Theory]
    [InlineData(999, 1, SupplierProductSaveStatus.SupplierUnavailable)]
    [InlineData(1, 999, SupplierProductSaveStatus.ProductUnavailable)]
    public async Task CreateAsync_RejectsUnavailableRelatedRecord(
        int supplierId,
        int productId,
        SupplierProductSaveStatus expectedStatus)
    {
        await using var testContext = await CreateContextAsync();
        var service = new SupplierProductService(testContext.DbContext);

        var result = await service.CreateAsync(
            new CreateSupplierProductRequest
            {
                SupplierId = supplierId,
                ProductId = productId
            },
            CancellationToken.None);

        Assert.Equal(expectedStatus, result.Status);
    }

    [Fact]
    public async Task GetAllAsync_FiltersBySupplierAndActiveAvailability()
    {
        await using var testContext = await CreateContextAsync();
        testContext.DbContext.SupplierProducts.AddRange(
            new SupplierProduct
            {
                SupplierId = 1,
                ProductId = 1,
                IsActive = true
            },
            new SupplierProduct
            {
                SupplierId = 1,
                ProductId = 2,
                IsActive = false
            },
            new SupplierProduct
            {
                SupplierId = 2,
                ProductId = 1,
                IsActive = true
            });
        await testContext.DbContext.SaveChangesAsync();
        var service = new SupplierProductService(testContext.DbContext);

        var active = await service.GetAllAsync(
            supplierId: 1,
            productId: null,
            includeInactive: false,
            CancellationToken.None);
        var all = await service.GetAllAsync(
            supplierId: 1,
            productId: null,
            includeInactive: true,
            CancellationToken.None);

        Assert.Single(active);
        Assert.Equal("ITEM-0001", active[0].ProductCode);
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task UpdateAsync_UpdatesPreferredAndActiveStatus()
    {
        await using var testContext = await CreateContextAsync();
        var supplierProduct = new SupplierProduct
        {
            SupplierId = 1,
            ProductId = 1
        };
        testContext.DbContext.SupplierProducts.Add(supplierProduct);
        await testContext.DbContext.SaveChangesAsync();
        var service = new SupplierProductService(testContext.DbContext);

        var result = await service.UpdateAsync(
            supplierProduct.Id,
            new UpdateSupplierProductRequest
            {
                IsPreferred = true,
                IsActive = false
            },
            CancellationToken.None);

        Assert.Equal(SupplierProductSaveStatus.Success, result.Status);
        Assert.True(result.SupplierProduct!.IsPreferred);
        Assert.False(result.SupplierProduct.IsActive);
        Assert.NotNull(result.SupplierProduct.UpdatedAtUtc);
    }

    [Fact]
    public async Task UpdateAsync_RejectsReactivationWhenSupplierIsInactive()
    {
        await using var testContext = await CreateContextAsync();
        var supplierProduct = new SupplierProduct
        {
            SupplierId = 2,
            ProductId = 1,
            IsActive = false
        };
        testContext.DbContext.SupplierProducts.Add(supplierProduct);
        await testContext.DbContext.SaveChangesAsync();
        var service = new SupplierProductService(testContext.DbContext);

        var result = await service.UpdateAsync(
            supplierProduct.Id,
            new UpdateSupplierProductRequest
            {
                IsActive = true
            },
            CancellationToken.None);

        Assert.Equal(SupplierProductSaveStatus.SupplierUnavailable, result.Status);
        Assert.False(supplierProduct.IsActive);
    }

    [Fact]
    public async Task DeleteAsync_RemovesRelationshipOnly()
    {
        await using var testContext = await CreateContextAsync();
        var supplierProduct = new SupplierProduct
        {
            SupplierId = 1,
            ProductId = 1
        };
        testContext.DbContext.SupplierProducts.Add(supplierProduct);
        await testContext.DbContext.SaveChangesAsync();
        var service = new SupplierProductService(testContext.DbContext);

        var result = await service.DeleteAsync(
            supplierProduct.Id,
            CancellationToken.None);

        Assert.Equal(SupplierProductSaveStatus.Success, result.Status);
        Assert.Empty(testContext.DbContext.SupplierProducts);
        Assert.Equal(2, testContext.DbContext.Suppliers.Count());
        Assert.Equal(2, testContext.DbContext.Products.Count());
    }

    private static async Task<TestContext> CreateContextAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        var dbContext = new AppDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var unit = new UnitOfMeasure
        {
            Id = 1,
            Code = "UNIT",
            Name = "Unit"
        };
        var category = new ProductCategory
        {
            Id = 1,
            Code = "CAT-0001",
            Name = "Electronics"
        };

        dbContext.Suppliers.AddRange(
            new Supplier
            {
                Id = 1,
                Code = "SUP-0001",
                Name = "Example Supplies"
            },
            new Supplier
            {
                Id = 2,
                Code = "SUP-0002",
                Name = "Inactive Supplies",
                IsActive = false
            });
        dbContext.Products.AddRange(
            new Product
            {
                Id = 1,
                Code = "ITEM-0001",
                Name = "Dell Monitor",
                ProductCategory = category,
                UnitOfMeasure = unit,
                DefaultUnitPrice = 1399.90m
            },
            new Product
            {
                Id = 2,
                Code = "ITEM-0002",
                Name = "Inactive Keyboard",
                ProductCategory = category,
                UnitOfMeasure = unit,
                DefaultUnitPrice = 99.90m,
                IsActive = false
            });
        await dbContext.SaveChangesAsync();

        return new TestContext(connection, dbContext);
    }

    private sealed class TestContext(
        SqliteConnection connection,
        AppDbContext dbContext) : IAsyncDisposable
    {
        public AppDbContext DbContext { get; } = dbContext;

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
