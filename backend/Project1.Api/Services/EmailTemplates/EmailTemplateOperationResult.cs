using Project1.Api.DTOs.EmailTemplates;

namespace Project1.Api.Services.EmailTemplates;

public enum EmailTemplateOperationStatus
{
    Success,
    NotFound,
    ValidationFailed,
    Conflict,
    InvalidState
}

public sealed record EmailTemplateOperationResult(
    EmailTemplateOperationStatus Status,
    EmailTemplateResponse? Template = null,
    EmailTemplatePreviewResponse? Preview = null,
    string? ErrorMessage = null);
