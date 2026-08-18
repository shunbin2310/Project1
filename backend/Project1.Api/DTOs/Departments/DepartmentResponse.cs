namespace Project1.Api.DTOs.Departments;

public sealed record DepartmentResponse(
    int Id,
    string Code,
    string Name,
    string? Description,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);
