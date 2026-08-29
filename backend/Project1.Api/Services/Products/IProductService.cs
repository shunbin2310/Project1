using Project1.Api.DTOs.Products;

namespace Project1.Api.Services.Products;

public interface IProductService
{
    Task<IReadOnlyList<ProductResponse>> GetAllAsync(
        bool includeInactive,
        CancellationToken cancellationToken);

    Task<ProductResponse?> GetByIdAsync(int id, CancellationToken cancellationToken);

    Task<ProductSaveResult> CreateAsync(
        CreateProductRequest request,
        CancellationToken cancellationToken);

    Task<ProductSaveResult> UpdateAsync(
        int id,
        UpdateProductRequest request,
        CancellationToken cancellationToken);

    Task<ProductSaveResult> DeactivateAsync(int id, CancellationToken cancellationToken);
}
