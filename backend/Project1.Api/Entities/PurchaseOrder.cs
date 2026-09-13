namespace Project1.Api.Entities;

public sealed class PurchaseOrder
{
    public int Id { get; set; }

    public string PurchaseOrderNumber { get; set; } = string.Empty;

    public int QuotationId { get; set; }

    public Quotation Quotation { get; set; } = null!;

    public int PurchaseRequestId { get; set; }

    public PurchaseRequest PurchaseRequest { get; set; } = null!;

    public int SupplierId { get; set; }

    public Supplier Supplier { get; set; } = null!;

    public string QuotationNumber { get; set; } = string.Empty;

    public string PurchaseRequestNumber { get; set; } = string.Empty;

    public string SupplierCode { get; set; } = string.Empty;

    public string SupplierName { get; set; } = string.Empty;

    public string? SupplierQuotationReference { get; set; }

    public DateOnly OrderDate { get; set; }

    public DateOnly? ExpectedDeliveryDate { get; set; }

    public string? DeliveryAddress { get; set; }

    public string? Notes { get; set; }

    public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Draft;

    public int CreatedByUserId { get; set; }

    public string CreatedByName { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAtUtc { get; set; }

    public DateTimeOffset? IssuedAtUtc { get; set; }

    public int? IssuedByUserId { get; set; }

    public string? IssuedByName { get; set; }

    public DateTimeOffset? CancelledAtUtc { get; set; }

    public int? CancelledByUserId { get; set; }

    public string? CancelledByName { get; set; }

    public string? CancellationReason { get; set; }

    public ICollection<PurchaseOrderItem> Items { get; set; } = [];
}
