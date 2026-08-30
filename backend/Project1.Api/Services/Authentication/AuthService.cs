using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Project1.Api.Data;
using Project1.Api.DTOs.Authentication;
using Project1.Api.Entities.Identity;

namespace Project1.Api.Services.Authentication;

public sealed class AuthService(
    AppDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IJwtTokenService jwtTokenService) : IAuthService
{
    public async Task<LoginResponse?> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim();
        var user = await userManager.FindByEmailAsync(normalizedEmail);

        if (user is null || !user.IsActive)
        {
            return null;
        }

        var signInResult = await signInManager.CheckPasswordSignInAsync(
            user,
            request.Password,
            lockoutOnFailure: true);

        if (!signInResult.Succeeded)
        {
            return null;
        }

        var roles = await userManager.GetRolesAsync(user);
        var token = jwtTokenService.CreateToken(user, roles.ToArray());
        var responseUser = await GetUserAsync(user.Id, cancellationToken);

        return responseUser is null
            ? null
            : new LoginResponse(token.AccessToken, token.ExpiresAtUtc, responseUser);
    }

    public async Task<AuthenticatedUserResponse?> GetUserAsync(
        int userId,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Users
            .AsNoTracking()
            .Include(item => item.Department)
            .SingleOrDefaultAsync(item => item.Id == userId && item.IsActive, cancellationToken);

        if (user is null)
        {
            return null;
        }

        var roles = await userManager.GetRolesAsync(user);

        return new AuthenticatedUserResponse(
            user.Id,
            user.Email ?? string.Empty,
            user.FullName,
            user.DepartmentId,
            user.Department?.Code,
            user.Department?.Name,
            roles.OrderBy(role => role).ToList());
    }
}
