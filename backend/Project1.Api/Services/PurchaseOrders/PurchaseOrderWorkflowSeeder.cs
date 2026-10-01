using Microsoft.EntityFrameworkCore;
using Project1.Api.Authentication;
using Project1.Api.Data;
using Project1.Api.Entities.Workflows;

namespace Project1.Api.Services.PurchaseOrders;

public static class PurchaseOrderWorkflowSeeder
{
    public static async Task SeedPurchaseOrderWorkflowAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (await dbContext.WorkflowProcessTemplates.AnyAsync(
            template => template.Code == PurchaseOrderWorkflow.TemplateCode))
        {
            return;
        }

        var draft = new WorkflowStepTemplate
        {
            Code = PurchaseOrderWorkflow.DraftStep,
            Name = "Draft",
            DisplayOrder = 1,
            IsInitial = true
        };
        var pendingApproval = new WorkflowStepTemplate
        {
            Code = PurchaseOrderWorkflow.PendingApprovalStep,
            Name = "Pending Approval",
            DisplayOrder = 2
        };
        var approved = new WorkflowStepTemplate
        {
            Code = PurchaseOrderWorkflow.ApprovedStep,
            Name = "Approved",
            DisplayOrder = 3,
            IsTerminal = true
        };

        draft.Actions.Add(new WorkflowActionTemplate
        {
            Code = PurchaseOrderWorkflow.SubmitAction,
            Name = "Submit for approval",
            ToStepTemplate = pendingApproval,
            Actioners =
            [
                new WorkflowActionerTemplate
                {
                    ActionerType = WorkflowActionerType.Requester
                }
            ]
        });
        pendingApproval.Actions.Add(new WorkflowActionTemplate
        {
            Code = PurchaseOrderWorkflow.ApproveAction,
            Name = "Approve purchase order",
            ToStepTemplate = approved,
            Actioners =
            [
                new WorkflowActionerTemplate
                {
                    ActionerType = WorkflowActionerType.Role,
                    ActionerKey = ApplicationRoles.PurchaseOrderApprover
                }
            ]
        });
        pendingApproval.Actions.Add(new WorkflowActionTemplate
        {
            Code = PurchaseOrderWorkflow.RejectAction,
            Name = "Reject purchase order",
            ToStepTemplate = draft,
            RequiresComment = true,
            Actioners =
            [
                new WorkflowActionerTemplate
                {
                    ActionerType = WorkflowActionerType.Role,
                    ActionerKey = ApplicationRoles.PurchaseOrderApprover
                }
            ]
        });

        var now = DateTimeOffset.UtcNow;
        dbContext.WorkflowProcessTemplates.Add(new WorkflowProcessTemplate
        {
            Code = PurchaseOrderWorkflow.TemplateCode,
            Name = PurchaseOrderWorkflow.TemplateName,
            EntityType = PurchaseOrderWorkflow.EntityType,
            Version = 1,
            IsPublished = true,
            IsActive = true,
            CreatedAtUtc = now,
            PublishedAtUtc = now,
            Steps = [draft, pendingApproval, approved]
        });

        await dbContext.SaveChangesAsync();
    }
}
