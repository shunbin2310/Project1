using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Project1.Api.Authentication;
using Project1.Api.Controllers;
using Project1.Api.DTOs.Users;
using Project1.Api.Services.Users;

namespace Project1.Api.Tests.Controllers;

public sealed class UsersControllerTests
{
    [Fact]
    public void Controller_RequiresAdminRole()
    {
        var attribute = Assert.Single(typeof(UsersController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>());

        Assert.Equal(ApplicationRoles.Admin, attribute.Roles);
    }

    [Fact]
    public void UpdateRequest_DoesNotExposeEmail()
    {
        Assert.Null(typeof(UpdateUserRequest).GetProperty("Email"));
    }

    [Fact]
    public void CreateRequest_DoesNotExposePassword()
    {
        Assert.Null(typeof(CreateUserRequest).GetProperty("TemporaryPassword"));
    }

    [Fact]
    public async Task Create_ReturnsCreatedUser()
    {
        var response = CreateResponse();
        var service = new FakeUserManagementService
        {
            CreateResult = new UserManagementResult(
                UserManagementStatus.Success,
                response)
        };
        var controller = new UsersController(service);

        var result = await controller.Create(new CreateUserRequest
        {
            Email = response.Email,
            FullName = response.FullName,
            Roles = response.Roles
        }, CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(nameof(UsersController.GetById), created.ActionName);
        Assert.Equal(response, created.Value);
    }

    [Fact]
    public async Task SetActive_ReturnsForbidden_WhenChangingOwnAccount()
    {
        var service = new FakeUserManagementService
        {
            SetActiveResult = new UserManagementResult(
                UserManagementStatus.Forbidden,
                ErrorMessage: "You cannot deactivate your own account.")
        };
        var controller = new UsersController(service);

        var result = await controller.SetActive(
            1,
            new SetUserActiveRequest { IsActive = false },
            CancellationToken.None);

        var error = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, error.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(error.Value);
        Assert.Equal("You cannot deactivate your own account.", problem.Detail);
    }

    private static UserResponse CreateResponse() => new(
        1,
        "new.user@demo.local",
        "New User",
        1,
        "IT",
        "Information Technology",
        true,
        [ApplicationRoles.Requester],
        DateTimeOffset.UtcNow);

    private sealed class FakeUserManagementService : IUserManagementService
    {
        public UserManagementResult CreateResult { get; init; } =
            new(UserManagementStatus.Success, CreateResponse());

        public UserManagementResult UpdateResult { get; init; } =
            new(UserManagementStatus.Success, CreateResponse());

        public UserManagementResult SetActiveResult { get; init; } =
            new(UserManagementStatus.Success, CreateResponse());

        public Task<IReadOnlyList<UserResponse>> GetAllAsync(
            bool includeInactive,
            string? search,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UserResponse>>([]);

        public Task<UserResponse?> GetByIdAsync(
            int id,
            CancellationToken cancellationToken) =>
            Task.FromResult<UserResponse?>(null);

        public Task<UserManagementResult> CreateAsync(
            CreateUserRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(CreateResult);

        public Task<UserManagementResult> UpdateAsync(
            int id,
            UpdateUserRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(UpdateResult);

        public Task<UserManagementResult> SetActiveAsync(
            int id,
            bool isActive,
            CancellationToken cancellationToken) =>
            Task.FromResult(SetActiveResult);

    }
}
