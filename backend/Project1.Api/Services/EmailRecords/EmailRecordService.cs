using Microsoft.EntityFrameworkCore;
using Project1.Api.Data;
using Project1.Api.DTOs.EmailRecords;
using Project1.Api.Entities;
using Project1.Api.Services.Authentication;

namespace Project1.Api.Services.EmailRecords;

public sealed class EmailRecordService(
    AppDbContext dbContext,
    ICurrentUserContext currentUser) : IEmailRecordService
{
    public async Task<IReadOnlyList<EmailRecordSummaryResponse>> GetAllAsync(
        string? search,
        EmailDeliveryStatus? status,
        string? sourceType,
        DateOnly? createdFrom,
        DateOnly? createdTo,
        CancellationToken cancellationToken)
    {
        var query = dbContext.EmailOutboxes.AsNoTracking();
        var normalizedSearch = NormalizeOptionalText(search);
        if (normalizedSearch is not null)
        {
            query = query.Where(email =>
                email.SourceReference.Contains(normalizedSearch) ||
                email.RecipientEmail.Contains(normalizedSearch) ||
                email.Subject.Contains(normalizedSearch));
        }

        if (status.HasValue)
        {
            query = query.Where(email => email.Status == status.Value);
        }

        var normalizedSourceType = NormalizeOptionalText(sourceType);
        if (normalizedSourceType is not null)
        {
            query = query.Where(email => email.SourceType == normalizedSourceType);
        }

        if (createdFrom.HasValue)
        {
            query = query.Where(email => email.CreatedDate >= createdFrom.Value);
        }

        if (createdTo.HasValue)
        {
            query = query.Where(email => email.CreatedDate <= createdTo.Value);
        }

        return await query
            .OrderByDescending(email => email.Id)
            .Select(email => ToSummary(email))
            .ToListAsync(cancellationToken);
    }

    public async Task<EmailRecordDetailsResponse?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var email = await dbContext.EmailOutboxes
            .AsNoTracking()
            .Include(item => item.Attachments)
            .Where(item => item.Id == id)
            .SingleOrDefaultAsync(cancellationToken);

        return email is null ? null : ToDetails(email);
    }

    public async Task<EmailAttachmentFileResult?> GetAttachmentAsync(
        int emailRecordId,
        int attachmentId,
        CancellationToken cancellationToken) =>
        await dbContext.EmailAttachments
            .AsNoTracking()
            .Where(attachment =>
                attachment.EmailOutboxId == emailRecordId &&
                attachment.Id == attachmentId)
            .Select(attachment => new EmailAttachmentFileResult(
                attachment.FileName,
                attachment.ContentType,
                attachment.Content))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<EmailRecordOperationResult> RetryAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var email = await dbContext.EmailOutboxes
            .Include(item => item.Attachments)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (email is null)
        {
            return NotFound();
        }

        if (email.Status != EmailDeliveryStatus.Failed)
        {
            return InvalidState("Only a failed email can be retried.");
        }

        email.Status = EmailDeliveryStatus.Pending;
        email.LastError = null;
        email.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return new EmailRecordOperationResult(
            EmailRecordOperationStatus.Success,
            ToDetails(email));
    }

    public async Task<EmailRecordOperationResult> ResendAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var source = await dbContext.EmailOutboxes
            .Include(item => item.Attachments)
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (source is null)
        {
            return NotFound();
        }

        if (source.Status != EmailDeliveryStatus.Sent)
        {
            return InvalidState("Only a sent email can be resent. Retry a failed email instead.");
        }

        var resent = new EmailOutbox
        {
            PurchaseOrderId = source.PurchaseOrderId,
            SourceType = source.SourceType,
            SourceId = source.SourceId,
            SourceReference = source.SourceReference,
            FromAddress = source.FromAddress,
            FromName = source.FromName,
            RecipientEmail = source.RecipientEmail,
            CcRecipients = source.CcRecipients,
            BccRecipients = source.BccRecipients,
            Subject = source.Subject,
            HtmlBody = source.HtmlBody,
            TemplateCode = source.TemplateCode,
            TemplateVersion = source.TemplateVersion,
            Status = EmailDeliveryStatus.Pending,
            CreatedByUserId = currentUser.UserId,
            CreatedByName = CurrentUserName(),
            ResentFromEmailOutboxId = source.Id,
            CreatedDate = DateOnly.FromDateTime(DateTime.UtcNow),
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        foreach (var attachment in source.Attachments.OrderBy(attachment => attachment.Id))
        {
            resent.Attachments.Add(new EmailAttachment
            {
                FileName = attachment.FileName,
                ContentType = attachment.ContentType,
                FileSizeBytes = attachment.FileSizeBytes,
                Content = attachment.Content.ToArray(),
                CreatedAtUtc = resent.CreatedAtUtc
            });
        }

        dbContext.EmailOutboxes.Add(resent);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new EmailRecordOperationResult(
            EmailRecordOperationStatus.Success,
            ToDetails(resent));
    }

    private static EmailRecordSummaryResponse ToSummary(EmailOutbox email) => new(
        email.Id,
        email.SourceType,
        email.SourceId,
        email.SourceReference,
        email.FromAddress,
        email.FromName,
        email.RecipientEmail,
        email.CcRecipients,
        email.BccRecipients,
        email.Subject,
        email.TemplateCode,
        email.TemplateVersion,
        email.Status,
        email.AttemptCount,
        email.CreatedByUserId,
        email.CreatedByName,
        email.ResentFromEmailOutboxId,
        email.CreatedAtUtc,
        email.UpdatedAtUtc,
        email.LastAttemptAtUtc,
        email.SentAtUtc,
        email.LastError);

    private static EmailRecordDetailsResponse ToDetails(EmailOutbox email) => new(
        email.Id,
        email.SourceType,
        email.SourceId,
        email.SourceReference,
        email.FromAddress,
        email.FromName,
        email.RecipientEmail,
        email.CcRecipients,
        email.BccRecipients,
        email.Subject,
        email.HtmlBody,
        email.TemplateCode,
        email.TemplateVersion,
        email.Status,
        email.AttemptCount,
        email.CreatedByUserId,
        email.CreatedByName,
        email.ResentFromEmailOutboxId,
        email.CreatedAtUtc,
        email.UpdatedAtUtc,
        email.LastAttemptAtUtc,
        email.SentAtUtc,
        email.LastError,
        email.Attachments
            .OrderBy(attachment => attachment.Id)
            .Select(attachment => new EmailAttachmentResponse(
                attachment.Id,
                attachment.FileName,
                attachment.ContentType,
                attachment.FileSizeBytes,
                attachment.CreatedAtUtc))
            .ToList());

    private string CurrentUserName() =>
        string.IsNullOrWhiteSpace(currentUser.DisplayName)
            ? "Unknown user"
            : currentUser.DisplayName.Trim();

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static EmailRecordOperationResult NotFound() =>
        new(EmailRecordOperationStatus.NotFound, ErrorMessage: "Email record was not found.");

    private static EmailRecordOperationResult InvalidState(string message) =>
        new(EmailRecordOperationStatus.InvalidState, ErrorMessage: message);
}
