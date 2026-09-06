using Project1.Api.DTOs.Users;

namespace Project1.Api.Services.Users;

public interface IUserManagementService
{
    Task<IReadOnlyList<UserResponse>> GetAllAsync(
        bool includeInactive,
        string? search,
        CancellationToken cancellationToken);

    Task<UserResponse?> GetByIdAsync(int id, CancellationToken cancellationToken);

    Task<UserManagementResult> CreateAsync(
        CreateUserRequest request,
        CancellationToken cancellationToken);

    Task<UserManagementResult> UpdateAsync(
        int id,
        UpdateUserRequest request,
        CancellationToken cancellationToken);

    Task<UserManagementResult> SetActiveAsync(
        int id,
        bool isActive,
        CancellationToken cancellationToken);
}
