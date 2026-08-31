using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Project1.Api.Authentication;
using Project1.Api.Data;
using Project1.Api.DTOs.PurchaseRequests;
using Project1.Api.Entities;
using Project1.Api.Entities.Identity;
using Project1.Api.Services.Authentication;
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
        Assert.Equal("Alex Tan", result.PurchaseRequest.RequesterName);
        Assert.Equal(fixture.DepartmentId, result.PurchaseRequest.DepartmentId);
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
            new PurchaseRequestActionRequest(),
            CancellationToken.None);
        fixture.CurrentUser.Set(11, "Finance Manager", null, [ApplicationRoles.FinanceApprover]);
        var unauthorized = await fixture.Service.ExecuteActionAsync(
            id,
            "APPROVE",
            new PurchaseRequestActionRequest(),
            CancellationToken.None);
        fixture.CurrentUser.Set(
            12,
            "Department Manager",
            null,
            [ApplicationRoles.DepartmentApprover]);
        var departmentApproved = await fixture.Service.ExecuteActionAsync(
            id,
            "APPROVE",
            new PurchaseRequestActionRequest(),
            CancellationToken.None);
        fixture.CurrentUser.Set(11, "Finance Manager", null, [ApplicationRoles.FinanceApprover]);
        var financeApproved = await fixture.Service.ExecuteActionAsync(
            id,
            "APPROVE",
            new PurchaseRequestActionRequest(),
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
            new PurchaseRequestActionRequest(),
            CancellationToken.None);

        var result = await fixture.Service.UpdateAsync(
            id,
            new UpdatePurchaseRequestRequest
            {
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

    [Fact]
    public async Task UpdateAsync_AdminCannotEditAnotherRequestersDraft()
    {
        await using var fixture = await PurchaseRequestFixture.CreateAsync();
        var created = await fixture.Service.CreateAsync(
            fixture.ValidRequest(),
            CancellationToken.None);
        fixture.CurrentUser.Set(
            4,
            "Demo Admin",
            fixture.DepartmentId,
            [
                ApplicationRoles.Admin,
                ApplicationRoles.Requester,
                ApplicationRoles.DepartmentApprover,
                ApplicationRoles.FinanceApprover
            ]);

        var result = await fixture.Service.UpdateAsync(
            created.PurchaseRequest!.Id,
            new UpdatePurchaseRequestRequest
            {
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

        Assert.Equal(PurchaseRequestOperationStatus.Unauthorized, result.Status);
        Assert.Equal("Only the original requester can edit this draft.", result.ErrorMessage);
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
            CurrentUser = new FakeCurrentUserContext();
            CurrentUser.Set(10, "Alex Tan", departmentId, [ApplicationRoles.Requester]);
            Service = new PurchaseRequestService(
                dbContext,
                new WorkflowEngine(dbContext),
                CurrentUser);
        }

        public AppDbContext DbContext { get; }

        public PurchaseRequestService Service { get; }

        public FakeCurrentUserContext CurrentUser { get; }

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
            var requester = new ApplicationUser
            {
                Id = 10,
                UserName = "alex@demo.local",
                NormalizedUserName = "ALEX@DEMO.LOCAL",
                Email = "alex@demo.local",
                NormalizedEmail = "ALEX@DEMO.LOCAL",
                FullName = "Alex Tan",
                Department = department,
                EmailConfirmed = true
            };
            dbContext.AddRange(requester, product);
            await dbContext.SaveChangesAsync();

            return new PurchaseRequestFixture(
                connection,
                dbContext,
                department.Id,
                product.Id);
        }

        public sealed class FakeCurrentUserContext : ICurrentUserContext
        {
            public bool IsAuthenticated { get; private set; } = true;

            public int UserId { get; private set; }

            public string DisplayName { get; private set; } = string.Empty;

            public int? DepartmentId { get; private set; }

            public IReadOnlyCollection<string> Roles { get; private set; } = [];

            public bool IsInRole(string role) =>
                Roles.Contains(role, StringComparer.OrdinalIgnoreCase);

            public void Set(
                int userId,
                string displayName,
                int? departmentId,
                IReadOnlyCollection<string> roles)
            {
                UserId = userId;
                DisplayName = displayName;
                DepartmentId = departmentId;
                Roles = roles;
            }
        }

        public CreatePurchaseRequestRequest ValidRequest() => new()
        {
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
