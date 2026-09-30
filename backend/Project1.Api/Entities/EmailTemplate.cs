namespace Project1.Api.Entities;

public sealed class EmailTemplate
{
    public int Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public int Version { get; set; }

    public EmailTemplateStatus Status { get; set; } = EmailTemplateStatus.Draft;

    public string SubjectTemplate { get; set; } = string.Empty;

    public string HtmlBodyTemplate { get; set; } = string.Empty;

    public string ToRule { get; set; } = string.Empty;

    public string? CcRecipients { get; set; }

    public string? BccRecipients { get; set; }

    public int? CreatedByUserId { get; set; }

    public string? CreatedByName { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public int? PublishedByUserId { get; set; }

    public string? PublishedByName { get; set; }

    public DateTimeOffset? PublishedAtUtc { get; set; }

    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
