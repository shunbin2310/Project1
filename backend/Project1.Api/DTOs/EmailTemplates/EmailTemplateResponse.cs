using Project1.Api.Entities;

namespace Project1.Api.DTOs.EmailTemplates;

public sealed record EmailTemplateResponse(
    int Id,
    string Code,
    string Name,
    int Version,
    EmailTemplateStatus Status,
    string SubjectTemplate,
    string HtmlBodyTemplate,
    string ToRule,
    string? CcRecipients,
    string? BccRecipients,
    int? CreatedByUserId,
    string? CreatedByName,
    DateTimeOffset CreatedAtUtc,
    int? PublishedByUserId,
    string? PublishedByName,
    DateTimeOffset? PublishedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    IReadOnlyList<string> SupportedPlaceholders);
