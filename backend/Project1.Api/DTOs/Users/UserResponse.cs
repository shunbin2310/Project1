namespace Project1.Api.DTOs.Users;

public sealed record UserResponse(
    int Id,
    string Email,
    string FullName,
    int? DepartmentId,
    string? DepartmentCode,
    string? DepartmentName,
    bool IsActive,
    IReadOnlyList<string> Roles,
    DateTimeOffset CreatedAtUtc);
