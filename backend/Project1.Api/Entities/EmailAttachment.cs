namespace Project1.Api.Entities;

public sealed class EmailAttachment
{
    public int Id { get; set; }

    public int EmailOutboxId { get; set; }

    public EmailOutbox EmailOutbox { get; set; } = null!;

    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }

    public byte[] Content { get; set; } = [];

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
