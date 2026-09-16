namespace Project1.Api.Entities;

public sealed class GoodsReceiptItem
{
    public int Id { get; set; }

    public int GoodsReceiptId { get; set; }

    public GoodsReceipt GoodsReceipt { get; set; } = null!;

    public int PurchaseOrderItemId { get; set; }

    public PurchaseOrderItem PurchaseOrderItem { get; set; } = null!;

    public int ProductId { get; set; }

    public Product Product { get; set; } = null!;

    public string ProductCode { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public string UnitOfMeasureCode { get; set; } = string.Empty;

    public decimal OrderedQuantity { get; set; }

    public decimal QuantityReceived { get; set; }
}
