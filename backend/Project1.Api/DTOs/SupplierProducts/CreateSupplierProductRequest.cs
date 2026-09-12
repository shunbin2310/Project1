using System.ComponentModel.DataAnnotations;

namespace Project1.Api.DTOs.SupplierProducts;

public sealed class CreateSupplierProductRequest
{
    [Range(1, int.MaxValue)]
    public int SupplierId { get; init; }

    [Range(1, int.MaxValue)]
    public int ProductId { get; init; }

    public bool IsPreferred { get; init; }
}
