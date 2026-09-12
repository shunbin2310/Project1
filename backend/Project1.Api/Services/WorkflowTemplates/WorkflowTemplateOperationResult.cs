using Project1.Api.DTOs.WorkflowTemplates;

namespace Project1.Api.Services.WorkflowTemplates;

public enum WorkflowTemplateOperationStatus
{
    Success,
    NotFound,
    ValidationFailed,
    Conflict,
    InvalidState
}

public sealed record WorkflowTemplateOperationResult(
    WorkflowTemplateOperationStatus Status,
    WorkflowTemplateResponse? Template = null,
    string? ErrorMessage = null);
