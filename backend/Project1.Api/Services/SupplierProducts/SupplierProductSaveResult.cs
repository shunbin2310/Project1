using Project1.Api.DTOs.SupplierProducts;

namespace Project1.Api.Services.SupplierProducts;

public enum SupplierProductSaveStatus
{
    Success,
    NotFound,
    SupplierUnavailable,
    ProductUnavailable,
    DuplicateRelationship
}

public sealed record SupplierProductSaveResult(
    SupplierProductSaveStatus Status,
    SupplierProductResponse? SupplierProduct = null);
