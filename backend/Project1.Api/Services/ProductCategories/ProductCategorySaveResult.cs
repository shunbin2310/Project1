using Project1.Api.DTOs.ProductCategories;

namespace Project1.Api.Services.ProductCategories;

public enum ProductCategorySaveStatus
{
    Success,
    NotFound,
    DuplicateName
}

public sealed record ProductCategorySaveResult(
    ProductCategorySaveStatus Status,
    ProductCategoryResponse? ProductCategory = null);
