using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Project1.Api.Authentication;
using Project1.Api.Data;
using Project1.Api.DTOs.Authentication;
using Project1.Api.Entities;
using Project1.Api.Entities.Identity;
using Project1.Api.Services.Authentication;

namespace Project1.Api.Tests.Authentication;

public sealed class AuthServiceTests
{
    [Fact]
    public async Task LoginAsync_VerifiesIdentityPasswordAndReturnsUserRoles()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthentication();
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(connection));
        services.AddHttpContextAccessor();
        services
            .AddIdentityCore<ApplicationUser>(options => options.User.RequireUniqueEmail = true)
            .AddRoles<IdentityRole<int>>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddSignInManager();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.EnsureCreatedAsync();

        var department = new Department { Code = "IT", Name = "Information Technology" };
        dbContext.Departments.Add(department);
        await dbContext.SaveChangesAsync();

        var roleManager = scope.ServiceProvider
            .GetRequiredService<RoleManager<IdentityRole<int>>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var signInManager = scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();
        await roleManager.CreateAsync(new IdentityRole<int>(ApplicationRoles.Requester));

        var user = new ApplicationUser
        {
            UserName = "requester@demo.local",
            Email = "requester@demo.local",
            EmailConfirmed = true,
            FullName = "Demo Requester",
            DepartmentId = department.Id
        };
        var createResult = await userManager.CreateAsync(user, "Project1Demo123!");
        Assert.True(createResult.Succeeded);
        await userManager.AddToRoleAsync(user, ApplicationRoles.Requester);

        var authService = new AuthService(
            dbContext,
            userManager,
            signInManager,
            new StubJwtTokenService());

        var success = await authService.LoginAsync(
            new LoginRequest
            {
                Email = "requester@demo.local",
                Password = "Project1Demo123!"
            },
            CancellationToken.None);
        var rejected = await authService.LoginAsync(
            new LoginRequest
            {
                Email = "requester@demo.local",
                Password = "WrongPassword123!"
            },
            CancellationToken.None);

        Assert.NotNull(success);
        Assert.Equal("test-token", success.AccessToken);
        Assert.Equal("IT", success.User.DepartmentCode);
        Assert.Contains(ApplicationRoles.Requester, success.User.Roles);
        Assert.Null(rejected);
    }

    private sealed class StubJwtTokenService : IJwtTokenService
    {
        public JwtTokenResult CreateToken(
            ApplicationUser user,
            IReadOnlyCollection<string> roles) =>
            new("test-token", DateTimeOffset.UtcNow.AddHours(1));
    }
}
