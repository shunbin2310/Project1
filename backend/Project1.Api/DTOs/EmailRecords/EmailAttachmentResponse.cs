namespace Project1.Api.DTOs.EmailRecords;

public sealed record EmailAttachmentResponse(
    int Id,
    string FileName,
    string ContentType,
    long FileSizeBytes,
    DateTimeOffset CreatedAtUtc);
