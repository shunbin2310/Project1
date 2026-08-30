namespace Project1.Api.DTOs.Authentication;

public sealed record AuthenticatedUserResponse(
    int Id,
    string Email,
    string FullName,
    int? DepartmentId,
    string? DepartmentCode,
    string? DepartmentName,
    IReadOnlyList<string> Roles);
