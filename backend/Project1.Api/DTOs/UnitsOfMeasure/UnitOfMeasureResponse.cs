namespace Project1.Api.DTOs.UnitsOfMeasure;

public sealed record UnitOfMeasureResponse(
    int Id,
    string Code,
    string Name,
    string? Description,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);
