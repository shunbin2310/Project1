using Project1.Api.DTOs.SupplierProducts;

namespace Project1.Api.Services.SupplierProducts;

public interface ISupplierProductService
{
    Task<IReadOnlyList<SupplierProductResponse>> GetAllAsync(
        int? supplierId,
        int? productId,
        bool includeInactive,
        CancellationToken cancellationToken);

    Task<SupplierProductResponse?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken);

    Task<SupplierProductSaveResult> CreateAsync(
        CreateSupplierProductRequest request,
        CancellationToken cancellationToken);

    Task<SupplierProductSaveResult> UpdateAsync(
        int id,
        UpdateSupplierProductRequest request,
        CancellationToken cancellationToken);

    Task<SupplierProductSaveResult> DeleteAsync(
        int id,
        CancellationToken cancellationToken);
}
