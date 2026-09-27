namespace Project1.Api.Entities;

public sealed class InventoryBalance
{
    public int Id { get; set; }

    public int ProductId { get; set; }

    public Product Product { get; set; } = null!;

    public decimal QuantityOnHand { get; set; }

    public DateTimeOffset LastUpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
