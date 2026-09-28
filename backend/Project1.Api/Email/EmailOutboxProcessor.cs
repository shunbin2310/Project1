using Microsoft.EntityFrameworkCore;
using Project1.Api.Data;
using Project1.Api.Entities;

namespace Project1.Api.Email;

public sealed class EmailOutboxProcessor(
    AppDbContext dbContext,
    IEmailSender emailSender,
    ILogger<EmailOutboxProcessor> logger) : IEmailOutboxProcessor
{
    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        var outbox = await dbContext.EmailOutboxes
            .OrderBy(item => item.Id)
            .FirstOrDefaultAsync(
                item => item.Status == EmailDeliveryStatus.Pending,
                cancellationToken);

        if (outbox is null)
        {
            return false;
        }

        var attemptedAt = DateTimeOffset.UtcNow;
        outbox.AttemptCount++;
        outbox.LastAttemptAtUtc = attemptedAt;
        outbox.UpdatedAtUtc = attemptedAt;

        try
        {
            await emailSender.SendAsync(
                new EmailMessage(
                    outbox.RecipientEmail,
                    outbox.Subject,
                    outbox.HtmlBody,
                    outbox.FromAddress,
                    outbox.FromName,
                    outbox.CcRecipients,
                    outbox.BccRecipients),
                cancellationToken);

            outbox.Status = EmailDeliveryStatus.Sent;
            outbox.SentAtUtc = DateTimeOffset.UtcNow;
            outbox.LastError = null;
            logger.LogInformation(
                "Email record {EmailOutboxId} was sent to {RecipientEmail}.",
                outbox.Id,
                outbox.RecipientEmail);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            outbox.Status = EmailDeliveryStatus.Failed;
            outbox.LastError = Truncate(exception.Message, 2000);
            logger.LogWarning(
                exception,
                "Email record {EmailOutboxId} failed for {RecipientEmail}.",
                outbox.Id,
                outbox.RecipientEmail);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];
}
