using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Project1.Api.Authentication;
using Project1.Api.Data;
using Project1.Api.Entities;
using Project1.Api.Entities.Identity;
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
    public async Task GetAsync_CatalogManagerRoutesStockRemindersToProducts()
    {
        await using var fixture = await DashboardFixture.CreateAsync();
        var service = fixture.CreateService(7, ApplicationRoles.CatalogManager);

        var dashboard = await service.GetAsync(CancellationToken.None);

        var card = Card(dashboard, "out-of-stock");
        Assert.Equal(1, card.Value);
        Assert.Equal("/products", card.Route);
        Assert.DoesNotContain(dashboard.RecentActivity, activity =>
            activity.Module == "Purchase Request");
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
                User(7, "catalog@demo.local", "Catalog Manager", department.Id));

            var category = new ProductCategory { Code = "CAT-001", Name = "Equipment" };
            var unit = new UnitOfMeasure { Code = "UNIT", Name = "Unit" };
            var product = new Product
            {
                Code = "ITEM-0001",
                Name = "Keyboard",
                ProductCategory = category,
                UnitOfMeasure = unit,
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
