namespace Project1.Api.Entities;

public sealed class PurchaseOrderItem
{
    public int Id { get; set; }

    public int PurchaseOrderId { get; set; }

    public PurchaseOrder PurchaseOrder { get; set; } = null!;

    public int QuotationItemId { get; set; }

    public QuotationItem QuotationItem { get; set; } = null!;

    public int ProductId { get; set; }

    public Product Product { get; set; } = null!;

    public string ProductCode { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public string UnitOfMeasureCode { get; set; } = string.Empty;

    public decimal Quantity { get; set; }

    public decimal UnitPrice { get; set; }
}
