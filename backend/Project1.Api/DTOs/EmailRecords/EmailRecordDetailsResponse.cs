using Project1.Api.Entities;

namespace Project1.Api.DTOs.EmailRecords;

public sealed record EmailRecordDetailsResponse(
    int Id,
    string SourceType,
    int SourceId,
    string SourceReference,
    string FromAddress,
    string FromName,
    string RecipientEmail,
    string? CcRecipients,
    string? BccRecipients,
    string Subject,
    string HtmlBody,
    EmailDeliveryStatus Status,
    int AttemptCount,
    int? CreatedByUserId,
    string? CreatedByName,
    int? ResentFromEmailOutboxId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    DateTimeOffset? LastAttemptAtUtc,
    DateTimeOffset? SentAtUtc,
    string? LastError);
