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
            From = new MailAddress(options.FromAddress, options.FromName),
            Subject = message.Subject,
            Body = message.HtmlBody,
            IsBodyHtml = true
        };
        mailMessage.To.Add(new MailAddress(message.RecipientEmail));

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
}
