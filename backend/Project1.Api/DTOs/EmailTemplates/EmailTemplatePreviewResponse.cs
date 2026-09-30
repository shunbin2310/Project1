namespace Project1.Api.DTOs.EmailTemplates;

public sealed record EmailTemplatePreviewResponse(
    string RecipientEmail,
    string? CcRecipients,
    string? BccRecipients,
    string Subject,
    string HtmlBody);
