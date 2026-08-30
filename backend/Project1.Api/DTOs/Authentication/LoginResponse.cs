namespace Project1.Api.DTOs.Authentication;

public sealed record LoginResponse(
    string AccessToken,
    DateTimeOffset ExpiresAtUtc,
    AuthenticatedUserResponse User);
