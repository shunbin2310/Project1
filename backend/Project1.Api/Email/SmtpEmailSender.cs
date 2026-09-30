using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace Project1.Api.Email;

public sealed class SmtpEmailSender(IOptions<SmtpOptions> options) : IEmailSender
{
    private readonly SmtpOptions options = options.Value;

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        using var mailMessage = new MailMessage
        {
            From = new MailAddress(
                string.IsNullOrWhiteSpace(message.FromAddress)
                    ? options.FromAddress
                    : message.FromAddress,
                string.IsNullOrWhiteSpace(message.FromName)
                    ? options.FromName
                    : message.FromName),
            Subject = message.Subject,
            Body = message.HtmlBody,
            IsBodyHtml = true
        };
        mailMessage.To.Add(new MailAddress(message.RecipientEmail));
        AddRecipients(mailMessage.CC, message.CcRecipients);
        AddRecipients(mailMessage.Bcc, message.BccRecipients);

        foreach (var attachment in message.Attachments ?? [])
        {
            var contentStream = new MemoryStream(attachment.Content, writable: false);
            mailMessage.Attachments.Add(new Attachment(
                contentStream,
                attachment.FileName,
                attachment.ContentType));
        }

        using var smtpClient = new SmtpClient(options.Host, options.Port)
        {
            EnableSsl = options.UseSsl,
            UseDefaultCredentials = false
        };

        if (!string.IsNullOrWhiteSpace(options.Username))
        {
            smtpClient.Credentials = new NetworkCredential(options.Username, options.Password);
        }

        await smtpClient.SendMailAsync(mailMessage, cancellationToken);
    }

    private static void AddRecipients(MailAddressCollection collection, string? recipients)
    {
        if (string.IsNullOrWhiteSpace(recipients))
        {
            return;
        }

        foreach (var recipient in recipients.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            collection.Add(new MailAddress(recipient.Trim()));
        }
    }
}
