namespace Project1.Api.DTOs.Products;

public sealed record ProductResponse(
    int Id,
    string Code,
    string Name,
    string? Description,
    int ProductCategoryId,
    string ProductCategoryCode,
    string ProductCategoryName,
    int UnitOfMeasureId,
    string UnitOfMeasureCode,
    string UnitOfMeasureName,
    decimal DefaultUnitPrice,
    decimal ReorderLevel,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);
