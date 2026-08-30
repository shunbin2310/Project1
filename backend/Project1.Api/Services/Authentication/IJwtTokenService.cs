using Project1.Api.Entities.Identity;

namespace Project1.Api.Services.Authentication;

public interface IJwtTokenService
{
    JwtTokenResult CreateToken(
        ApplicationUser user,
        IReadOnlyCollection<string> roles);
}

public sealed record JwtTokenResult(string AccessToken, DateTimeOffset ExpiresAtUtc);
