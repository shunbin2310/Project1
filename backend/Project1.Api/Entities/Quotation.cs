namespace Project1.Api.Entities;

public sealed class Quotation
{
    public int Id { get; set; }

    public string QuotationNumber { get; set; } = string.Empty;

    public int PurchaseRequestId { get; set; }

    public PurchaseRequest PurchaseRequest { get; set; } = null!;

    public int SupplierId { get; set; }

    public Supplier Supplier { get; set; } = null!;

    public string SupplierCode { get; set; } = string.Empty;

    public string SupplierName { get; set; } = string.Empty;

    public string? SupplierQuotationReference { get; set; }

    public DateOnly QuotationDate { get; set; }

    public DateOnly? ValidUntil { get; set; }

    public string? Notes { get; set; }

    public QuotationStatus Status { get; set; } = QuotationStatus.Draft;

    public int CreatedByUserId { get; set; }

    public string CreatedByName { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAtUtc { get; set; }

    public DateTimeOffset? SubmittedAtUtc { get; set; }

    public DateTimeOffset? SelectedAtUtc { get; set; }

    public ICollection<QuotationItem> Items { get; set; } = [];
}
