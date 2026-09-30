namespace Project1.Api.Email;

public sealed record EmailMessageAttachment(
    string FileName,
    string ContentType,
    byte[] Content);
