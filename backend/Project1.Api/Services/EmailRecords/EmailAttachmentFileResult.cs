namespace Project1.Api.Services.EmailRecords;

public sealed record EmailAttachmentFileResult(
    string FileName,
    string ContentType,
    byte[] Content);
