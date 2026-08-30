using Project1.Api.DTOs.Authentication;

namespace Project1.Api.Services.Authentication;

public interface IAuthService
{
    Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken);

    Task<AuthenticatedUserResponse?> GetUserAsync(int userId, CancellationToken cancellationToken);
}
