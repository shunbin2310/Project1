using System.ComponentModel.DataAnnotations;

namespace Project1.Api.DTOs.EmailTemplates;

public sealed record UpdateEmailTemplateRequest
{
    [Required]
    [StringLength(150, MinimumLength = 2)]
    public string Name { get; init; } = string.Empty;

    [Required]
    [StringLength(300, MinimumLength = 1)]
    public string SubjectTemplate { get; init; } = string.Empty;

    [Required]
    [StringLength(50000, MinimumLength = 1)]
    public string HtmlBodyTemplate { get; init; } = string.Empty;

    [StringLength(2000)]
    public string? CcRecipients { get; init; }

    [StringLength(2000)]
    public string? BccRecipients { get; init; }
}
