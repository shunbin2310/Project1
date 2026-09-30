using Project1.Api.Entities;

namespace Project1.Api.DTOs.EmailTemplates;

public sealed record EmailTemplateSummaryResponse(
    int Id,
    string Code,
    string Name,
    int Version,
    EmailTemplateStatus Status,
    string ToRule,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? PublishedAtUtc,
    DateTimeOffset? UpdatedAtUtc);
