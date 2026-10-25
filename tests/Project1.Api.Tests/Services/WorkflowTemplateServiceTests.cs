using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Project1.Api.Authentication;
using Project1.Api.Data;
using Project1.Api.DTOs.WorkflowTemplates;
using Project1.Api.Entities.Workflows;
using Project1.Api.Services.PurchaseOrders;
using Project1.Api.Services.WorkflowTemplates;
using Project1.Api.Services.Workflows;

namespace Project1.Api.Tests.Services;

public sealed class WorkflowTemplateServiceTests
{
    [Fact]
    public async Task CreateAsync_CreatesNewVersionOneDraftFromCompleteDefinition()
    {
        await using var fixture = await WorkflowTemplateFixture.CreateAsync();

        var result = await fixture.Service.CreateAsync(
            NewTemplateRequest(),
            CancellationToken.None);

        Assert.Equal(WorkflowTemplateOperationStatus.Success, result.Status);
        Assert.Equal("EXPENSE_CLAIM", result.Template!.Code);
        Assert.Equal(1, result.Template.Version);
        Assert.False(result.Template.IsPublished);
        Assert.False(result.Template.IsActive);
        Assert.Equal(2, result.Template.Steps.Count);
    }

    [Fact]
    public async Task CreateVersionAsync_CopiesActivePublishedTemplateIntoDraft()
    {
        await using var fixture = await WorkflowTemplateFixture.CreateAsync();

        var result = await fixture.Service.CreateVersionAsync(1, CancellationToken.None);
        var duplicate = await fixture.Service.CreateVersionAsync(1, CancellationToken.None);

        Assert.Equal(WorkflowTemplateOperationStatus.Success, result.Status);
        Assert.Equal(2, result.Template!.Version);
        Assert.False(result.Template.IsPublished);
        Assert.Equal(5, result.Template.Steps.Count);
        Assert.Equal(WorkflowTemplateOperationStatus.Conflict, duplicate.Status);
    }

    [Fact]
    public async Task CreateAsync_AllowsReturnToInitialStep()
    {
        await using var fixture = await WorkflowTemplateFixture.CreateAsync(seedPurchaseOrderWorkflow: true);
        var source = await fixture.DbContext.WorkflowProcessTemplates
            .SingleAsync(template => template.Code == PurchaseOrderWorkflow.TemplateCode);
        var definition = UpdateRequestFrom(await fixture.GetTemplateAsync(source.Id));

        var result = await fixture.Service.CreateAsync(new CreateWorkflowTemplateRequest
        {
            Code = "REWORK_APPROVAL",
            Name = "Approval with rework",
            EntityType = "Rework",
            Steps = definition.Steps
        }, CancellationToken.None);

        Assert.Equal(WorkflowTemplateOperationStatus.Success, result.Status);
        Assert.Equal("DRAFT", result.Template!.Steps.Single(step => step.Code == "PENDING_APPROVAL")
            .Actions.Single(action => action.Code == "REJECT").ToStepCode);
    }

    [Fact]
    public async Task PurchaseOrderVersion_CanBeCopiedUpdatedPublishedAndResubmittedAfterRejection()
    {
        await using var fixture = await WorkflowTemplateFixture.CreateAsync(seedPurchaseOrderWorkflow: true);
        var source = await fixture.DbContext.WorkflowProcessTemplates
            .SingleAsync(template => template.Code == PurchaseOrderWorkflow.TemplateCode);
        var engine = new WorkflowEngine(fixture.DbContext);
        var requester = new WorkflowActor(10, "Procurement Officer", [ApplicationRoles.ProcurementOfficer]);
        var approver = new WorkflowActor(11, "Purchase Order Approver", [ApplicationRoles.PurchaseOrderApprover]);
        var original = await engine.StartAsync("PurchaseOrder", 101, requester, CancellationToken.None);
        Assert.Equal(WorkflowExecutionStatus.Success, original.Status);

        var copied = await fixture.Service.CreateVersionAsync(source.Id, CancellationToken.None);
        Assert.Equal(WorkflowTemplateOperationStatus.Success, copied.Status);
        var draft = copied.Template!;
        Assert.Equal(2, draft.Version);
        Assert.False(draft.IsPublished);
        Assert.False(draft.IsActive);
        var reject = draft.Steps.Single(step => step.Code == "PENDING_APPROVAL")
            .Actions.Single(action => action.Code == "REJECT");
        Assert.Equal("DRAFT", reject.ToStepCode);
        Assert.True(reject.RequiresComment);

        var updated = await fixture.Service.UpdateAsync(draft.Id, UpdateRequestFrom(draft) with
        {
            Name = "Purchase Order Approval Updated"
        }, CancellationToken.None);
        Assert.Equal(WorkflowTemplateOperationStatus.Success, updated.Status);
        var published = await fixture.Service.PublishAsync(draft.Id, CancellationToken.None);
        Assert.Equal(WorkflowTemplateOperationStatus.Success, published.Status);
        Assert.True(published.Template!.IsActive);
        Assert.True(published.Template.IsPublished);
        Assert.False((await fixture.GetTemplateAsync(source.Id)).IsActive);

        var existing = await engine.GetInstanceAsync("PurchaseOrder", 101, CancellationToken.None);
        Assert.Equal(1, existing!.TemplateVersion);
        Assert.Equal(PurchaseOrderWorkflow.TemplateName, existing.TemplateName);
        Assert.Equal("DRAFT", existing.CurrentStepCode);
        Assert.Single(existing.History);
        var started = await engine.StartAsync("PurchaseOrder", 102, requester, CancellationToken.None);
        Assert.Equal(WorkflowExecutionStatus.Success, started.Status);
        Assert.Equal(2, started.Workflow!.TemplateVersion);
        Assert.Equal("Purchase Order Approval Updated", started.Workflow.TemplateName);

        var submitted = await engine.ExecuteActionAsync(
            "PurchaseOrder", 102, "SUBMIT", requester, null, CancellationToken.None);
        Assert.Equal(WorkflowExecutionStatus.Success, submitted.Status);
        var unauthorized = await engine.ExecuteActionAsync(
            "PurchaseOrder", 102, "REJECT", requester, "Please revise.", CancellationToken.None);
        Assert.Equal(WorkflowExecutionStatus.Unauthorized, unauthorized.Status);
        var missingComment = await engine.ExecuteActionAsync(
            "PurchaseOrder", 102, "REJECT", approver, null, CancellationToken.None);
        Assert.Equal(WorkflowExecutionStatus.CommentRequired, missingComment.Status);
        var rejected = await engine.ExecuteActionAsync(
            "PurchaseOrder", 102, "REJECT", approver, "Please revise.", CancellationToken.None);
        Assert.Equal(WorkflowExecutionStatus.Success, rejected.Status);
        Assert.Equal("DRAFT", rejected.Workflow!.CurrentStepCode);
        Assert.Equal(WorkflowInstanceStatus.Running, rejected.Workflow.Status);
        var resubmitted = await engine.ExecuteActionAsync(
            "PurchaseOrder", 102, "SUBMIT", requester, null, CancellationToken.None);
        Assert.Equal(WorkflowExecutionStatus.Success, resubmitted.Status);
        Assert.Equal("PENDING_APPROVAL", resubmitted.Workflow!.CurrentStepCode);
        var approved = await engine.ExecuteActionAsync(
            "PurchaseOrder", 102, "APPROVE", approver, null, CancellationToken.None);
        Assert.Equal(WorkflowExecutionStatus.Success, approved.Status);
        Assert.Equal("APPROVED", approved.Workflow!.CurrentStepCode);
        Assert.Equal(WorkflowInstanceStatus.Completed, approved.Workflow.Status);
        Assert.Equal(["START", "SUBMIT", "REJECT", "SUBMIT", "APPROVE"],
            approved.Workflow.History.Select(history => history.ActionCode));
    }

    [Theory]
    [InlineData("FINANCE_REVIEW", "cannot point back to its own step")]
    [InlineData("DEPARTMENT_REVIEW", "cycle")]
    public async Task CreateVersionAsync_RejectsSelfLoopsAndCyclesBetweenReviewSteps(
        string rejectionTarget,
        string expectedError)
    {
        await using var fixture = await WorkflowTemplateFixture.CreateAsync();
        var rejection = await fixture.DbContext.WorkflowActionTemplates.SingleAsync(action =>
            action.FromStepTemplate.Code == "FINANCE_REVIEW" && action.Code == "REJECT");
        rejection.ToStepTemplateId = await fixture.DbContext.WorkflowStepTemplates
            .Where(step => step.Code == rejectionTarget)
            .Select(step => step.Id)
            .SingleAsync();
        await fixture.DbContext.SaveChangesAsync();

        var result = await fixture.Service.CreateVersionAsync(1, CancellationToken.None);

        Assert.Equal(WorkflowTemplateOperationStatus.ValidationFailed, result.Status);
        Assert.Contains(expectedError, result.ErrorMessage);
        Assert.False(await fixture.DbContext.WorkflowProcessTemplates
            .AnyAsync(template => template.Code == "PURCHASE_REQUEST" && template.Version == 2));
    }

    [Fact]
    public async Task UpdateAsync_RejectsPublishedTemplateAndInvalidGraph()
    {
        await using var fixture = await WorkflowTemplateFixture.CreateAsync();

        var publishedResult = await fixture.Service.UpdateAsync(
            1,
            UpdateRequestFrom(await fixture.GetTemplateAsync(1)),
            CancellationToken.None);

        var draft = await fixture.Service.CreateVersionAsync(1, CancellationToken.None);
        var draftTemplate = Assert.IsType<WorkflowTemplateResponse>(draft.Template);
        var invalidRequest = UpdateRequestFrom(draftTemplate) with
        {
            Steps = draftTemplate.Steps
                .Where(step => step.Code != "FINANCE_REVIEW")
                .Select(ToStepRequest)
                .ToList()
        };
        var invalidResult = await fixture.Service.UpdateAsync(
            draftTemplate.Id,
            invalidRequest,
            CancellationToken.None);

        Assert.Equal(WorkflowTemplateOperationStatus.InvalidState, publishedResult.Status);
        Assert.Equal(WorkflowTemplateOperationStatus.ValidationFailed, invalidResult.Status);
        Assert.Contains("unknown step", invalidResult.ErrorMessage);
    }

    [Fact]
    public async Task PublishAsync_ActivatesNewVersionWithoutChangingExistingInstance()
    {
        await using var fixture = await WorkflowTemplateFixture.CreateAsync();
        var engine = new WorkflowEngine(fixture.DbContext);
        await engine.StartAsync(
            "PurchaseRequest",
            101,
            new WorkflowActor(10, "Original Requester", [ApplicationRoles.Requester]),
            CancellationToken.None);

        var draft = await fixture.Service.CreateVersionAsync(1, CancellationToken.None);
        var draftTemplate = Assert.IsType<WorkflowTemplateResponse>(draft.Template);
        var updateRequest = UpdateRequestFrom(draftTemplate);
        updateRequest = updateRequest with
        {
            Name = "Purchase Request Approval Updated",
            Steps = updateRequest.Steps.Select(step => step.Code == "DEPARTMENT_REVIEW"
                ? new WorkflowStepDefinitionRequest
                {
                    Code = step.Code,
                    Name = "Manager Review",
                    DisplayOrder = step.DisplayOrder,
                    IsInitial = step.IsInitial,
                    IsTerminal = step.IsTerminal,
                    Actions = step.Actions
                }
                : step).ToList()
        };
        var updated = await fixture.Service.UpdateAsync(
            draftTemplate.Id,
            updateRequest,
            CancellationToken.None);
        var published = await fixture.Service.PublishAsync(
            draftTemplate.Id,
            CancellationToken.None);

        var oldTemplate = await fixture.GetTemplateAsync(1);
        var oldInstance = await engine.GetInstanceAsync(
            "PurchaseRequest",
            101,
            CancellationToken.None);
        var newInstance = await engine.StartAsync(
            "PurchaseRequest",
            102,
            new WorkflowActor(11, "New Requester", [ApplicationRoles.Requester]),
            CancellationToken.None);

        Assert.Equal(WorkflowTemplateOperationStatus.Success, updated.Status);
        Assert.Equal(WorkflowTemplateOperationStatus.Success, published.Status);
        Assert.True(published.Template!.IsPublished);
        Assert.True(published.Template.IsActive);
        Assert.False(oldTemplate.IsActive);
        Assert.Equal(1, oldInstance!.TemplateVersion);
        Assert.Equal("Department Review", oldInstance.AvailableActions.Single().ToStepName);
        Assert.Equal(2, newInstance.Workflow!.TemplateVersion);
        Assert.Equal("Manager Review", newInstance.Workflow.AvailableActions.Single().ToStepName);
    }

    [Fact]
    public async Task DeleteAsync_DeletesDraftButRejectsPublishedVersion()
    {
        await using var fixture = await WorkflowTemplateFixture.CreateAsync();
        var draft = await fixture.Service.CreateVersionAsync(1, CancellationToken.None);
        var draftId = draft.Template!.Id;
        var stepIds = await fixture.DbContext.WorkflowStepTemplates
            .Where(step => step.ProcessTemplateId == draftId)
            .Select(step => step.Id)
            .ToListAsync();
        var actionIds = await fixture.DbContext.WorkflowActionTemplates
            .Where(action => stepIds.Contains(action.FromStepTemplateId))
            .Select(action => action.Id)
            .ToListAsync();
        var actionerIds = await fixture.DbContext.WorkflowActionerTemplates
            .Where(actioner => actionIds.Contains(actioner.ActionTemplateId))
            .Select(actioner => actioner.Id)
            .ToListAsync();

        var publishedResult = await fixture.Service.DeleteAsync(1, CancellationToken.None);
        var draftResult = await fixture.Service.DeleteAsync(
            draftId,
            CancellationToken.None);

        Assert.Equal(WorkflowTemplateOperationStatus.InvalidState, publishedResult.Status);
        Assert.Equal(WorkflowTemplateOperationStatus.Success, draftResult.Status);
        Assert.Null(await fixture.Service.GetByIdAsync(draftId, CancellationToken.None));
        Assert.False(await fixture.DbContext.WorkflowStepTemplates
            .AnyAsync(step => stepIds.Contains(step.Id)));
        Assert.False(await fixture.DbContext.WorkflowActionTemplates
            .AnyAsync(action => actionIds.Contains(action.Id)));
        Assert.False(await fixture.DbContext.WorkflowActionerTemplates
            .AnyAsync(actioner => actionerIds.Contains(actioner.Id)));
    }

    private static CreateWorkflowTemplateRequest NewTemplateRequest() => new()
    {
        Code = "expense_claim",
        Name = "Expense Claim Approval",
        EntityType = "ExpenseClaim",
        Steps =
        [
            new WorkflowStepDefinitionRequest
            {
                Code = "draft",
                Name = "Draft",
                DisplayOrder = 1,
                IsInitial = true,
                Actions =
                [
                    new WorkflowActionDefinitionRequest
                    {
                        Code = "submit",
                        Name = "Submit",
                        ToStepCode = "approved",
                        Actioners =
                        [
                            new WorkflowActionerDefinitionRequest
                            {
                                ActionerType = WorkflowActionerType.Requester
                            }
                        ]
                    }
                ]
            },
            new WorkflowStepDefinitionRequest
            {
                Code = "approved",
                Name = "Approved",
                DisplayOrder = 2,
                IsTerminal = true
            }
        ]
    };

    private static UpdateWorkflowTemplateRequest UpdateRequestFrom(
        WorkflowTemplateResponse response) => new()
        {
            Name = response.Name,
            Steps = response.Steps.Select(ToStepRequest).ToList()
        };

    private static WorkflowStepDefinitionRequest ToStepRequest(
        WorkflowTemplateStepResponse step) => new()
        {
            Code = step.Code,
            Name = step.Name,
            DisplayOrder = step.DisplayOrder,
            IsInitial = step.IsInitial,
            IsTerminal = step.IsTerminal,
            Actions = step.Actions.Select(action => new WorkflowActionDefinitionRequest
            {
                Code = action.Code,
                Name = action.Name,
                ToStepCode = action.ToStepCode,
                RequiresComment = action.RequiresComment,
                Actioners = action.Actioners.Select(actioner => new WorkflowActionerDefinitionRequest
                {
                    ActionerType = actioner.ActionerType,
                    ActionerKey = actioner.ActionerKey
                }).ToList()
            }).ToList()
        };

    private sealed class WorkflowTemplateFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;

        private WorkflowTemplateFixture(SqliteConnection connection, AppDbContext dbContext)
        {
            this.connection = connection;
            DbContext = dbContext;
            Service = new WorkflowTemplateService(dbContext);
        }

        public AppDbContext DbContext { get; }

        public WorkflowTemplateService Service { get; }

        public static async Task<WorkflowTemplateFixture> CreateAsync(bool seedPurchaseOrderWorkflow = false)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;
            var dbContext = new AppDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();
            if (seedPurchaseOrderWorkflow)
            {
                await using var services = new ServiceCollection()
                    .AddDbContext<AppDbContext>(builder => builder.UseSqlite(connection))
                    .BuildServiceProvider();
                await services.SeedPurchaseOrderWorkflowAsync();
            }
            return new WorkflowTemplateFixture(connection, dbContext);
        }

        public async Task<WorkflowTemplateResponse> GetTemplateAsync(int id) =>
            (await Service.GetByIdAsync(id, CancellationToken.None))!;

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
