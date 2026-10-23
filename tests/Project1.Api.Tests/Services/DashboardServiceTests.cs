using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Project1.Api.Authentication;
using Project1.Api.Data;
using Project1.Api.Entities;
using Project1.Api.Entities.Identity;
using Project1.Api.Entities.Workflows;
using Project1.Api.Services.Authentication;
using Project1.Api.Services.Dashboard;
using Project1.Api.Services.Workflows;

namespace Project1.Api.Tests.Services;

public sealed class DashboardServiceTests
{
    [Fact]
    public async Task GetAsync_RequesterSeesOnlyOwnedRequestSummaryAndActivity()
    {
        await using var fixture = await DashboardFixture.CreateAsync();
        var service = fixture.CreateService(1, ApplicationRoles.Requester);

        var dashboard = await service.GetAsync(CancellationToken.None);

        Assert.Equal(1, Card(dashboard, "my-drafts").Value);
        Assert.Equal(0, Card(dashboard, "my-in-review").Value);
        Assert.Contains(dashboard.Reminders, reminder => reminder.Key == "my-drafts");
        Assert.All(dashboard.RecentActivity, activity =>
            Assert.Equal(fixture.OwnRequest.RequestNumber, activity.Reference));
    }

    [Fact]
    public async Task GetAsync_DepartmentApproverReceivesPendingApprovalReminder()
    {
        await using var fixture = await DashboardFixture.CreateAsync();
        var service = fixture.CreateService(2, ApplicationRoles.DepartmentApprover);

        var dashboard = await service.GetAsync(CancellationToken.None);

        var card = Card(dashboard, "department-review");
        Assert.Equal(1, card.Value);
        Assert.Equal("/my-tasks", card.Route);
        Assert.Contains(dashboard.Reminders, reminder =>
            reminder.Key == "department-review" && reminder.Count == 1);
    }

    [Fact]
    public async Task GetAsync_AdminReceivesOrganizationWideSummary()
    {
        await using var fixture = await DashboardFixture.CreateAsync();
        var service = fixture.CreateService(4, ApplicationRoles.Admin);

        var dashboard = await service.GetAsync(CancellationToken.None);

        Assert.Equal(1, Card(dashboard, "pending-request-approval").Value);
        Assert.Equal(1, Card(dashboard, "out-of-stock").Value);
        Assert.Contains(dashboard.Reminders, reminder => reminder.Key == "out-of-stock");
        Assert.Contains(dashboard.RecentActivity, activity =>
            activity.Reference == fixture.OtherRequest.RequestNumber);
    }

    [Fact]
    public async Task GetAsync_MultipleRolesMergeCardsAndActivitiesWithoutDuplicateKeys()
    {
        await using var fixture = await DashboardFixture.CreateAsync();
        var service = fixture.CreateService(
            1,
            ApplicationRoles.Requester,
            ApplicationRoles.DepartmentApprover);

        var dashboard = await service.GetAsync(CancellationToken.None);

        Assert.Contains(dashboard.SummaryCards, card => card.Key == "my-drafts");
        Assert.Contains(dashboard.SummaryCards, card => card.Key == "department-review");
        Assert.Equal(
            dashboard.SummaryCards.Count,
            dashboard.SummaryCards.Select(card => card.Key).Distinct().Count());
        Assert.Equal(
            dashboard.RecentActivity.Count,
            dashboard.RecentActivity.Select(activity => activity.Key).Distinct().Count());
    }

    [Fact]
    public async Task GetAsync_ProcurementOfficerSeesProductMaintenanceAlongsidePurchasing()
    {
        await using var fixture = await DashboardFixture.CreateAsync();
        var service = fixture.CreateService(7, ApplicationRoles.ProcurementOfficer);

        var dashboard = await service.GetAsync(CancellationToken.None);

        var card = Card(dashboard, "out-of-stock");
        Assert.Equal(1, card.Value);
        Assert.Equal("/products", card.Route);
        Assert.Contains(dashboard.SummaryCards, item => item.Key == "ready-for-quotation");
        Assert.Contains(dashboard.SummaryCards, item => item.Key == "active-products");
        Assert.Contains(dashboard.RecentActivity, activity => activity.Module == "Product");
    }

    [Fact]
    public async Task GetAsync_ShowsOnlyCurrentRequestStatusAndKeepsFullHistory()
    {
        await using var fixture = await DashboardFixture.CreateAsync();
        var engine = new WorkflowEngine(fixture.DbContext);
        await engine.ExecuteActionAsync("PurchaseRequest", fixture.OtherRequest.Id, "APPROVE",
            new WorkflowActor(2, "Department Approver", [ApplicationRoles.DepartmentApprover]),
            null, CancellationToken.None);
        await engine.ExecuteActionAsync("PurchaseRequest", fixture.OtherRequest.Id, "APPROVE",
            new WorkflowActor(4, "Finance Approver", [ApplicationRoles.FinanceApprover]),
            null, CancellationToken.None);

        var dashboard = await fixture.CreateService(4, ApplicationRoles.Admin).GetAsync(CancellationToken.None);

        var activity = Assert.Single(dashboard.RecentActivity,
            item => item.Reference == fixture.OtherRequest.RequestNumber);
        Assert.Equal("Approved", activity.Title);
        Assert.Equal("Purchase Request is currently at Approved.", activity.Description);
        Assert.Equal("Finance Approver", activity.ActorName);
        var history = await fixture.DbContext.WorkflowHistory
            .Where(item => item.ProcessInstance.EntityType == "PurchaseRequest" &&
                           item.ProcessInstance.EntityId == fixture.OtherRequest.Id)
            .OrderBy(item => item.Id)
            .ToListAsync();
        Assert.Equal(4, history.Count);
        Assert.Equal(history[^1].ActionAtUtc, activity.OccurredAtUtc);
        Assert.Equal(2, dashboard.RecentActivity.Count(item => item.Module == "Purchase Request"));
    }

    [Fact]
    public async Task GetAsync_TakesTenDistinctRecordsAfterLatestUpdatesEvenWithManyHistoryEntries()
    {
        await using var fixture = await DashboardFixture.CreateAsync();
        var engine = new WorkflowEngine(fixture.DbContext);
        var baseline = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        for (var index = 0; index < 11; index++)
        {
            var request = new PurchaseRequest
            {
                RequestNumber = $"PR-RECENT-{index:D2}",
                RequesterUserId = 1,
                RequesterName = "Demo Requester",
                CreatedAtUtc = baseline,
                UpdatedAtUtc = baseline.AddDays(index + 1)
            };
            fixture.DbContext.PurchaseRequests.Add(request);
            await fixture.DbContext.SaveChangesAsync();
            await engine.StartAsync("PurchaseRequest", request.Id,
                new WorkflowActor(1, "Demo Requester", [ApplicationRoles.Requester]), CancellationToken.None);
        }

        var busiest = await fixture.DbContext.WorkflowProcessInstances
            .SingleAsync(instance => instance.EntityType == "PurchaseRequest" &&
                instance.EntityId == fixture.DbContext.PurchaseRequests
                    .Where(request => request.RequestNumber == "PR-RECENT-10")
                    .Select(request => request.Id).Single());
        for (var index = 0; index < 60; index++)
        {
            busiest.History.Add(new WorkflowHistory
            {
                ActionCode = "START",
                ToStepCode = "DRAFT",
                ActionBy = $"Actor {index}",
                ActionAtUtc = baseline
            });
        }
        await fixture.DbContext.SaveChangesAsync();

        var dashboard = await fixture.CreateService(1, ApplicationRoles.Requester).GetAsync(CancellationToken.None);

        Assert.Equal(Enumerable.Range(1, 10).Reverse().Select(index => $"PR-RECENT-{index:D2}"),
            dashboard.RecentActivity.Select(activity => activity.Reference));
        Assert.Equal(10, dashboard.RecentActivity.Select(activity => activity.Key).Distinct().Count());
        Assert.All(dashboard.RecentActivity, activity => Assert.Equal("Draft", activity.Title));
        Assert.Equal(baseline.AddDays(11), dashboard.RecentActivity[0].OccurredAtUtc);
        Assert.Equal("Actor 59", dashboard.RecentActivity[0].ActorName);
    }

    [Theory]
    [InlineData(PurchaseOrderStatus.Issued, "Issued", "Procurement Officer")]
    [InlineData(PurchaseOrderStatus.PartiallyReceived, "Partially Received", "Warehouse Officer")]
    [InlineData(PurchaseOrderStatus.Received, "Received", "Warehouse Officer")]
    [InlineData(PurchaseOrderStatus.Cancelled, "Cancelled", "Cancelling Officer")]
    public async Task GetAsync_ShowsOneOrderWithCurrentStatusAfterApproval(
        PurchaseOrderStatus status, string expectedTitle, string expectedActor)
    {
        await using var fixture = await DashboardFixture.CreateAsync();
        var order = await fixture.CreateApprovedOrderAsync();
        var issuedAt = DateTimeOffset.UtcNow.AddDays(1);
        var updatedAt = issuedAt.AddDays(1);
        order.Status = status;
        order.IssuedAtUtc = issuedAt;
        order.IssuedByName = "Procurement Officer";
        order.UpdatedAtUtc = updatedAt;
        if (status == PurchaseOrderStatus.Cancelled)
        {
            order.CancelledAtUtc = updatedAt;
            order.CancelledByName = "Cancelling Officer";
        }
        if (status is PurchaseOrderStatus.PartiallyReceived or PurchaseOrderStatus.Received)
        {
            fixture.DbContext.GoodsReceipts.Add(new GoodsReceipt
            {
                GoodsReceiptNumber = "GR-001",
                PurchaseOrder = order,
                SupplierId = order.SupplierId,
                Status = GoodsReceiptStatus.Posted,
                CreatedByUserId = 4,
                CreatedByName = "Warehouse Officer",
                PostedAtUtc = updatedAt,
                PostedByName = "Warehouse Officer"
            });
        }
        await fixture.DbContext.SaveChangesAsync();

        var dashboard = await fixture.CreateService(4, ApplicationRoles.Admin).GetAsync(CancellationToken.None);

        var activity = Assert.Single(dashboard.RecentActivity, item => item.Module == "Purchase Order");
        Assert.Equal(order.PurchaseOrderNumber, activity.Reference);
        Assert.Equal(expectedTitle, activity.Title);
        Assert.Equal(expectedActor, activity.ActorName);
        Assert.Equal(updatedAt, activity.OccurredAtUtc);
    }

    private static Project1.Api.DTOs.Dashboard.DashboardSummaryCardResponse Card(
        Project1.Api.DTOs.Dashboard.DashboardResponse dashboard,
        string key) => dashboard.SummaryCards.Single(card => card.Key == key);

    private sealed class DashboardFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;

        private DashboardFixture(
            SqliteConnection connection,
            AppDbContext dbContext,
            PurchaseRequest ownRequest,
            PurchaseRequest otherRequest)
        {
            this.connection = connection;
            DbContext = dbContext;
            OwnRequest = ownRequest;
            OtherRequest = otherRequest;
        }

        public AppDbContext DbContext { get; }

        public PurchaseRequest OwnRequest { get; }

        public PurchaseRequest OtherRequest { get; }

        public DashboardService CreateService(int userId, params string[] roles) =>
            new(DbContext, new FakeCurrentUserContext(userId, roles));

        public async Task<PurchaseOrder> CreateApprovedOrderAsync()
        {
            var supplier = new Supplier { Code = "SUP-001", Name = "Supplier" };
            var order = new PurchaseOrder
            {
                PurchaseOrderNumber = "PO-001",
                PurchaseRequest = OwnRequest,
                Supplier = supplier,
                Quotation = new Quotation
                {
                    QuotationNumber = "QT-001",
                    PurchaseRequest = OwnRequest,
                    Supplier = supplier,
                    CreatedByUserId = 4,
                    CreatedByName = "Demo Admin"
                },
                CreatedByUserId = 4,
                CreatedByName = "Demo Admin",
                Status = PurchaseOrderStatus.Approved
            };
            var draft = new WorkflowStepTemplate { Code = "DRAFT", Name = "Draft", IsInitial = true };
            var approved = new WorkflowStepTemplate { Code = "APPROVED", Name = "Approved", IsTerminal = true };
            draft.Actions.Add(new WorkflowActionTemplate
            {
                Code = "APPROVE",
                Name = "Approve",
                ToStepTemplate = approved,
                Actioners = [new WorkflowActionerTemplate { ActionerType = WorkflowActionerType.Requester }]
            });
            DbContext.WorkflowProcessTemplates.Add(new WorkflowProcessTemplate
            {
                Code = "PO-TEST", Name = "Purchase Order", EntityType = "PurchaseOrder",
                Version = 1, IsPublished = true, IsActive = true, Steps = [draft, approved]
            });
            DbContext.PurchaseOrders.Add(order);
            await DbContext.SaveChangesAsync();
            var engine = new WorkflowEngine(DbContext);
            var actor = new WorkflowActor(4, "Demo Admin", [ApplicationRoles.Admin]);
            await engine.StartAsync("PurchaseOrder", order.Id, actor, CancellationToken.None);
            await engine.ExecuteActionAsync("PurchaseOrder", order.Id, "APPROVE", actor, null, CancellationToken.None);
            return order;
        }

        public static async Task<DashboardFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;
            var dbContext = new AppDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();

            var department = new Department { Code = "IT", Name = "Information Technology" };
            dbContext.Departments.Add(department);
            await dbContext.SaveChangesAsync();

            dbContext.Users.AddRange(
                User(1, "requester@demo.local", "Demo Requester", department.Id),
                User(2, "department@demo.local", "Department Approver", department.Id),
                User(4, "admin@demo.local", "Demo Admin", department.Id),
                User(7, "procurement@demo.local", "Procurement Officer", department.Id));

            var product = new Product
            {
                Code = "ITEM-0001",
                Name = "Keyboard",
                DefaultUnitPrice = 100m,
                ReorderLevel = 3m
            };
            dbContext.Products.Add(product);

            var ownRequest = new PurchaseRequest
            {
                RequestNumber = "PR-OWN",
                RequesterUserId = 1,
                RequesterName = "Demo Requester",
                DepartmentId = department.Id,
                RequiredDate = new DateOnly(2030, 1, 1),
                Justification = "Owned request"
            };
            var otherRequest = new PurchaseRequest
            {
                RequestNumber = "PR-OTHER",
                RequesterUserId = 2,
                RequesterName = "Department Approver",
                DepartmentId = department.Id,
                RequiredDate = new DateOnly(2030, 1, 2),
                Justification = "Other request"
            };
            dbContext.PurchaseRequests.AddRange(ownRequest, otherRequest);
            await dbContext.SaveChangesAsync();

            var workflowEngine = new WorkflowEngine(dbContext);
            await workflowEngine.StartAsync(
                "PurchaseRequest",
                ownRequest.Id,
                new WorkflowActor(1, "Demo Requester", [ApplicationRoles.Requester]),
                CancellationToken.None);
            await workflowEngine.StartAsync(
                "PurchaseRequest",
                otherRequest.Id,
                new WorkflowActor(2, "Department Approver", [ApplicationRoles.Requester]),
                CancellationToken.None);
            await workflowEngine.ExecuteActionAsync(
                "PurchaseRequest",
                otherRequest.Id,
                "SUBMIT",
                new WorkflowActor(2, "Department Approver", [ApplicationRoles.Requester]),
                null,
                CancellationToken.None);

            return new DashboardFixture(connection, dbContext, ownRequest, otherRequest);
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await connection.DisposeAsync();
        }

        private static ApplicationUser User(
            int id,
            string email,
            string name,
            int departmentId) => new()
        {
            Id = id,
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            FullName = name,
            DepartmentId = departmentId,
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString()
        };
    }

    private sealed class FakeCurrentUserContext(int userId, IReadOnlyCollection<string> roles)
        : ICurrentUserContext
    {
        public bool IsAuthenticated => true;

        public int UserId => userId;

        public string DisplayName => "Dashboard User";

        public int? DepartmentId => 1;

        public IReadOnlyCollection<string> Roles => roles;

        public bool IsInRole(string role) => roles.Contains(role, StringComparer.OrdinalIgnoreCase);
    }
}
