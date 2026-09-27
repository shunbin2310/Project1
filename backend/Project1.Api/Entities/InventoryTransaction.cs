namespace Project1.Api.Entities;

public sealed class InventoryTransaction
{
    public int Id { get; set; }

    public int ProductId { get; set; }

    public Product Product { get; set; } = null!;

    public InventoryTransactionType Type { get; set; }

    public decimal QuantityChange { get; set; }

    public decimal QuantityBefore { get; set; }

    public decimal QuantityAfter { get; set; }

    public string ProductCode { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public string UnitOfMeasureCode { get; set; } = string.Empty;

    public string ReferenceType { get; set; } = string.Empty;

    public int ReferenceId { get; set; }

    public string ReferenceNumber { get; set; } = string.Empty;

    public int? GoodsReceiptItemId { get; set; }

    public GoodsReceiptItem? GoodsReceiptItem { get; set; }

    public int PerformedByUserId { get; set; }

    public string PerformedByName { get; set; } = string.Empty;

    public DateOnly OccurredDate { get; set; }

    public DateTimeOffset OccurredAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
