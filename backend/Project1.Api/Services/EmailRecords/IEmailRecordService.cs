using Project1.Api.DTOs.EmailRecords;
using Project1.Api.Entities;

namespace Project1.Api.Services.EmailRecords;

public interface IEmailRecordService
{
    Task<IReadOnlyList<EmailRecordSummaryResponse>> GetAllAsync(
        string? search,
        EmailDeliveryStatus? status,
        string? sourceType,
        DateOnly? createdFrom,
        DateOnly? createdTo,
        CancellationToken cancellationToken);

    Task<EmailRecordDetailsResponse?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken);

    Task<EmailAttachmentFileResult?> GetAttachmentAsync(
        int emailRecordId,
        int attachmentId,
        CancellationToken cancellationToken);

    Task<EmailRecordOperationResult> RetryAsync(
        int id,
        CancellationToken cancellationToken);

    Task<EmailRecordOperationResult> ResendAsync(
        int id,
        CancellationToken cancellationToken);
}
