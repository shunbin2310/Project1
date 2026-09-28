using Project1.Api.DTOs.EmailRecords;

namespace Project1.Api.Services.EmailRecords;

public enum EmailRecordOperationStatus
{
    Success,
    NotFound,
    InvalidState
}

public sealed record EmailRecordOperationResult(
    EmailRecordOperationStatus Status,
    EmailRecordDetailsResponse? EmailRecord = null,
    string? ErrorMessage = null);
