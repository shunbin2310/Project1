using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Project1.Api.Authentication;
using Project1.Api.Data;
using Project1.Api.DTOs.PurchaseRequests;
using Project1.Api.DTOs.Quotations;
using Project1.Api.Entities;
using Project1.Api.Entities.Identity;
using Project1.Api.Services.Authentication;
using Project1.Api.Services.PurchaseRequests;
using Project1.Api.Services.Quotations;
using Project1.Api.Services.Workflows;

namespace Project1.Api.Tests.Services;

public sealed class QuotationServiceTests
{
    [Fact]
    public async Task CreateAsync_CreatesDraftWithSnapshotsAndCalculatedTotal()
    {
        await using var fixture = await QuotationFixture.CreateAsync();

        var result = await fixture.Service.CreateAsync(
            fixture.ValidRequest(fixture.SupplierOneId, 100m, 25m),
            CancellationToken.None);

        Assert.Equal(QuotationOperationStatus.Success, result.Status);
        Assert.NotNull(result.Quotation);
        Assert.StartsWith("QT-", result.Quotation.QuotationNumber);
        Assert.Equal(QuotationStatus.Draft, result.Quotation.Status);
        Assert.Equal("SUP-0001", result.Quotation.SupplierCode);
        Assert.Equal("Demo Admin", result.Quotation.CreatedByName);
        Assert.Equal(250m, result.Quotation.TotalAmount);
        Assert.Collection(
            result.Quotation.Items,
            item =>
            {
                Assert.Equal("ITEM-0001", item.ProductCode);
                Assert.Equal(200m, item.LineTotal);
            },
            item =>
            {
                Assert.Equal("ITEM-0002", item.ProductCode);
                Assert.Equal(50m, item.LineTotal);
            });
    }

    [Fact]
    public async Task CreateAsync_RejectsPurchaseRequestThatIsNotApproved()
    {
        await using var fixture = await QuotationFixture.CreateAsync();
        var draftPurchaseRequestId = await fixture.CreateDraftPurchaseRequestAsync();
        var draftItemIds = await fixture.DbContext.PurchaseRequestItems
            .Where(item => item.PurchaseRequestId == draftPurchaseRequestId)
            .OrderBy(item => item.Id)
            .Select(item => item.Id)
            .ToListAsync();

        var request = fixture.ValidRequest(fixture.SupplierOneId, 100m, 25m);
        request = new CreateQuotationRequest
        {
            PurchaseRequestId = draftPurchaseRequestId,
            SupplierId = request.SupplierId,
            QuotationDate = request.QuotationDate,
            ValidUntil = request.ValidUntil,
            Items =
            [
                new() { PurchaseRequestItemId = draftItemIds[0], UnitPrice = 100m },
                new() { PurchaseRequestItemId = draftItemIds[1], UnitPrice = 25m }
            ]
        };

        var result = await fixture.Service.CreateAsync(request, CancellationToken.None);

        Assert.Equal(QuotationOperationStatus.InvalidState, result.Status);
        Assert.Empty(fixture.DbContext.Quotations);
    }

    [Fact]
    public async Task CreateAsync_RejectsSupplierThatCannotSupplyEveryProduct()
    {
        await using var fixture = await QuotationFixture.CreateAsync();

        var result = await fixture.Service.CreateAsync(
            fixture.ValidRequest(fixture.PartialSupplierId, 100m, 25m),
            CancellationToken.None);

        Assert.Equal(QuotationOperationStatus.SupplierCannotSupplyProducts, result.Status);
        Assert.Contains("ITEM-0002", result.ErrorMessage);
    }

    [Fact]
    public async Task SubmitAsync_RequiresAllUnitPricesToBeGreaterThanZero()
    {
        await using var fixture = await QuotationFixture.CreateAsync();
        var created = await fixture.Service.CreateAsync(
            fixture.ValidRequest(fixture.SupplierOneId, 100m, 0m),
            CancellationToken.None);

        var result = await fixture.Service.SubmitAsync(
            created.Quotation!.Id,
            CancellationToken.None);

        Assert.Equal(QuotationOperationStatus.ValidationFailed, result.Status);
        Assert.Equal(QuotationStatus.Draft, fixture.DbContext.Quotations.Single().Status);
    }

    [Fact]
    public async Task SelectAsync_SelectsWinnerAndMarksOtherQuotationNotSelected()
    {
        await using var fixture = await QuotationFixture.CreateAsync();
        var first = await fixture.Service.CreateAsync(
            fixture.ValidRequest(fixture.SupplierOneId, 100m, 25m),
            CancellationToken.None);
        var second = await fixture.Service.CreateAsync(
            fixture.ValidRequest(fixture.SupplierTwoId, 90m, 20m),
            CancellationToken.None);
        await fixture.Service.SubmitAsync(first.Quotation!.Id, CancellationToken.None);
        await fixture.Service.SubmitAsync(second.Quotation!.Id, CancellationToken.None);

        var selected = await fixture.Service.SelectAsync(
            second.Quotation.Id,
            CancellationToken.None);
        var comparison = await fixture.Service.GetComparisonAsync(
            fixture.ApprovedPurchaseRequestId,
            CancellationToken.None);

        Assert.Equal(QuotationOperationStatus.Success, selected.Status);
        Assert.Equal(QuotationStatus.Selected, selected.Quotation!.Status);
        Assert.Equal(second.Quotation.Id, comparison!.SelectedQuotationId);
        Assert.Equal(220m, comparison.LowestTotalAmount);
        Assert.True(comparison.Quotations.Single(item => item.QuotationId == second.Quotation.Id).IsLowestTotal);
        Assert.Equal(
            QuotationStatus.NotSelected,
            comparison.Quotations.Single(item => item.QuotationId == first.Quotation.Id).Status);
    }

    [Fact]
    public async Task SelectAsync_LeavesUnsubmittedDraftAvailableForDeletion()
    {
        await using var fixture = await QuotationFixture.CreateAsync();
        var draft = await fixture.Service.CreateAsync(
            fixture.ValidRequest(fixture.SupplierOneId, 0m, 0m),
            CancellationToken.None);
        var submitted = await fixture.Service.CreateAsync(
            fixture.ValidRequest(fixture.SupplierTwoId, 90m, 20m),
            CancellationToken.None);
        await fixture.Service.SubmitAsync(submitted.Quotation!.Id, CancellationToken.None);

        await fixture.Service.SelectAsync(submitted.Quotation.Id, CancellationToken.None);
        var comparison = await fixture.Service.GetComparisonAsync(
            fixture.ApprovedPurchaseRequestId,
            CancellationToken.None);
        var deleted = await fixture.Service.DeleteAsync(
            draft.Quotation!.Id,
            CancellationToken.None);

        Assert.Single(comparison!.Quotations);
        Assert.Equal(submitted.Quotation.Id, comparison.Quotations[0].QuotationId);
        Assert.Equal(QuotationOperationStatus.Success, deleted.Status);
    }

    [Fact]
    public async Task DeleteAsync_AllowsDraftButRejectsSubmittedQuotation()
    {
        await using var fixture = await QuotationFixture.CreateAsync();
        var draft = await fixture.Service.CreateAsync(
            fixture.ValidRequest(fixture.SupplierOneId, 100m, 25m),
            CancellationToken.None);
        var submitted = await fixture.Service.CreateAsync(
            fixture.ValidRequest(fixture.SupplierTwoId, 90m, 20m),
            CancellationToken.None);
        await fixture.Service.SubmitAsync(submitted.Quotation!.Id, CancellationToken.None);

        var deleted = await fixture.Service.DeleteAsync(draft.Quotation!.Id, CancellationToken.None);
        var rejected = await fixture.Service.DeleteAsync(submitted.Quotation.Id, CancellationToken.None);

        Assert.Equal(QuotationOperationStatus.Success, deleted.Status);
        Assert.Equal(QuotationOperationStatus.InvalidState, rejected.Status);
        Assert.Single(fixture.DbContext.Quotations);
        Assert.Equal(submitted.Quotation.Id, fixture.DbContext.Quotations.Single().Id);
    }

    private sealed class QuotationFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly PurchaseRequestService purchaseRequestService;

        private QuotationFixture(
            SqliteConnection connection,
            AppDbContext dbContext,
            FakeCurrentUserContext currentUser,
            PurchaseRequestService purchaseRequestService)
        {
            this.connection = connection;
            this.purchaseRequestService = purchaseRequestService;
            DbContext = dbContext;
            CurrentUser = currentUser;
            Service = new QuotationService(dbContext, currentUser);
        }

        public AppDbContext DbContext { get; }

        public FakeCurrentUserContext CurrentUser { get; }

        public QuotationService Service { get; }

        public int ApprovedPurchaseRequestId { get; private set; }

        public int SupplierOneId { get; private set; }

        public int SupplierTwoId { get; private set; }

        public int PartialSupplierId { get; private set; }

        public IReadOnlyList<int> PurchaseRequestItemIds { get; private set; } = [];

        public static async Task<QuotationFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;
            var dbContext = new AppDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();

            var department = new Department { Code = "IT", Name = "Information Technology" };
            var category = new ProductCategory { Code = "CAT-TEST", Name = "Test Category" };
            var unit = new UnitOfMeasure { Code = "UNIT", Name = "Unit" };
            var firstProduct = new Product
            {
                Code = "ITEM-0001",
                Name = "Monitor",
                ProductCategory = category,
                UnitOfMeasure = unit,
                DefaultUnitPrice = 150m
            };
            var secondProduct = new Product
            {
                Code = "ITEM-0002",
                Name = "Keyboard",
                ProductCategory = category,
                UnitOfMeasure = unit,
                DefaultUnitPrice = 30m
            };
            var admin = new ApplicationUser
            {
                Id = 4,
                UserName = "admin@demo.local",
                NormalizedUserName = "ADMIN@DEMO.LOCAL",
                Email = "admin@demo.local",
                NormalizedEmail = "ADMIN@DEMO.LOCAL",
                FullName = "Demo Admin",
                Department = department,
                EmailConfirmed = true
            };
            var supplierOne = new Supplier { Code = "SUP-0001", Name = "Supplier One" };
            var supplierTwo = new Supplier { Code = "SUP-0002", Name = "Supplier Two" };
            var partialSupplier = new Supplier { Code = "SUP-0003", Name = "Partial Supplier" };

            dbContext.AddRange(admin, firstProduct, secondProduct, supplierOne, supplierTwo, partialSupplier);
            await dbContext.SaveChangesAsync();
            dbContext.SupplierProducts.AddRange(
                new() { SupplierId = supplierOne.Id, ProductId = firstProduct.Id },
                new() { SupplierId = supplierOne.Id, ProductId = secondProduct.Id },
                new() { SupplierId = supplierTwo.Id, ProductId = firstProduct.Id },
                new() { SupplierId = supplierTwo.Id, ProductId = secondProduct.Id },
                new() { SupplierId = partialSupplier.Id, ProductId = firstProduct.Id });
            await dbContext.SaveChangesAsync();

            var currentUser = new FakeCurrentUserContext();
            var purchaseRequestService = new PurchaseRequestService(
                dbContext,
                new WorkflowEngine(dbContext),
                currentUser);
            var fixture = new QuotationFixture(
                connection,
                dbContext,
                currentUser,
                purchaseRequestService)
            {
                SupplierOneId = supplierOne.Id,
                SupplierTwoId = supplierTwo.Id,
                PartialSupplierId = partialSupplier.Id
            };

            fixture.ApprovedPurchaseRequestId = await fixture.CreateApprovedPurchaseRequestAsync();
            fixture.PurchaseRequestItemIds = await dbContext.PurchaseRequestItems
                .Where(item => item.PurchaseRequestId == fixture.ApprovedPurchaseRequestId)
                .OrderBy(item => item.Id)
                .Select(item => item.Id)
                .ToListAsync();

            return fixture;
        }

        public CreateQuotationRequest ValidRequest(
            int supplierId,
            decimal firstUnitPrice,
            decimal secondUnitPrice) => new()
            {
                PurchaseRequestId = ApprovedPurchaseRequestId,
                SupplierId = supplierId,
                SupplierQuotationReference = "SUP-QUOTE-001",
                QuotationDate = DateOnly.FromDateTime(DateTime.UtcNow),
                ValidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
                Items =
            [
                new()
                {
                    PurchaseRequestItemId = PurchaseRequestItemIds[0],
                    UnitPrice = firstUnitPrice
                },
                new()
                {
                    PurchaseRequestItemId = PurchaseRequestItemIds[1],
                    UnitPrice = secondUnitPrice
                }
            ]
            };

        public async Task<int> CreateDraftPurchaseRequestAsync()
        {
            var result = await purchaseRequestService.CreateAsync(
                ValidPurchaseRequest(),
                CancellationToken.None);
            return result.PurchaseRequest!.Id;
        }

        private async Task<int> CreateApprovedPurchaseRequestAsync()
        {
            var id = await CreateDraftPurchaseRequestAsync();
            await purchaseRequestService.ExecuteActionAsync(
                id,
                "SUBMIT",
                new PurchaseRequestActionRequest(),
                CancellationToken.None);
            await purchaseRequestService.ExecuteActionAsync(
                id,
                "APPROVE",
                new PurchaseRequestActionRequest(),
                CancellationToken.None);
            var approved = await purchaseRequestService.ExecuteActionAsync(
                id,
                "APPROVE",
                new PurchaseRequestActionRequest(),
                CancellationToken.None);
            Assert.Equal("APPROVED", approved.PurchaseRequest!.Workflow.CurrentStepCode);
            return id;
        }

        private CreatePurchaseRequestRequest ValidPurchaseRequest() => new()
        {
            RequiredDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)),
            Justification = "Test quotation comparison.",
            Items =
            [
                new() { ProductId = DbContext.Products.Single(item => item.Code == "ITEM-0001").Id, Quantity = 2 },
                new() { ProductId = DbContext.Products.Single(item => item.Code == "ITEM-0002").Id, Quantity = 2 }
            ]
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
