using Project1.Api.DTOs.Workflows;

namespace Project1.Api.Services.Workflows;

public enum WorkflowExecutionStatus
{
    Success,
    TemplateNotFound,
    InstanceNotFound,
    ActionNotAvailable,
    Unauthorized,
    CommentRequired
}

public sealed record WorkflowExecutionResult(
    WorkflowExecutionStatus Status,
    WorkflowInstanceResponse? Workflow = null,
    string? ErrorMessage = null);
