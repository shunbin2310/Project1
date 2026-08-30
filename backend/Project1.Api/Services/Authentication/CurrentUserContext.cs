using System.Globalization;
using System.Security.Claims;
using Project1.Api.Authentication;

namespace Project1.Api.Services.Authentication;

public sealed class CurrentUserContext(IHttpContextAccessor httpContextAccessor)
    : ICurrentUserContext
{
    private ClaimsPrincipal User => httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal();

    public bool IsAuthenticated => User.Identity?.IsAuthenticated == true;

    public int UserId => int.TryParse(
        User.FindFirstValue(ClaimTypes.NameIdentifier),
        NumberStyles.None,
        CultureInfo.InvariantCulture,
        out var userId)
            ? userId
            : 0;

    public string DisplayName =>
        User.Identity?.Name ??
        User.FindFirstValue(ClaimTypes.Email) ??
        string.Empty;

    public int? DepartmentId => int.TryParse(
        User.FindFirstValue(ApplicationClaimTypes.DepartmentId),
        NumberStyles.None,
        CultureInfo.InvariantCulture,
        out var departmentId)
            ? departmentId
            : null;

    public IReadOnlyCollection<string> Roles => User
        .FindAll(ClaimTypes.Role)
        .Select(claim => claim.Value)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public bool IsInRole(string role) => User.IsInRole(role);
}
