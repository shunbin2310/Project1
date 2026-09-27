using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Project1.Api.Authentication;
using Project1.Api.Data;
using Project1.Api.DTOs.GoodsReceipts;
using Project1.Api.Entities;
using Project1.Api.Services.Authentication;
using Project1.Api.Services.GoodsReceipts;

namespace Project1.Api.Tests.Services;

public sealed class GoodsReceiptServiceTests
{
    [Fact]
    public async Task CreateAsync_CreatesDraftWithOrderSnapshotsAndAudit()
    {
        await using var fixture = await GoodsReceiptFixture.CreateAsync();

        var result = await fixture.Service.CreateAsync(
            fixture.Request((fixture.FirstOrderItem.Id, 6m)),
            CancellationToken.None);

        Assert.Equal(GoodsReceiptOperationStatus.Success, result.Status);
        Assert.Equal("GRN-0001", result.GoodsReceipt!.GoodsReceiptNumber);
        Assert.Equal(GoodsReceiptStatus.Draft, result.GoodsReceipt.Status);
        Assert.Equal(fixture.PurchaseOrder.PurchaseOrderNumber, result.GoodsReceipt.PurchaseOrderNumber);
        Assert.Equal("Supplier One", result.GoodsReceipt.SupplierName);
        Assert.Equal("DN-001", result.GoodsReceipt.SupplierDeliveryNoteNumber);
        Assert.Equal("Demo Admin", result.GoodsReceipt.CreatedByName);
        var item = Assert.Single(result.GoodsReceipt.Items);
        Assert.Equal("ITEM-0001", item.ProductCode);
        Assert.Equal(10m, item.OrderedQuantity);
        Assert.Equal(6m, item.QuantityReceived);
    }

    [Fact]
    public async Task CreateAsync_RejectsPurchaseOrderThatIsNotIssued()
    {
        await using var fixture = await GoodsReceiptFixture.CreateAsync();
        fixture.PurchaseOrder.Status = PurchaseOrderStatus.Draft;
        await fixture.DbContext.SaveChangesAsync();

        var result = await fixture.Service.CreateAsync(
            fixture.Request((fixture.FirstOrderItem.Id, 1m)),
            CancellationToken.None);

        Assert.Equal(GoodsReceiptOperationStatus.InvalidState, result.Status);
        Assert.Empty(fixture.DbContext.GoodsReceipts);
    }

    [Fact]
    public async Task CreateAsync_RejectsSecondDraftForSamePurchaseOrder()
    {
        await using var fixture = await GoodsReceiptFixture.CreateAsync();
        await fixture.Service.CreateAsync(
            fixture.Request((fixture.FirstOrderItem.Id, 1m)),
            CancellationToken.None);

        var duplicate = await fixture.Service.CreateAsync(
            fixture.Request((fixture.FirstOrderItem.Id, 2m)),
            CancellationToken.None);

        Assert.Equal(GoodsReceiptOperationStatus.DuplicateDraft, duplicate.Status);
        Assert.Single(fixture.DbContext.GoodsReceipts);
    }

    [Fact]
    public async Task UpdateAsync_UpdatesDraftButRejectsPostedReceipt()
    {
        await using var fixture = await GoodsReceiptFixture.CreateAsync();
        var created = await fixture.Service.CreateAsync(
            fixture.Request((fixture.FirstOrderItem.Id, 2m)),
            CancellationToken.None);
        var id = created.GoodsReceipt!.Id;
        var update = new UpdateGoodsReceiptRequest
        {
            SupplierDeliveryNoteNumber = "  DN-UPDATED  ",
            ReceivedDate = fixture.Today,
            Notes = "  Boxes inspected.  ",
            Items =
            [
                new GoodsReceiptItemInput
                {
                    PurchaseOrderItemId = fixture.FirstOrderItem.Id,
                    QuantityReceived = 3m
                }
            ]
        };

        var updated = await fixture.Service.UpdateAsync(id, update, CancellationToken.None);
        await fixture.Service.PostAsync(id, CancellationToken.None);
        var rejected = await fixture.Service.UpdateAsync(id, update, CancellationToken.None);

        Assert.Equal(GoodsReceiptOperationStatus.Success, updated.Status);
        Assert.Equal("DN-UPDATED", updated.GoodsReceipt!.SupplierDeliveryNoteNumber);
        Assert.Equal("Boxes inspected.", updated.GoodsReceipt.Notes);
        Assert.Equal(3m, Assert.Single(updated.GoodsReceipt.Items).QuantityReceived);
        Assert.Equal(GoodsReceiptOperationStatus.InvalidState, rejected.Status);
    }

    [Fact]
    public async Task PostAsync_RecordsAuditAndMarksOrderPartiallyReceived()
    {
        await using var fixture = await GoodsReceiptFixture.CreateAsync();
        var created = await fixture.Service.CreateAsync(
            fixture.Request(
                (fixture.FirstOrderItem.Id, 6m),
                (fixture.SecondOrderItem.Id, 5m)),
            CancellationToken.None);

        var posted = await fixture.Service.PostAsync(
            created.GoodsReceipt!.Id,
            CancellationToken.None);

        Assert.Equal(GoodsReceiptOperationStatus.Success, posted.Status);
        Assert.Equal(GoodsReceiptStatus.Posted, posted.GoodsReceipt!.Status);
        Assert.Equal(4, posted.GoodsReceipt.PostedByUserId);
        Assert.Equal("Demo Admin", posted.GoodsReceipt.PostedByName);
        Assert.NotNull(posted.GoodsReceipt.PostedAtUtc);
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, fixture.PurchaseOrder.Status);

        var balances = await fixture.DbContext.InventoryBalances
            .OrderBy(balance => balance.ProductId)
            .ToListAsync();
        Assert.Equal(2, balances.Count);
        Assert.Equal(6m, balances[0].QuantityOnHand);
        Assert.Equal(5m, balances[1].QuantityOnHand);

        var transactions = await fixture.DbContext.InventoryTransactions
            .OrderBy(transaction => transaction.ProductId)
            .ToListAsync();
        Assert.Equal(2, transactions.Count);
        Assert.All(transactions, transaction =>
            Assert.Equal(InventoryTransactionType.GoodsReceipt, transaction.Type));
        Assert.Equal(0m, transactions[0].QuantityBefore);
        Assert.Equal(6m, transactions[0].QuantityAfter);
        Assert.Equal("GRN-0001", transactions[0].ReferenceNumber);
        Assert.Equal("Demo Admin", transactions[0].PerformedByName);
    }

    [Fact]
    public async Task PostingMultipleReceipts_CompletesOrderAndPreventsOverReceipt()
    {
        await using var fixture = await GoodsReceiptFixture.CreateAsync();
        var first = await fixture.Service.CreateAsync(
            fixture.Request(
                (fixture.FirstOrderItem.Id, 6m),
                (fixture.SecondOrderItem.Id, 5m)),
            CancellationToken.None);
        await fixture.Service.PostAsync(first.GoodsReceipt!.Id, CancellationToken.None);

        var excessive = await fixture.Service.CreateAsync(
            fixture.RequestWithDeliveryNote(
                "DN-002",
                (fixture.FirstOrderItem.Id, 5m)),
            CancellationToken.None);

        Assert.Equal(GoodsReceiptOperationStatus.QuantityExceeded, excessive.Status);

        var second = await fixture.Service.CreateAsync(
            fixture.RequestWithDeliveryNote(
                "DN-002",
                (fixture.FirstOrderItem.Id, 4m)),
            CancellationToken.None);
        var completed = await fixture.Service.PostAsync(
            second.GoodsReceipt!.Id,
            CancellationToken.None);

        Assert.Equal(GoodsReceiptOperationStatus.Success, completed.Status);
        Assert.Equal(PurchaseOrderStatus.Received, fixture.PurchaseOrder.Status);
        Assert.Equal(
            10m,
            (await fixture.DbContext.InventoryBalances
                .SingleAsync(balance => balance.ProductId == fixture.FirstOrderItem.ProductId))
            .QuantityOnHand);
        Assert.Equal(3, await fixture.DbContext.InventoryTransactions.CountAsync());

        var afterCompletion = await fixture.Service.CreateAsync(
            fixture.RequestWithDeliveryNote(
                "DN-003",
                (fixture.FirstOrderItem.Id, 1m)),
            CancellationToken.None);
        Assert.Equal(GoodsReceiptOperationStatus.InvalidState, afterCompletion.Status);
    }

    [Fact]
    public async Task PostAsync_DoesNotRecordInventoryTwiceForSameReceiptItems()
    {
        await using var fixture = await GoodsReceiptFixture.CreateAsync();
        var created = await fixture.Service.CreateAsync(
            fixture.Request((fixture.FirstOrderItem.Id, 2m)),
            CancellationToken.None);
        await fixture.Service.PostAsync(created.GoodsReceipt!.Id, CancellationToken.None);

        var trackedReceipt = await fixture.DbContext.GoodsReceipts
            .SingleAsync(receipt => receipt.Id == created.GoodsReceipt.Id);
        trackedReceipt.Status = GoodsReceiptStatus.Draft;
        await fixture.DbContext.SaveChangesAsync();

        var duplicate = await fixture.Service.PostAsync(
            created.GoodsReceipt.Id,
            CancellationToken.None);

        Assert.Equal(GoodsReceiptOperationStatus.InvalidState, duplicate.Status);
        Assert.Equal(2m, (await fixture.DbContext.InventoryBalances.SingleAsync()).QuantityOnHand);
        Assert.Single(fixture.DbContext.InventoryTransactions);
    }

    [Fact]
    public async Task CreateAsync_RejectsDuplicateDeliveryNoteAfterPreviousReceiptIsPosted()
    {
        await using var fixture = await GoodsReceiptFixture.CreateAsync();
        var first = await fixture.Service.CreateAsync(
            fixture.Request((fixture.FirstOrderItem.Id, 2m)),
            CancellationToken.None);
        await fixture.Service.PostAsync(first.GoodsReceipt!.Id, CancellationToken.None);

        var duplicate = await fixture.Service.CreateAsync(
            fixture.Request((fixture.FirstOrderItem.Id, 1m)),
            CancellationToken.None);

        Assert.Equal(GoodsReceiptOperationStatus.DuplicateDeliveryNote, duplicate.Status);
    }

    [Fact]
    public async Task DeleteAsync_AllowsDraftButRejectsPostedReceipt()
    {
        await using var fixture = await GoodsReceiptFixture.CreateAsync();
        var first = await fixture.Service.CreateAsync(
            fixture.Request((fixture.FirstOrderItem.Id, 1m)),
            CancellationToken.None);

        var deleted = await fixture.Service.DeleteAsync(
            first.GoodsReceipt!.Id,
            CancellationToken.None);
        var second = await fixture.Service.CreateAsync(
            fixture.Request((fixture.FirstOrderItem.Id, 1m)),
            CancellationToken.None);
        await fixture.Service.PostAsync(second.GoodsReceipt!.Id, CancellationToken.None);
        var rejected = await fixture.Service.DeleteAsync(
            second.GoodsReceipt.Id,
            CancellationToken.None);

        Assert.Equal(GoodsReceiptOperationStatus.Success, deleted.Status);
        Assert.Equal(GoodsReceiptOperationStatus.InvalidState, rejected.Status);
        Assert.Single(fixture.DbContext.GoodsReceipts);
    }

    private sealed class GoodsReceiptFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;

        private GoodsReceiptFixture(
            SqliteConnection connection,
            AppDbContext dbContext,
            PurchaseOrder purchaseOrder)
        {
            this.connection = connection;
            DbContext = dbContext;
            PurchaseOrder = purchaseOrder;
            Service = new GoodsReceiptService(dbContext, new FakeCurrentUserContext());
        }

        public AppDbContext DbContext { get; }

        public GoodsReceiptService Service { get; }

        public PurchaseOrder PurchaseOrder { get; }

        public PurchaseOrderItem FirstOrderItem => PurchaseOrder.Items.OrderBy(item => item.Id).First();

        public PurchaseOrderItem SecondOrderItem => PurchaseOrder.Items.OrderBy(item => item.Id).Last();

        public DateOnly Today { get; } = DateOnly.FromDateTime(DateTime.UtcNow);

        public static async Task<GoodsReceiptFixture> CreateAsync()
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
            var firstProduct = new Product
            {
                Code = "ITEM-0001",
                Name = "Monitor",
                ProductCategory = category,
                UnitOfMeasure = unit,
                DefaultUnitPrice = 100m
            };
            var secondProduct = new Product
            {
                Code = "ITEM-0002",
                Name = "Keyboard",
                ProductCategory = category,
                UnitOfMeasure = unit,
                DefaultUnitPrice = 50m
            };
            var supplier = new Supplier { Code = "SUP-0001", Name = "Supplier One" };
            var firstRequestItem = new PurchaseRequestItem
            {
                Product = firstProduct,
                Quantity = 10m,
                EstimatedUnitPrice = 100m
            };
            var secondRequestItem = new PurchaseRequestItem
            {
                Product = secondProduct,
                Quantity = 5m,
                EstimatedUnitPrice = 50m
            };
            var purchaseRequest = new PurchaseRequest
            {
                RequestNumber = "PR-0001",
                RequesterName = "Demo Requester",
                Items = [firstRequestItem, secondRequestItem]
            };
            var firstQuotationItem = new QuotationItem
            {
                PurchaseRequestItem = firstRequestItem,
                Product = firstProduct,
                ProductCode = firstProduct.Code,
                ProductName = firstProduct.Name,
                UnitOfMeasureCode = unit.Code,
                Quantity = 10m,
                UnitPrice = 90m
            };
            var secondQuotationItem = new QuotationItem
            {
                PurchaseRequestItem = secondRequestItem,
                Product = secondProduct,
                ProductCode = secondProduct.Code,
                ProductName = secondProduct.Name,
                UnitOfMeasureCode = unit.Code,
                Quantity = 5m,
                UnitPrice = 45m
            };
            var quotation = new Quotation
            {
                QuotationNumber = "QT-0001",
                PurchaseRequest = purchaseRequest,
                Supplier = supplier,
                SupplierCode = supplier.Code,
                SupplierName = supplier.Name,
                QuotationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2)),
                Status = QuotationStatus.Selected,
                CreatedByUserId = 4,
                CreatedByName = "Demo Admin",
                Items = [firstQuotationItem, secondQuotationItem]
            };
            var purchaseOrder = new PurchaseOrder
            {
                PurchaseOrderNumber = "PO-0001",
                Quotation = quotation,
                PurchaseRequest = purchaseRequest,
                Supplier = supplier,
                QuotationNumber = quotation.QuotationNumber,
                PurchaseRequestNumber = purchaseRequest.RequestNumber,
                SupplierCode = supplier.Code,
                SupplierName = supplier.Name,
                OrderDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
                ExpectedDeliveryDate = DateOnly.FromDateTime(DateTime.UtcNow),
                DeliveryAddress = "Main warehouse",
                Status = PurchaseOrderStatus.Issued,
                CreatedByUserId = 4,
                CreatedByName = "Demo Admin",
                IssuedAtUtc = DateTimeOffset.UtcNow.AddDays(-1),
                IssuedByUserId = 4,
                IssuedByName = "Demo Admin",
                Items =
                [
                    new PurchaseOrderItem
                    {
                        QuotationItem = firstQuotationItem,
                        Product = firstProduct,
                        ProductCode = firstProduct.Code,
                        ProductName = firstProduct.Name,
                        UnitOfMeasureCode = unit.Code,
                        Quantity = 10m,
                        UnitPrice = 90m
                    },
                    new PurchaseOrderItem
                    {
                        QuotationItem = secondQuotationItem,
                        Product = secondProduct,
                        ProductCode = secondProduct.Code,
                        ProductName = secondProduct.Name,
                        UnitOfMeasureCode = unit.Code,
                        Quantity = 5m,
                        UnitPrice = 45m
                    }
                ]
            };

            dbContext.PurchaseOrders.Add(purchaseOrder);
            await dbContext.SaveChangesAsync();

            return new GoodsReceiptFixture(connection, dbContext, purchaseOrder);
        }

        public CreateGoodsReceiptRequest Request(
            params (int PurchaseOrderItemId, decimal Quantity)[] items) =>
            RequestWithDeliveryNote("DN-001", items);

        public CreateGoodsReceiptRequest RequestWithDeliveryNote(
            string deliveryNote,
            params (int PurchaseOrderItemId, decimal Quantity)[] items) => new()
            {
                PurchaseOrderId = PurchaseOrder.Id,
                SupplierDeliveryNoteNumber = deliveryNote,
                ReceivedDate = Today,
                Notes = "Boxes received in good condition.",
                Items = items
                    .Select(item => new GoodsReceiptItemInput
                    {
                        PurchaseOrderItemId = item.PurchaseOrderItemId,
                        QuantityReceived = item.Quantity
                    })
                    .ToList()
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
