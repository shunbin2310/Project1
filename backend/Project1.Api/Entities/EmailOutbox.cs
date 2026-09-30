namespace Project1.Api.Entities;

public sealed class EmailOutbox
{
    public int Id { get; set; }

    public int PurchaseOrderId { get; set; }

    public PurchaseOrder PurchaseOrder { get; set; } = null!;

    public string SourceType { get; set; } = "PurchaseOrder";

    public int SourceId { get; set; }

    public string SourceReference { get; set; } = string.Empty;

    public string FromAddress { get; set; } = string.Empty;

    public string FromName { get; set; } = string.Empty;

    public string RecipientEmail { get; set; } = string.Empty;

    public string? CcRecipients { get; set; }

    public string? BccRecipients { get; set; }

    public string Subject { get; set; } = string.Empty;

    public string HtmlBody { get; set; } = string.Empty;

    public string? TemplateCode { get; set; }

    public int? TemplateVersion { get; set; }

    public EmailDeliveryStatus Status { get; set; } = EmailDeliveryStatus.Pending;

    public int AttemptCount { get; set; }

    public DateTimeOffset? LastAttemptAtUtc { get; set; }

    public DateTimeOffset? SentAtUtc { get; set; }

    public string? LastError { get; set; }

    public int? CreatedByUserId { get; set; }

    public string? CreatedByName { get; set; }

    public int? ResentFromEmailOutboxId { get; set; }

    public DateOnly CreatedDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAtUtc { get; set; }

    public ICollection<EmailAttachment> Attachments { get; set; } = [];
}
