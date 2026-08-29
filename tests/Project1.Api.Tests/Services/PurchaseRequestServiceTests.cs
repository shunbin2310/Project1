using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Project1.Api.Data;
using Project1.Api.DTOs.PurchaseRequests;
using Project1.Api.Entities;
using Project1.Api.Entities.Workflows;
using Project1.Api.Services.PurchaseRequests;
using Project1.Api.Services.Workflows;

namespace Project1.Api.Tests.Services;

public sealed class PurchaseRequestServiceTests
{
    [Fact]
    public async Task CreateAsync_CreatesDraftWorkflowSnapshot()
    {
        await using var fixture = await PurchaseRequestFixture.CreateAsync();

        var result = await fixture.Service.CreateAsync(
            fixture.ValidRequest(),
            CancellationToken.None);

        Assert.Equal(PurchaseRequestOperationStatus.Success, result.Status);
        Assert.StartsWith("PR-", result.PurchaseRequest!.RequestNumber);
        Assert.Equal("DRAFT", result.PurchaseRequest.Workflow.CurrentStepCode);
        Assert.Equal(1, result.PurchaseRequest.Workflow.TemplateVersion);
        Assert.Equal("SUBMIT", Assert.Single(result.PurchaseRequest.Workflow.AvailableActions).Code);
        Assert.Equal("START", Assert.Single(result.PurchaseRequest.Workflow.History).ActionCode);
    }

    [Fact]
    public async Task ExecuteActionAsync_UsesWorkflowRolesUntilApproval()
    {
        await using var fixture = await PurchaseRequestFixture.CreateAsync();
        var created = await fixture.Service.CreateAsync(
            fixture.ValidRequest(),
            CancellationToken.None);
        var id = created.PurchaseRequest!.Id;

        var submitted = await fixture.Service.ExecuteActionAsync(
            id,
            "SUBMIT",
            new PurchaseRequestActionRequest { ActionBy = "Alex Tan" },
            CancellationToken.None);
        var unauthorized = await fixture.Service.ExecuteActionAsync(
            id,
            "APPROVE",
            new PurchaseRequestActionRequest
            {
                ActionBy = "Finance Manager",
                ActorRoles = ["FINANCE_APPROVER"]
            },
            CancellationToken.None);
        var departmentApproved = await fixture.Service.ExecuteActionAsync(
            id,
            "APPROVE",
            new PurchaseRequestActionRequest
            {
                ActionBy = "Department Manager",
                ActorRoles = ["DEPARTMENT_APPROVER"]
            },
            CancellationToken.None);
        var financeApproved = await fixture.Service.ExecuteActionAsync(
            id,
            "APPROVE",
            new PurchaseRequestActionRequest
            {
                ActionBy = "Finance Manager",
                ActorRoles = ["FINANCE_APPROVER"]
            },
            CancellationToken.None);

        Assert.Equal("DEPARTMENT_REVIEW", submitted.PurchaseRequest!.Workflow.CurrentStepCode);
        Assert.Equal(PurchaseRequestOperationStatus.Unauthorized, unauthorized.Status);
        Assert.Equal("FINANCE_REVIEW", departmentApproved.PurchaseRequest!.Workflow.CurrentStepCode);
        Assert.Equal("APPROVED", financeApproved.PurchaseRequest!.Workflow.CurrentStepCode);
        Assert.Equal(WorkflowInstanceStatus.Completed, financeApproved.PurchaseRequest.Workflow.Status);
    }

    [Fact]
    public async Task UpdateAsync_IsRejectedAfterDraftIsSubmitted()
    {
        await using var fixture = await PurchaseRequestFixture.CreateAsync();
        var created = await fixture.Service.CreateAsync(
            fixture.ValidRequest(),
            CancellationToken.None);
        var id = created.PurchaseRequest!.Id;
        await fixture.Service.ExecuteActionAsync(
            id,
            "SUBMIT",
            new PurchaseRequestActionRequest { ActionBy = "Alex Tan" },
            CancellationToken.None);

        var result = await fixture.Service.UpdateAsync(
            id,
            new UpdatePurchaseRequestRequest
            {
                RequesterName = "Alex Tan",
                DepartmentId = fixture.DepartmentId,
                RequiredDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)),
                Items =
                [
                    new PurchaseRequestItemRequest
                    {
                        ProductId = fixture.ProductId,
                        Quantity = 2
                    }
                ]
            },
            CancellationToken.None);

        Assert.Equal(PurchaseRequestOperationStatus.InvalidState, result.Status);
        Assert.Equal("Only purchase requests at the DRAFT step can be edited.", result.ErrorMessage);
    }

    private sealed class PurchaseRequestFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;

        private PurchaseRequestFixture(
            SqliteConnection connection,
            AppDbContext dbContext,
            int departmentId,
            int productId)
        {
            this.connection = connection;
            DbContext = dbContext;
            DepartmentId = departmentId;
            ProductId = productId;
            Service = new PurchaseRequestService(dbContext, new WorkflowEngine(dbContext));
        }

        public AppDbContext DbContext { get; }

        public PurchaseRequestService Service { get; }

        public int DepartmentId { get; }

        public int ProductId { get; }

        public static async Task<PurchaseRequestFixture> CreateAsync()
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
            var product = new Product
            {
                Code = "ITEM-TEST",
                Name = "Test Product",
                ProductCategory = category,
                UnitOfMeasure = unit,
                DefaultUnitPrice = 25m
            };
            dbContext.AddRange(department, product);
            await dbContext.SaveChangesAsync();

            return new PurchaseRequestFixture(
                connection,
                dbContext,
                department.Id,
                product.Id);
        }

        public CreatePurchaseRequestRequest ValidRequest() => new()
        {
            RequesterName = "Alex Tan",
            DepartmentId = DepartmentId,
            RequiredDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            Justification = "New equipment is required.",
            Items =
            [
                new PurchaseRequestItemRequest
                {
                    ProductId = ProductId,
                    Quantity = 2
                }
            ]
        };

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
