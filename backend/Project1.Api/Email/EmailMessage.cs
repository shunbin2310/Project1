namespace Project1.Api.Email;

public sealed record EmailMessage(
    string RecipientEmail,
    string Subject,
    string HtmlBody);
