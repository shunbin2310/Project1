namespace Project1.Api.Email;

public sealed record EmailMessage(
    string RecipientEmail,
    string Subject,
    string HtmlBody,
    string? FromAddress = null,
    string? FromName = null,
    string? CcRecipients = null,
    string? BccRecipients = null);
