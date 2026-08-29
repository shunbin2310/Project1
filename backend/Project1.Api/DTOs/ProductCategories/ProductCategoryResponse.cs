namespace Project1.Api.DTOs.ProductCategories;

public sealed record ProductCategoryResponse(
    int Id,
    string Code,
    string Name,
    string? Description,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);
