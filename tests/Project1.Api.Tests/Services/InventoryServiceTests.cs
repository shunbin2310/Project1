using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Project1.Api.Data;
using Project1.Api.Entities;
using Project1.Api.Services.Inventory;

namespace Project1.Api.Tests.Services;

public sealed class InventoryServiceTests
{
    [Fact]
    public async Task GetBalancesAsync_IncludesProductsWithoutMovementsAndCalculatesLowStock()
    {
        await using var fixture = await InventoryFixture.CreateAsync();

        var balances = await fixture.Service.GetBalancesAsync(
            productCategoryId: null,
            lowStock: null,
            includeInactive: false,
            search: null,
            CancellationToken.None);

        Assert.Equal(2, balances.Count);
        var monitor = balances.Single(balance => balance.ProductCode == "ITEM-0001");
        Assert.Equal(8m, monitor.QuantityOnHand);
        Assert.False(monitor.IsLowStock);

        var keyboard = balances.Single(balance => balance.ProductCode == "ITEM-0002");
        Assert.Equal(0m, keyboard.QuantityOnHand);
        Assert.True(keyboard.IsLowStock);
        Assert.Null(keyboard.LastUpdatedAtUtc);
    }

    [Fact]
    public async Task GetBalancesAsync_AppliesCategoryLowStockActiveAndSearchFilters()
    {
        await using var fixture = await InventoryFixture.CreateAsync();

        var lowStock = await fixture.Service.GetBalancesAsync(
            fixture.Category.Id,
            lowStock: true,
            includeInactive: false,
            search: "keyboard",
            CancellationToken.None);

        var result = Assert.Single(lowStock);
        Assert.Equal("ITEM-0002", result.ProductCode);
    }

    [Fact]
    public async Task GetTransactionsAsync_ReturnsFilteredLedgerInNewestFirstOrder()
    {
        await using var fixture = await InventoryFixture.CreateAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var transactions = await fixture.Service.GetTransactionsAsync(
            fixture.Monitor.Id,
            InventoryTransactionType.GoodsReceipt,
            today.AddDays(-1),
            today,
            CancellationToken.None);

        var transaction = Assert.Single(transactions);
        Assert.Equal(8m, transaction.QuantityChange);
        Assert.Equal(0m, transaction.QuantityBefore);
        Assert.Equal(8m, transaction.QuantityAfter);
        Assert.Equal("GRN-0001", transaction.ReferenceNumber);
    }

    private sealed class InventoryFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;

        private InventoryFixture(
            SqliteConnection connection,
            AppDbContext dbContext,
            ProductCategory category,
            Product monitor)
        {
            this.connection = connection;
            DbContext = dbContext;
            Category = category;
            Monitor = monitor;
            Service = new InventoryService(dbContext);
        }

        public AppDbContext DbContext { get; }

        public ProductCategory Category { get; }

        public Product Monitor { get; }

        public InventoryService Service { get; }

        public static async Task<InventoryFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;
            var dbContext = new AppDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();

            var category = new ProductCategory
            {
                Code = "CAT-0001",
                Name = "Electronics"
            };
            var unit = new UnitOfMeasure { Code = "UNIT", Name = "Unit" };
            var monitor = new Product
            {
                Code = "ITEM-0001",
                Name = "Monitor",
                ProductCategory = category,
                UnitOfMeasure = unit,
                DefaultUnitPrice = 1200m,
                ReorderLevel = 5m
            };
            var keyboard = new Product
            {
                Code = "ITEM-0002",
                Name = "Keyboard",
                ProductCategory = category,
                UnitOfMeasure = unit,
                DefaultUnitPrice = 100m,
                ReorderLevel = 3m
            };
            var inactiveMouse = new Product
            {
                Code = "ITEM-0003",
                Name = "Mouse",
                ProductCategory = category,
                UnitOfMeasure = unit,
                DefaultUnitPrice = 50m,
                ReorderLevel = 2m,
                IsActive = false
            };

            dbContext.Products.AddRange(monitor, keyboard, inactiveMouse);
            await dbContext.SaveChangesAsync();

            var occurredAtUtc = DateTimeOffset.UtcNow;
            dbContext.InventoryBalances.Add(new InventoryBalance
            {
                ProductId = monitor.Id,
                QuantityOnHand = 8m,
                LastUpdatedAtUtc = occurredAtUtc
            });
            dbContext.InventoryTransactions.AddRange(
                new InventoryTransaction
                {
                    ProductId = monitor.Id,
                    Type = InventoryTransactionType.GoodsReceipt,
                    QuantityChange = 8m,
                    QuantityBefore = 0m,
                    QuantityAfter = 8m,
                    ProductCode = monitor.Code,
                    ProductName = monitor.Name,
                    UnitOfMeasureCode = unit.Code,
                    ReferenceType = nameof(GoodsReceipt),
                    ReferenceId = 1,
                    ReferenceNumber = "GRN-0001",
                    PerformedByUserId = 4,
                    PerformedByName = "Demo Admin",
                    OccurredDate = DateOnly.FromDateTime(occurredAtUtc.UtcDateTime),
                    OccurredAtUtc = occurredAtUtc
                },
                new InventoryTransaction
                {
                    ProductId = monitor.Id,
                    Type = InventoryTransactionType.AdjustmentIncrease,
                    QuantityChange = 1m,
                    QuantityBefore = 8m,
                    QuantityAfter = 9m,
                    ProductCode = monitor.Code,
                    ProductName = monitor.Name,
                    UnitOfMeasureCode = unit.Code,
                    ReferenceType = "Adjustment",
                    ReferenceId = 2,
                    ReferenceNumber = "ADJ-0001",
                    PerformedByUserId = 4,
                    PerformedByName = "Demo Admin",
                    OccurredDate = DateOnly.FromDateTime(occurredAtUtc.AddDays(-10).UtcDateTime),
                    OccurredAtUtc = occurredAtUtc.AddDays(-10)
                });
            await dbContext.SaveChangesAsync();

            return new InventoryFixture(connection, dbContext, category, monitor);
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
