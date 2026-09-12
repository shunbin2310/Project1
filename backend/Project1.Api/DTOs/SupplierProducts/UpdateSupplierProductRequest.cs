namespace Project1.Api.DTOs.SupplierProducts;

public sealed class UpdateSupplierProductRequest
{
    public bool IsPreferred { get; init; }

    public bool IsActive { get; init; }
}
