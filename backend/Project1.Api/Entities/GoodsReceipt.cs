namespace Project1.Api.Entities;

public sealed class GoodsReceipt
{
    public int Id { get; set; }

    public string GoodsReceiptNumber { get; set; } = string.Empty;

    public int PurchaseOrderId { get; set; }

    public PurchaseOrder PurchaseOrder { get; set; } = null!;

    public int SupplierId { get; set; }

    public Supplier Supplier { get; set; } = null!;

    public string PurchaseOrderNumber { get; set; } = string.Empty;

    public string SupplierCode { get; set; } = string.Empty;

    public string SupplierName { get; set; } = string.Empty;

    public string? SupplierDeliveryNoteNumber { get; set; }

    public DateOnly ReceivedDate { get; set; }

    public string? Notes { get; set; }

    public GoodsReceiptStatus Status { get; set; } = GoodsReceiptStatus.Draft;

    public int CreatedByUserId { get; set; }

    public string CreatedByName { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAtUtc { get; set; }

    public DateTimeOffset? PostedAtUtc { get; set; }

    public int? PostedByUserId { get; set; }

    public string? PostedByName { get; set; }

    public ICollection<GoodsReceiptItem> Items { get; set; } = [];
}
