using System.Data;
using Microsoft.EntityFrameworkCore;
using Project1.Api.Data;
using Project1.Api.DTOs.EmailTemplates;
using Project1.Api.Email;
using Project1.Api.Entities;
using Project1.Api.Services.Authentication;

namespace Project1.Api.Services.EmailTemplates;

public sealed class EmailTemplateService(
    AppDbContext dbContext,
    ICurrentUserContext currentUser,
    IEmailTemplateRenderer renderer) : IEmailTemplateService
{
    public async Task<IReadOnlyList<EmailTemplateSummaryResponse>> GetAllAsync(
        CancellationToken cancellationToken)
    {
        var templates = await dbContext.EmailTemplates
            .AsNoTracking()
            .Where(template => template.Code == EmailTemplateConstants.PurchaseOrderIssuedCode)
            .OrderByDescending(template => template.Version)
            .ToListAsync(cancellationToken);

        return templates.Select(ToSummary).ToList();
    }

    public async Task<EmailTemplateResponse?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var template = await dbContext.EmailTemplates
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        return template is null ? null : ToResponse(template);
    }

    public async Task<EmailTemplateOperationResult> CreateVersionAsync(
        int sourceTemplateId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var source = await dbContext.EmailTemplates
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == sourceTemplateId, cancellationToken);
        if (source is null)
        {
            return NotFound();
        }

        if (source.Status != EmailTemplateStatus.Active)
        {
            return InvalidState("Only the active email template can be copied into a new version.");
        }

        var draftExists = await dbContext.EmailTemplates.AnyAsync(
            item => item.Code == source.Code && item.Status == EmailTemplateStatus.Draft,
            cancellationToken);
        if (draftExists)
        {
            return Conflict("A draft version already exists. Edit or delete it before creating another version.");
        }

        var nextVersion = await dbContext.EmailTemplates
            .Where(item => item.Code == source.Code)
            .MaxAsync(item => item.Version, cancellationToken) + 1;
        var draft = new EmailTemplate
        {
            Code = source.Code,
            Name = source.Name,
            Version = nextVersion,
            Status = EmailTemplateStatus.Draft,
            SubjectTemplate = source.SubjectTemplate,
            HtmlBodyTemplate = source.HtmlBodyTemplate,
            ToRule = source.ToRule,
            CcRecipients = source.CcRecipients,
            BccRecipients = source.BccRecipients,
            CreatedByUserId = currentUser.UserId,
            CreatedByName = CurrentUserName()
        };

        dbContext.EmailTemplates.Add(draft);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Success(ToResponse(draft));
    }

    public async Task<EmailTemplateOperationResult> UpdateAsync(
        int id,
        UpdateEmailTemplateRequest request,
        CancellationToken cancellationToken)
    {
        var template = await dbContext.EmailTemplates
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (template is null)
        {
            return NotFound();
        }

        if (template.Status != EmailTemplateStatus.Draft)
        {
            return InvalidState("Only draft email templates can be edited.");
        }

        var name = request.Name.Trim();
        if (name.Length is < 2 or > 150)
        {
            return ValidationFailed("Template name must contain between 2 and 150 characters.");
        }

        var validationError = renderer.Validate(
            request.SubjectTemplate,
            request.HtmlBodyTemplate,
            request.CcRecipients,
            request.BccRecipients);
        if (validationError is not null)
        {
            return ValidationFailed(validationError);
        }

        template.Name = name;
        template.SubjectTemplate = request.SubjectTemplate.Trim();
        template.HtmlBodyTemplate = request.HtmlBodyTemplate.Trim();
        template.CcRecipients = NormalizeOptional(request.CcRecipients);
        template.BccRecipients = NormalizeOptional(request.BccRecipients);
        template.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        return Success(ToResponse(template));
    }

    public Task<EmailTemplateOperationResult> PreviewAsync(
        PreviewEmailTemplateRequest request,
        CancellationToken cancellationToken)
    {
        var validationError = renderer.Validate(
            request.SubjectTemplate,
            request.HtmlBodyTemplate,
            request.CcRecipients,
            request.BccRecipients);
        if (validationError is not null)
        {
            return Task.FromResult(ValidationFailed(validationError));
        }

        var preview = renderer.RenderPreview(
            request.SubjectTemplate,
            request.HtmlBodyTemplate,
            request.CcRecipients,
            request.BccRecipients);
        return Task.FromResult(new EmailTemplateOperationResult(
            EmailTemplateOperationStatus.Success,
            Preview: preview));
    }

    public async Task<EmailTemplateOperationResult> PublishAsync(
        int id,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var template = await dbContext.EmailTemplates
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (template is null)
        {
            return NotFound();
        }

        if (template.Status != EmailTemplateStatus.Draft)
        {
            return InvalidState("Only a draft email template can be published.");
        }

        var validationError = renderer.Validate(
            template.SubjectTemplate,
            template.HtmlBodyTemplate,
            template.CcRecipients,
            template.BccRecipients);
        if (validationError is not null)
        {
            return ValidationFailed(validationError);
        }

        var activeTemplates = await dbContext.EmailTemplates
            .Where(item => item.Code == template.Code && item.Status == EmailTemplateStatus.Active)
            .ToListAsync(cancellationToken);
        foreach (var activeTemplate in activeTemplates)
        {
            activeTemplate.Status = EmailTemplateStatus.Superseded;
            activeTemplate.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        var now = DateTimeOffset.UtcNow;
        template.Status = EmailTemplateStatus.Active;
        template.PublishedByUserId = currentUser.UserId;
        template.PublishedByName = CurrentUserName();
        template.PublishedAtUtc = now;
        template.UpdatedAtUtc = now;

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Success(ToResponse(template));
    }

    public async Task<EmailTemplateOperationResult> DeleteAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var template = await dbContext.EmailTemplates
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (template is null)
        {
            return NotFound();
        }

        if (template.Status != EmailTemplateStatus.Draft)
        {
            return InvalidState("Only draft email templates can be deleted.");
        }

        dbContext.EmailTemplates.Remove(template);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new EmailTemplateOperationResult(EmailTemplateOperationStatus.Success);
    }

    private static EmailTemplateSummaryResponse ToSummary(EmailTemplate template) => new(
        template.Id,
        template.Code,
        template.Name,
        template.Version,
        template.Status,
        template.ToRule,
        template.CreatedAtUtc,
        template.PublishedAtUtc,
        template.UpdatedAtUtc);

    private static EmailTemplateResponse ToResponse(EmailTemplate template) => new(
        template.Id,
        template.Code,
        template.Name,
        template.Version,
        template.Status,
        template.SubjectTemplate,
        template.HtmlBodyTemplate,
        template.ToRule,
        template.CcRecipients,
        template.BccRecipients,
        template.CreatedByUserId,
        template.CreatedByName,
        template.CreatedAtUtc,
        template.PublishedByUserId,
        template.PublishedByName,
        template.PublishedAtUtc,
        template.UpdatedAtUtc,
        EmailTemplateConstants.PurchaseOrderPlaceholders);

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private string CurrentUserName() =>
        string.IsNullOrWhiteSpace(currentUser.DisplayName) ? "Unknown user" : currentUser.DisplayName.Trim();

    private static EmailTemplateOperationResult Success(EmailTemplateResponse template) =>
        new(EmailTemplateOperationStatus.Success, template);

    private static EmailTemplateOperationResult NotFound() =>
        new(EmailTemplateOperationStatus.NotFound);

    private static EmailTemplateOperationResult ValidationFailed(string message) =>
        new(EmailTemplateOperationStatus.ValidationFailed, ErrorMessage: message);

    private static EmailTemplateOperationResult Conflict(string message) =>
        new(EmailTemplateOperationStatus.Conflict, ErrorMessage: message);

    private static EmailTemplateOperationResult InvalidState(string message) =>
        new(EmailTemplateOperationStatus.InvalidState, ErrorMessage: message);
}
