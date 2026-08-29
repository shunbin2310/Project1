using Project1.Api.DTOs.ProductCategories;

namespace Project1.Api.Services.ProductCategories;

public interface IProductCategoryService
{
    Task<IReadOnlyList<ProductCategoryResponse>> GetAllAsync(
        bool includeInactive,
        CancellationToken cancellationToken);

    Task<ProductCategoryResponse?> GetByIdAsync(int id, CancellationToken cancellationToken);

    Task<ProductCategorySaveResult> CreateAsync(
        CreateProductCategoryRequest request,
        CancellationToken cancellationToken);

    Task<ProductCategorySaveResult> UpdateAsync(
        int id,
        UpdateProductCategoryRequest request,
        CancellationToken cancellationToken);

    Task<ProductCategorySaveResult> DeactivateAsync(int id, CancellationToken cancellationToken);
}
