namespace Project1.Api.Entities;

public sealed class QuotationItem
{
    public int Id { get; set; }

    public int QuotationId { get; set; }

    public Quotation Quotation { get; set; } = null!;

    public int PurchaseRequestItemId { get; set; }

    public PurchaseRequestItem PurchaseRequestItem { get; set; } = null!;

    public int ProductId { get; set; }

    public Product Product { get; set; } = null!;

    public string ProductCode { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public string UnitOfMeasureCode { get; set; } = string.Empty;

    public decimal Quantity { get; set; }

    public decimal UnitPrice { get; set; }
}
