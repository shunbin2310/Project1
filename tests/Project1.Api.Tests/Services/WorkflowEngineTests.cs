using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Project1.Api.Data;
using Project1.Api.Entities.Workflows;
using Project1.Api.Services.Workflows;

namespace Project1.Api.Tests.Services;

public sealed class WorkflowEngineTests
{
    [Fact]
    public async Task StartAsync_CopiesPublishedTemplateIntoInstanceSnapshot()
    {
        await using var fixture = await WorkflowFixture.CreateAsync();
        var engine = new WorkflowEngine(fixture.DbContext);

        var started = await engine.StartAsync(
            "PurchaseRequest",
            101,
            new WorkflowActor(10, "Alex Tan", []),
            CancellationToken.None);

        Assert.Equal(WorkflowExecutionStatus.Success, started.Status);
        Assert.Equal(1, started.Workflow!.TemplateVersion);
        Assert.Equal("DRAFT", started.Workflow.CurrentStepCode);
        var submit = Assert.Single(started.Workflow.AvailableActions);
        Assert.Equal("SUBMIT", submit.Code);
        Assert.Equal("DEPARTMENT_REVIEW", submit.ToStepCode);
        var requester = Assert.Single(submit.Actioners);
        Assert.Equal(WorkflowActionerType.Requester, requester.ActionerType);
        Assert.Equal("10", requester.ActionerKey);
    }

    [Fact]
    public async Task ExistingInstance_IsNotAffectedWhenTemplateChanges()
    {
        await using var fixture = await WorkflowFixture.CreateAsync();
        var engine = new WorkflowEngine(fixture.DbContext);
        await engine.StartAsync(
            "PurchaseRequest",
            101,
            new WorkflowActor(10, "Alex Tan", []),
            CancellationToken.None);

        var departmentStep = await fixture.DbContext.WorkflowStepTemplates
            .SingleAsync(step => step.Code == "DEPARTMENT_REVIEW");
        departmentStep.Name = "Changed Department Review";
        await fixture.DbContext.SaveChangesAsync();

        var existing = await engine.GetInstanceAsync(
            "PurchaseRequest",
            101,
            CancellationToken.None);
        var newer = await engine.StartAsync(
            "PurchaseRequest",
            102,
            new WorkflowActor(11, "Jamie Lee", []),
            CancellationToken.None);

        Assert.Equal("Department Review", existing!.AvailableActions.Single().ToStepName);
        Assert.Equal("Changed Department Review", newer.Workflow!.AvailableActions.Single().ToStepName);
    }

    [Fact]
    public async Task ExecuteActionAsync_EnforcesRolesAndCompletesMultiStageWorkflow()
    {
        await using var fixture = await WorkflowFixture.CreateAsync();
        var engine = new WorkflowEngine(fixture.DbContext);
        await engine.StartAsync(
            "PurchaseRequest",
            101,
            new WorkflowActor(10, "Alex Tan", []),
            CancellationToken.None);

        var submitted = await engine.ExecuteActionAsync(
            "PurchaseRequest",
            101,
            "SUBMIT",
            new WorkflowActor(10, "Alex Tan", []),
            null,
            CancellationToken.None);
        var unauthorized = await engine.ExecuteActionAsync(
            "PurchaseRequest",
            101,
            "APPROVE",
            new WorkflowActor(11, "Finance Manager", ["FINANCE_APPROVER"]),
            null,
            CancellationToken.None);
        var departmentApproved = await engine.ExecuteActionAsync(
            "PurchaseRequest",
            101,
            "APPROVE",
            new WorkflowActor(12, "Department Manager", ["DEPARTMENT_APPROVER"]),
            "Department approved.",
            CancellationToken.None);
        var financeApproved = await engine.ExecuteActionAsync(
            "PurchaseRequest",
            101,
            "APPROVE",
            new WorkflowActor(11, "Finance Manager", ["FINANCE_APPROVER"]),
            "Budget confirmed.",
            CancellationToken.None);

        Assert.Equal("DEPARTMENT_REVIEW", submitted.Workflow!.CurrentStepCode);
        Assert.Equal(WorkflowExecutionStatus.Unauthorized, unauthorized.Status);
        Assert.Equal("FINANCE_REVIEW", departmentApproved.Workflow!.CurrentStepCode);
        Assert.Equal("APPROVED", financeApproved.Workflow!.CurrentStepCode);
        Assert.Equal(WorkflowInstanceStatus.Completed, financeApproved.Workflow.Status);
        Assert.Equal(
            ["START", "SUBMIT", "APPROVE", "APPROVE"],
            financeApproved.Workflow.History.Select(history => history.ActionCode));
    }

    [Fact]
    public async Task ExecuteActionAsync_RequiresCommentForReject()
    {
        await using var fixture = await WorkflowFixture.CreateAsync();
        var engine = new WorkflowEngine(fixture.DbContext);
        await engine.StartAsync(
            "PurchaseRequest",
            101,
            new WorkflowActor(10, "Alex Tan", []),
            CancellationToken.None);
        await engine.ExecuteActionAsync(
            "PurchaseRequest",
            101,
            "SUBMIT",
            new WorkflowActor(10, "Alex Tan", []),
            null,
            CancellationToken.None);

        var result = await engine.ExecuteActionAsync(
            "PurchaseRequest",
            101,
            "REJECT",
            new WorkflowActor(12, "Department Manager", ["DEPARTMENT_APPROVER"]),
            null,
            CancellationToken.None);

        Assert.Equal(WorkflowExecutionStatus.CommentRequired, result.Status);
        Assert.Equal("A comment is required for action 'REJECT'.", result.ErrorMessage);
    }

    private sealed class WorkflowFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;

        private WorkflowFixture(SqliteConnection connection, AppDbContext dbContext)
        {
            this.connection = connection;
            DbContext = dbContext;
        }

        public AppDbContext DbContext { get; }

        public static async Task<WorkflowFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;
            var dbContext = new AppDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();

            return new WorkflowFixture(connection, dbContext);
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
