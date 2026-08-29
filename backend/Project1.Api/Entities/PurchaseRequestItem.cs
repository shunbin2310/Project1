namespace Project1.Api.Entities;

public sealed class PurchaseRequestItem
{
    public int Id { get; set; }

    public int PurchaseRequestId { get; set; }

    public PurchaseRequest PurchaseRequest { get; set; } = null!;

    public int ProductId { get; set; }

    public Product Product { get; set; } = null!;

    public decimal Quantity { get; set; }

    public decimal EstimatedUnitPrice { get; set; }
}
