using Project1.Api.DTOs.EmailTemplates;
using Project1.Api.Entities;

namespace Project1.Api.Email;

public interface IEmailTemplateRenderer
{
    string? Validate(
        string subjectTemplate,
        string htmlBodyTemplate,
        string? ccRecipients,
        string? bccRecipients);

    EmailTemplatePreviewResponse RenderPreview(
        string subjectTemplate,
        string htmlBodyTemplate,
        string? ccRecipients,
        string? bccRecipients);

    Task<EmailTemplateRenderResult> RenderPurchaseOrderAsync(
        PurchaseOrder purchaseOrder,
        string recipientEmail,
        CancellationToken cancellationToken);
}
