using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Project1.Api.Authentication;
using Project1.Api.Data;
using Project1.Api.DTOs.PurchaseOrders;
using Project1.Api.Entities;
using Project1.Api.Services.Authentication;
using Project1.Api.Services.PurchaseOrders;

namespace Project1.Api.Tests.Services;

public sealed class PurchaseOrderServiceTests
{
    [Fact]
    public async Task CreateAsync_CopiesSelectedQuotationIntoDraftSnapshot()
    {
        await using var fixture = await PurchaseOrderFixture.CreateAsync();

        var result = await fixture.Service.CreateAsync(
            fixture.ValidRequest(),
            CancellationToken.None);

        Assert.Equal(PurchaseOrderOperationStatus.Success, result.Status);
        Assert.NotNull(result.PurchaseOrder);
        Assert.StartsWith("PO-", result.PurchaseOrder.PurchaseOrderNumber);
        Assert.Equal(PurchaseOrderStatus.Draft, result.PurchaseOrder.Status);
        Assert.Equal("QT-0001", result.PurchaseOrder.QuotationNumber);
        Assert.Equal("PR-0001", result.PurchaseOrder.PurchaseRequestNumber);
        Assert.Equal("SUP-0001", result.PurchaseOrder.SupplierCode);
        Assert.Equal("Supplier One", result.PurchaseOrder.SupplierName);
        Assert.Equal("Demo Admin", result.PurchaseOrder.CreatedByName);
        Assert.Equal(200m, result.PurchaseOrder.TotalAmount);
        var item = Assert.Single(result.PurchaseOrder.Items);
        Assert.Equal("ITEM-0001", item.ProductCode);
        Assert.Equal(2m, item.Quantity);
        Assert.Equal(100m, item.UnitPrice);

        fixture.Supplier.Name = "Supplier Renamed";
        fixture.Product.Name = "Product Renamed";
        await fixture.DbContext.SaveChangesAsync();
        var snapshot = await fixture.Service.GetByIdAsync(
            result.PurchaseOrder.Id,
            CancellationToken.None);

        Assert.Equal("Supplier One", snapshot!.SupplierName);
        Assert.Equal("Monitor", snapshot.Items[0].ProductName);
    }

    [Fact]
    public async Task CreateAsync_RejectsQuotationThatIsNotSelected()
    {
        await using var fixture = await PurchaseOrderFixture.CreateAsync();
        fixture.Quotation.Status = QuotationStatus.Submitted;
        await fixture.DbContext.SaveChangesAsync();

        var result = await fixture.Service.CreateAsync(
            fixture.ValidRequest(),
            CancellationToken.None);

        Assert.Equal(PurchaseOrderOperationStatus.QuotationNotSelected, result.Status);
        Assert.Empty(fixture.DbContext.PurchaseOrders);
    }

    [Fact]
    public async Task CreateAsync_RejectsSecondOrderForSameQuotation()
    {
        await using var fixture = await PurchaseOrderFixture.CreateAsync();
        await fixture.Service.CreateAsync(fixture.ValidRequest(), CancellationToken.None);

        var duplicate = await fixture.Service.CreateAsync(
            fixture.ValidRequest(),
            CancellationToken.None);

        Assert.Equal(PurchaseOrderOperationStatus.DuplicatePurchaseOrder, duplicate.Status);
        Assert.Single(fixture.DbContext.PurchaseOrders);
    }

    [Fact]
    public async Task UpdateAsync_UpdatesDraftButRejectsIssuedOrder()
    {
        await using var fixture = await PurchaseOrderFixture.CreateAsync();
        var created = await fixture.Service.CreateAsync(
            fixture.ValidRequest(),
            CancellationToken.None);
        var id = created.PurchaseOrder!.Id;
        var update = new UpdatePurchaseOrderRequest
        {
            OrderDate = fixture.Today,
            ExpectedDeliveryDate = fixture.Today.AddDays(21),
            DeliveryAddress = "  Updated warehouse  ",
            Notes = "  Handle with care.  "
        };

        var updated = await fixture.Service.UpdateAsync(id, update, CancellationToken.None);
        await fixture.Service.IssueAsync(id, CancellationToken.None);
        var rejected = await fixture.Service.UpdateAsync(id, update, CancellationToken.None);

        Assert.Equal(PurchaseOrderOperationStatus.Success, updated.Status);
        Assert.Equal("Updated warehouse", updated.PurchaseOrder!.DeliveryAddress);
        Assert.Equal("Handle with care.", updated.PurchaseOrder.Notes);
        Assert.Equal(PurchaseOrderOperationStatus.InvalidState, rejected.Status);
    }

    [Fact]
    public async Task IssueAsync_RequiresDeliveryDetailsAndRecordsIssuer()
    {
        await using var fixture = await PurchaseOrderFixture.CreateAsync();
        var request = new CreatePurchaseOrderRequest
        {
            QuotationId = fixture.Quotation.Id,
            OrderDate = fixture.Today,
            ExpectedDeliveryDate = null,
            DeliveryAddress = null
        };
        var created = await fixture.Service.CreateAsync(request, CancellationToken.None);

        var rejected = await fixture.Service.IssueAsync(
            created.PurchaseOrder!.Id,
            CancellationToken.None);
        await fixture.Service.UpdateAsync(
            created.PurchaseOrder.Id,
            new UpdatePurchaseOrderRequest
            {
                OrderDate = fixture.Today,
                ExpectedDeliveryDate = fixture.Today.AddDays(14),
                DeliveryAddress = "Main warehouse"
            },
            CancellationToken.None);
        var issued = await fixture.Service.IssueAsync(
            created.PurchaseOrder.Id,
            CancellationToken.None);

        Assert.Equal(PurchaseOrderOperationStatus.ValidationFailed, rejected.Status);
        Assert.Equal(PurchaseOrderOperationStatus.Success, issued.Status);
        Assert.Equal(PurchaseOrderStatus.Issued, issued.PurchaseOrder!.Status);
        Assert.Equal(4, issued.PurchaseOrder.IssuedByUserId);
        Assert.Equal("Demo Admin", issued.PurchaseOrder.IssuedByName);
        Assert.NotNull(issued.PurchaseOrder.IssuedAtUtc);
    }

    [Fact]
    public async Task CancelAsync_OnlyCancelsIssuedOrderWithReason()
    {
        await using var fixture = await PurchaseOrderFixture.CreateAsync();
        var created = await fixture.Service.CreateAsync(
            fixture.ValidRequest(),
            CancellationToken.None);
        var id = created.PurchaseOrder!.Id;

        var draftResult = await fixture.Service.CancelAsync(
            id,
            new CancelPurchaseOrderRequest { Reason = "Supplier unavailable." },
            CancellationToken.None);
        await fixture.Service.IssueAsync(id, CancellationToken.None);
        var missingReason = await fixture.Service.CancelAsync(
            id,
            new CancelPurchaseOrderRequest(),
            CancellationToken.None);
        var cancelled = await fixture.Service.CancelAsync(
            id,
            new CancelPurchaseOrderRequest { Reason = "  Supplier unavailable.  " },
            CancellationToken.None);

        Assert.Equal(PurchaseOrderOperationStatus.InvalidState, draftResult.Status);
        Assert.Equal(PurchaseOrderOperationStatus.ValidationFailed, missingReason.Status);
        Assert.Equal(PurchaseOrderOperationStatus.Success, cancelled.Status);
        Assert.Equal(PurchaseOrderStatus.Cancelled, cancelled.PurchaseOrder!.Status);
        Assert.Equal("Supplier unavailable.", cancelled.PurchaseOrder.CancellationReason);
        Assert.Equal("Demo Admin", cancelled.PurchaseOrder.CancelledByName);
    }

    [Fact]
    public async Task DeleteAsync_AllowsDraftButRejectsIssuedOrder()
    {
        await using var fixture = await PurchaseOrderFixture.CreateAsync();
        var first = await fixture.Service.CreateAsync(
            fixture.ValidRequest(),
            CancellationToken.None);

        var deleted = await fixture.Service.DeleteAsync(
            first.PurchaseOrder!.Id,
            CancellationToken.None);
        var second = await fixture.Service.CreateAsync(
            fixture.ValidRequest(),
            CancellationToken.None);
        await fixture.Service.IssueAsync(second.PurchaseOrder!.Id, CancellationToken.None);
        var rejected = await fixture.Service.DeleteAsync(
            second.PurchaseOrder.Id,
            CancellationToken.None);

        Assert.Equal(PurchaseOrderOperationStatus.Success, deleted.Status);
        Assert.Equal(PurchaseOrderOperationStatus.InvalidState, rejected.Status);
        Assert.Single(fixture.DbContext.PurchaseOrders);
    }

    private sealed class PurchaseOrderFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;

        private PurchaseOrderFixture(
            SqliteConnection connection,
            AppDbContext dbContext,
            Supplier supplier,
            Product product,
            Quotation quotation)
        {
            this.connection = connection;
            DbContext = dbContext;
            Supplier = supplier;
            Product = product;
            Quotation = quotation;
            Service = new PurchaseOrderService(dbContext, new FakeCurrentUserContext());
        }

        public AppDbContext DbContext { get; }

        public PurchaseOrderService Service { get; }

        public Supplier Supplier { get; }

        public Product Product { get; }

        public Quotation Quotation { get; }

        public DateOnly Today { get; } = DateOnly.FromDateTime(DateTime.UtcNow);

        public static async Task<PurchaseOrderFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;
            var dbContext = new AppDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();

            var category = new ProductCategory { Code = "CAT-TEST", Name = "Test Category" };
            var unit = new UnitOfMeasure { Code = "UNIT", Name = "Unit" };
            var product = new Product
            {
                Code = "ITEM-0001",
                Name = "Monitor",
                ProductCategory = category,
                UnitOfMeasure = unit,
                DefaultUnitPrice = 120m
            };
            var supplier = new Supplier { Code = "SUP-0001", Name = "Supplier One" };
            var purchaseRequestItem = new PurchaseRequestItem
            {
                Product = product,
                Quantity = 2m,
                EstimatedUnitPrice = 120m
            };
            var purchaseRequest = new PurchaseRequest
            {
                RequestNumber = "PR-0001",
                RequesterName = "Demo Requester",
                Items = [purchaseRequestItem]
            };
            var quotation = new Quotation
            {
                QuotationNumber = "QT-0001",
                PurchaseRequest = purchaseRequest,
                Supplier = supplier,
                SupplierCode = supplier.Code,
                SupplierName = supplier.Name,
                SupplierQuotationReference = "SUP-Q-001",
                QuotationDate = DateOnly.FromDateTime(DateTime.UtcNow),
                ValidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
                Status = QuotationStatus.Selected,
                CreatedByUserId = 4,
                CreatedByName = "Demo Admin",
                SubmittedAtUtc = DateTimeOffset.UtcNow,
                SelectedAtUtc = DateTimeOffset.UtcNow,
                Items =
                [
                    new QuotationItem
                    {
                        PurchaseRequestItem = purchaseRequestItem,
                        Product = product,
                        ProductCode = product.Code,
                        ProductName = product.Name,
                        UnitOfMeasureCode = unit.Code,
                        Quantity = 2m,
                        UnitPrice = 100m
                    }
                ]
            };

            dbContext.Quotations.Add(quotation);
            await dbContext.SaveChangesAsync();

            return new PurchaseOrderFixture(connection, dbContext, supplier, product, quotation);
        }

        public CreatePurchaseOrderRequest ValidRequest() => new()
        {
            QuotationId = Quotation.Id,
            OrderDate = Today,
            ExpectedDeliveryDate = Today.AddDays(14),
            DeliveryAddress = "Main warehouse",
            Notes = "Deliver during office hours."
        };

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class FakeCurrentUserContext : ICurrentUserContext
    {
        public bool IsAuthenticated => true;

        public int UserId => 4;

        public string DisplayName => "Demo Admin";

        public int? DepartmentId => 1;

        public IReadOnlyCollection<string> Roles => [ApplicationRoles.Admin];

        public bool IsInRole(string role) =>
            string.Equals(role, ApplicationRoles.Admin, StringComparison.OrdinalIgnoreCase);
    }
}
