using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Project1.Api.Authentication;
using Project1.Api.Data;
using Project1.Api.Entities;
using Project1.Api.Services.Authentication;
using Project1.Api.Services.EmailRecords;

namespace Project1.Api.Tests.Services;

public sealed class EmailRecordServiceTests
{
    [Fact]
    public async Task GetAllAsync_FiltersBySearchStatusSourceAndDate()
    {
        await using var fixture = await EmailRecordFixture.CreateAsync();
        fixture.AddEmail(EmailDeliveryStatus.Sent, "PO-0001", "first@supplier.test", new DateTimeOffset(2026, 9, 20, 8, 0, 0, TimeSpan.Zero));
        fixture.AddEmail(EmailDeliveryStatus.Failed, "PO-0001", "failed@supplier.test", new DateTimeOffset(2026, 9, 21, 8, 0, 0, TimeSpan.Zero));
        await fixture.DbContext.SaveChangesAsync();

        var records = await fixture.Service.GetAllAsync(
            "failed@supplier.test",
            EmailDeliveryStatus.Failed,
            "PurchaseOrder",
            new DateOnly(2026, 9, 21),
            new DateOnly(2026, 9, 21),
            CancellationToken.None);

        var record = Assert.Single(records);
        Assert.Equal(EmailDeliveryStatus.Failed, record.Status);
        Assert.Equal("failed@supplier.test", record.RecipientEmail);
    }

    [Fact]
    public async Task RetryAsync_QueuesFailedRecordWithoutChangingItsSnapshot()
    {
        await using var fixture = await EmailRecordFixture.CreateAsync();
        var email = fixture.AddEmail(EmailDeliveryStatus.Failed);
        email.AttemptCount = 1;
        email.LastError = "SMTP unavailable";
        await fixture.DbContext.SaveChangesAsync();

        var result = await fixture.Service.RetryAsync(email.Id, CancellationToken.None);

        Assert.Equal(EmailRecordOperationStatus.Success, result.Status);
        Assert.Equal(EmailDeliveryStatus.Pending, result.EmailRecord!.Status);
        Assert.Equal(1, result.EmailRecord.AttemptCount);
        Assert.Null(result.EmailRecord.LastError);
        Assert.Equal("<h1>Purchase Order</h1>", result.EmailRecord.HtmlBody);
    }

    [Fact]
    public async Task ResendAsync_CreatesANewPendingSnapshotForSentRecord()
    {
        await using var fixture = await EmailRecordFixture.CreateAsync();
        var original = fixture.AddEmail(EmailDeliveryStatus.Sent);
        original.AttemptCount = 1;
        original.SentAtUtc = DateTimeOffset.UtcNow;
        await fixture.DbContext.SaveChangesAsync();

        var result = await fixture.Service.ResendAsync(original.Id, CancellationToken.None);

        Assert.Equal(EmailRecordOperationStatus.Success, result.Status);
        Assert.Equal(2, await fixture.DbContext.EmailOutboxes.CountAsync());
        Assert.Equal(EmailDeliveryStatus.Pending, result.EmailRecord!.Status);
        Assert.Equal(0, result.EmailRecord.AttemptCount);
        Assert.Equal(original.Id, result.EmailRecord.ResentFromEmailOutboxId);
        Assert.Equal(original.Subject, result.EmailRecord.Subject);
        Assert.Equal(original.HtmlBody, result.EmailRecord.HtmlBody);
        Assert.Equal("Demo Procurement", result.EmailRecord.CreatedByName);
    }

    [Fact]
    public async Task ResendAsync_RejectsRecordThatHasNotBeenSent()
    {
        await using var fixture = await EmailRecordFixture.CreateAsync();
        var email = fixture.AddEmail(EmailDeliveryStatus.Failed);
        await fixture.DbContext.SaveChangesAsync();

        var result = await fixture.Service.ResendAsync(email.Id, CancellationToken.None);

        Assert.Equal(EmailRecordOperationStatus.InvalidState, result.Status);
        Assert.Single(fixture.DbContext.EmailOutboxes);
    }

    private sealed class EmailRecordFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;

        private EmailRecordFixture(
            SqliteConnection connection,
            AppDbContext dbContext,
            PurchaseOrder purchaseOrder)
        {
            this.connection = connection;
            DbContext = dbContext;
            PurchaseOrder = purchaseOrder;
            Service = new EmailRecordService(dbContext, new FakeCurrentUserContext());
        }

        public AppDbContext DbContext { get; }

        public PurchaseOrder PurchaseOrder { get; }

        public EmailRecordService Service { get; }

        public static async Task<EmailRecordFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;
            var dbContext = new AppDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();

            var supplier = new Supplier { Code = "SUP-0001", Name = "Supplier One" };
            var request = new PurchaseRequest { RequestNumber = "PR-0001" };
            var quotation = new Quotation
            {
                QuotationNumber = "QT-0001",
                PurchaseRequest = request,
                Supplier = supplier,
                SupplierCode = supplier.Code,
                SupplierName = supplier.Name,
                QuotationDate = new DateOnly(2026, 9, 20),
                CreatedByUserId = 4,
                CreatedByName = "Demo Admin"
            };
            var purchaseOrder = new PurchaseOrder
            {
                PurchaseOrderNumber = "PO-0001",
                Quotation = quotation,
                PurchaseRequest = request,
                Supplier = supplier,
                QuotationNumber = quotation.QuotationNumber,
                PurchaseRequestNumber = request.RequestNumber,
                SupplierCode = supplier.Code,
                SupplierName = supplier.Name,
                OrderDate = new DateOnly(2026, 9, 20),
                CreatedByUserId = 4,
                CreatedByName = "Demo Admin"
            };

            dbContext.PurchaseOrders.Add(purchaseOrder);
            await dbContext.SaveChangesAsync();
            return new EmailRecordFixture(connection, dbContext, purchaseOrder);
        }

        public EmailOutbox AddEmail(
            EmailDeliveryStatus status,
            string sourceReference = "PO-0001",
            string recipient = "orders@supplier.test",
            DateTimeOffset? createdAtUtc = null)
        {
            var email = new EmailOutbox
            {
                PurchaseOrderId = PurchaseOrder.Id,
                SourceType = "PurchaseOrder",
                SourceId = PurchaseOrder.Id,
                SourceReference = sourceReference,
                FromAddress = "purchasing@project1.test",
                FromName = "Project1 Purchasing",
                RecipientEmail = recipient,
                Subject = $"Purchase Order {sourceReference}",
                HtmlBody = "<h1>Purchase Order</h1>",
                Status = status,
                CreatedByUserId = 4,
                CreatedByName = "Demo Admin",
                CreatedDate = DateOnly.FromDateTime((createdAtUtc ?? DateTimeOffset.UtcNow).UtcDateTime),
                CreatedAtUtc = createdAtUtc ?? DateTimeOffset.UtcNow
            };
            DbContext.EmailOutboxes.Add(email);
            return email;
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class FakeCurrentUserContext : ICurrentUserContext
    {
        public bool IsAuthenticated => true;
        public int UserId => 8;
        public string DisplayName => "Demo Procurement";
        public int? DepartmentId => null;
        public IReadOnlyCollection<string> Roles => [ApplicationRoles.ProcurementOfficer];
        public bool IsInRole(string role) => Roles.Contains(role, StringComparer.OrdinalIgnoreCase);
    }
}
