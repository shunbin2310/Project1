using Project1.Api.DTOs.Users;

namespace Project1.Api.Services.Users;

public enum UserManagementStatus
{
    Success,
    NotFound,
    ValidationFailed,
    Conflict,
    Forbidden
}

public sealed record UserManagementResult(
    UserManagementStatus Status,
    UserResponse? User = null,
    string? ErrorMessage = null);
