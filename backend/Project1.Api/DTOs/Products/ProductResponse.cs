namespace Project1.Api.DTOs.Products;

public sealed record ProductResponse(
    int Id,
    string Code,
    string Name,
    string? Description,
    decimal DefaultUnitPrice,
    decimal ReorderLevel,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    string? StorageLocation);
