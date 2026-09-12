namespace Project1.Api.Entities;

public sealed class SupplierProduct
{
    public int Id { get; set; }

    public int SupplierId { get; set; }

    public Supplier Supplier { get; set; } = null!;

    public int ProductId { get; set; }

    public Product Product { get; set; } = null!;

    public bool IsPreferred { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
