using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Project1.Api.Authentication;
using Project1.Api.Entities.Identity;
using Project1.Api.Services.Authentication;

namespace Project1.Api.Tests.Authentication;

public sealed class JwtTokenServiceTests
{
    [Fact]
    public void CreateToken_IncludesUserDepartmentAndRoles()
    {
        var service = new JwtTokenService(Options.Create(new JwtOptions
        {
            Issuer = "Project1.Api.Tests",
            Audience = "Project1.Frontend.Tests",
            SigningKey = "test-only-signing-key-that-is-at-least-32-characters-long",
            AccessTokenMinutes = 60
        }));
        var user = new ApplicationUser
        {
            Id = 42,
            Email = "requester@demo.local",
            FullName = "Demo Requester",
            DepartmentId = 7
        };

        var result = service.CreateToken(user, [ApplicationRoles.Requester]);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(result.AccessToken);

        Assert.Equal("42", token.Subject);
        Assert.Contains(token.Claims, claim =>
            claim.Type == ClaimTypes.Name && claim.Value == "Demo Requester");
        Assert.Contains(token.Claims, claim =>
            claim.Type == ApplicationClaimTypes.DepartmentId && claim.Value == "7");
        Assert.Contains(token.Claims, claim =>
            claim.Type == ClaimTypes.Role && claim.Value == ApplicationRoles.Requester);
        Assert.True(result.ExpiresAtUtc > DateTimeOffset.UtcNow.AddMinutes(55));
    }
}
