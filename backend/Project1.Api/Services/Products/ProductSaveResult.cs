using Project1.Api.DTOs.Products;

namespace Project1.Api.Services.Products;

public enum ProductSaveStatus
{
    Success,
    NotFound,
    ProductCategoryUnavailable,
    UnitOfMeasureUnavailable
}

public sealed record ProductSaveResult(
    ProductSaveStatus Status,
    ProductResponse? Product = null);
