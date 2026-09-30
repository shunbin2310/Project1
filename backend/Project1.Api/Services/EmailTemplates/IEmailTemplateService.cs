using Project1.Api.DTOs.EmailTemplates;

namespace Project1.Api.Services.EmailTemplates;

public interface IEmailTemplateService
{
    Task<IReadOnlyList<EmailTemplateSummaryResponse>> GetAllAsync(
        CancellationToken cancellationToken);

    Task<EmailTemplateResponse?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken);

    Task<EmailTemplateOperationResult> CreateVersionAsync(
        int sourceTemplateId,
        CancellationToken cancellationToken);

    Task<EmailTemplateOperationResult> UpdateAsync(
        int id,
        UpdateEmailTemplateRequest request,
        CancellationToken cancellationToken);

    Task<EmailTemplateOperationResult> PreviewAsync(
        PreviewEmailTemplateRequest request,
        CancellationToken cancellationToken);

    Task<EmailTemplateOperationResult> PublishAsync(
        int id,
        CancellationToken cancellationToken);

    Task<EmailTemplateOperationResult> DeleteAsync(
        int id,
        CancellationToken cancellationToken);
}
