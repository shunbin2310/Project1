using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Project1.Api.Authentication;
using Project1.Api.Services.Authentication;

namespace Project1.Api.Tests.Authentication;

public sealed class CurrentUserContextTests
{
    [Fact]
    public void ReadsAuthenticatedIdentityFromClaims()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "15"),
            new Claim(ClaimTypes.Name, "Department Approver"),
            new Claim(ApplicationClaimTypes.DepartmentId, "2"),
            new Claim(ClaimTypes.Role, ApplicationRoles.DepartmentApprover)
        ], "Test"));
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };

        var currentUser = new CurrentUserContext(accessor);

        Assert.True(currentUser.IsAuthenticated);
        Assert.Equal(15, currentUser.UserId);
        Assert.Equal("Department Approver", currentUser.DisplayName);
        Assert.Equal(2, currentUser.DepartmentId);
        Assert.True(currentUser.IsInRole(ApplicationRoles.DepartmentApprover));
    }
}
