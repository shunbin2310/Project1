using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Project1.Api.Authentication;
using Project1.Api.Data;
using Project1.Api.DTOs.Users;
using Project1.Api.Entities;
using Project1.Api.Entities.Identity;
using Project1.Api.Services.Authentication;
using Project1.Api.Services.Users;

namespace Project1.Api.Tests.Services;

public sealed class UserManagementServiceTests
{
    [Fact]
    public async Task CreateAsync_CreatesUserWithConfiguredDefaultPasswordAndMultipleRoles()
    {
        await using var fixture = await UserManagementFixture.CreateAsync();

        var result = await fixture.Service.CreateAsync(new CreateUserRequest
        {
            Email = "new.user@demo.local",
            FullName = "New User",
            DepartmentId = fixture.DepartmentId,
            Roles = [ApplicationRoles.Requester, ApplicationRoles.DepartmentApprover]
        }, CancellationToken.None);

        Assert.Equal(UserManagementStatus.Success, result.Status);
        Assert.Equal("IT", result.User!.DepartmentCode);
        Assert.Equal(2, result.User.Roles.Count);

        var createdUser = await fixture.UserManager.FindByEmailAsync("new.user@demo.local");
        Assert.NotNull(createdUser);
        Assert.True(await fixture.UserManager.CheckPasswordAsync(
            createdUser,
            "Project1Demo123!"));
    }

    [Fact]
    public async Task UpdateAsync_RejectsRemovingCurrentUsersAdminRole()
    {
        await using var fixture = await UserManagementFixture.CreateAsync();
        var administrator = await fixture.CreateUserAsync(
            "admin@demo.local",
            [ApplicationRoles.Admin]);
        fixture.CurrentUser.UserId = administrator.Id;

        var result = await fixture.Service.UpdateAsync(administrator.Id, new UpdateUserRequest
        {
            FullName = "Demo Admin",
            DepartmentId = fixture.DepartmentId,
            Roles = [ApplicationRoles.Requester]
        }, CancellationToken.None);

        Assert.Equal(UserManagementStatus.Forbidden, result.Status);
        Assert.True(await fixture.UserManager.IsInRoleAsync(
            administrator,
            ApplicationRoles.Admin));
    }

    [Fact]
    public async Task SetActiveAsync_RejectsDeactivatingCurrentUser()
    {
        await using var fixture = await UserManagementFixture.CreateAsync();
        var administrator = await fixture.CreateUserAsync(
            "admin@demo.local",
            [ApplicationRoles.Admin]);
        fixture.CurrentUser.UserId = administrator.Id;

        var result = await fixture.Service.SetActiveAsync(
            administrator.Id,
            false,
            CancellationToken.None);

        Assert.Equal(UserManagementStatus.Forbidden, result.Status);
        Assert.True(administrator.IsActive);
    }

    [Fact]
    public async Task SetActiveAsync_DeactivatesAnotherUserAndChangesSecurityStamp()
    {
        await using var fixture = await UserManagementFixture.CreateAsync();
        var user = await fixture.CreateUserAsync(
            "requester@demo.local",
            [ApplicationRoles.Requester]);
        var originalSecurityStamp = user.SecurityStamp;

        var result = await fixture.Service.SetActiveAsync(
            user.Id,
            false,
            CancellationToken.None);

        Assert.Equal(UserManagementStatus.Success, result.Status);
        Assert.False(result.User!.IsActive);
        Assert.NotEqual(originalSecurityStamp, user.SecurityStamp);
    }

    [Fact]
    public async Task SetActiveAsync_RejectsDeactivatingLastActiveAdministrator()
    {
        await using var fixture = await UserManagementFixture.CreateAsync();
        var administrator = await fixture.CreateUserAsync(
            "last.admin@demo.local",
            [ApplicationRoles.Admin]);

        var result = await fixture.Service.SetActiveAsync(
            administrator.Id,
            false,
            CancellationToken.None);

        Assert.Equal(UserManagementStatus.Conflict, result.Status);
        Assert.True(administrator.IsActive);
    }

    [Fact]
    public async Task CreateAsync_RejectsUnknownRole()
    {
        await using var fixture = await UserManagementFixture.CreateAsync();

        var result = await fixture.Service.CreateAsync(new CreateUserRequest
        {
            Email = "unknown.role@demo.local",
            FullName = "Unknown Role",
            DepartmentId = fixture.DepartmentId,
            Roles = ["UNKNOWN_ROLE"]
        }, CancellationToken.None);

        Assert.Equal(UserManagementStatus.ValidationFailed, result.Status);
        Assert.Contains("Unknown roles", result.ErrorMessage);
        Assert.Null(await fixture.UserManager.FindByEmailAsync("unknown.role@demo.local"));
    }

    [Fact]
    public async Task CreateAsync_RejectsCombiningAdminWithOtherRoles()
    {
        await using var fixture = await UserManagementFixture.CreateAsync();

        var result = await fixture.Service.CreateAsync(new CreateUserRequest
        {
            Email = "admin.two@demo.local",
            FullName = "Second Admin",
            DepartmentId = fixture.DepartmentId,
            Roles = [ApplicationRoles.Admin, ApplicationRoles.Requester]
        }, CancellationToken.None);

        Assert.Equal(UserManagementStatus.ValidationFailed, result.Status);
        Assert.Contains("cannot be combined", result.ErrorMessage);
        Assert.Null(await fixture.UserManager.FindByEmailAsync("admin.two@demo.local"));
    }

    private sealed class UserManagementFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly ServiceProvider provider;
        private readonly AsyncServiceScope scope;

        private UserManagementFixture(
            SqliteConnection connection,
            ServiceProvider provider,
            AsyncServiceScope scope,
            AppDbContext dbContext,
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole<int>> roleManager,
            int departmentId)
        {
            this.connection = connection;
            this.provider = provider;
            this.scope = scope;
            DbContext = dbContext;
            UserManager = userManager;
            RoleManager = roleManager;
            DepartmentId = departmentId;
            CurrentUser = new FakeCurrentUserContext();
            Service = new UserManagementService(
                DbContext,
                UserManager,
                CurrentUser,
                Options.Create(new DemoUserOptions
                {
                    DefaultPassword = "Project1Demo123!"
                }));
        }

        public AppDbContext DbContext { get; }

        public UserManager<ApplicationUser> UserManager { get; }

        public RoleManager<IdentityRole<int>> RoleManager { get; }

        public FakeCurrentUserContext CurrentUser { get; }

        public UserManagementService Service { get; }

        public int DepartmentId { get; }

        public static async Task<UserManagementFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDataProtection();
            services.AddDbContext<AppDbContext>(options => options.UseSqlite(connection));
            services
                .AddIdentityCore<ApplicationUser>(options =>
                {
                    options.User.RequireUniqueEmail = true;
                    options.Password.RequiredLength = 10;
                    options.Password.RequireUppercase = true;
                    options.Password.RequireLowercase = true;
                    options.Password.RequireDigit = true;
                    options.Password.RequireNonAlphanumeric = true;
                })
                .AddRoles<IdentityRole<int>>()
                .AddEntityFrameworkStores<AppDbContext>()
                .AddDefaultTokenProviders();

            var provider = services.BuildServiceProvider();
            var scope = provider.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await dbContext.Database.EnsureCreatedAsync();

            var department = new Department
            {
                Code = "IT",
                Name = "Information Technology"
            };
            dbContext.Departments.Add(department);
            await dbContext.SaveChangesAsync();

            var roleManager = scope.ServiceProvider
                .GetRequiredService<RoleManager<IdentityRole<int>>>();
            foreach (var role in ApplicationRoles.All)
            {
                var roleResult = await roleManager.CreateAsync(new IdentityRole<int>(role));
                Assert.True(roleResult.Succeeded);
            }

            return new UserManagementFixture(
                connection,
                provider,
                scope,
                dbContext,
                scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(),
                roleManager,
                department.Id);
        }

        public async Task<ApplicationUser> CreateUserAsync(
            string email,
            IReadOnlyCollection<string> roles)
        {
            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FullName = email.Split('@')[0],
                DepartmentId = DepartmentId
            };
            var createResult = await UserManager.CreateAsync(user, "Temporary123!");
            Assert.True(createResult.Succeeded);
            var roleResult = await UserManager.AddToRolesAsync(user, roles);
            Assert.True(roleResult.Succeeded);
            return user;
        }

        public async ValueTask DisposeAsync()
        {
            await scope.DisposeAsync();
            await provider.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class FakeCurrentUserContext : ICurrentUserContext
    {
        public bool IsAuthenticated => true;

        public int UserId { get; set; } = -1;

        public string DisplayName => "Test Administrator";

        public int? DepartmentId => null;

        public IReadOnlyCollection<string> Roles => [ApplicationRoles.Admin];

        public bool IsInRole(string role) =>
            Roles.Contains(role, StringComparer.OrdinalIgnoreCase);
    }
}
