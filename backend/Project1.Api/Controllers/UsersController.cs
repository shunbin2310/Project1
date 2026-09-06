using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Project1.Api.Authentication;
using Project1.Api.DTOs.Users;
using Project1.Api.Services.Users;

namespace Project1.Api.Controllers;

[ApiController]
[Authorize(Roles = ApplicationRoles.Admin)]
[Route("api/users")]
public sealed class UsersController(IUserManagementService userManagementService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<UserResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<UserResponse>>> GetAll(
        [FromQuery] bool includeInactive = false,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        return Ok(await userManagementService.GetAllAsync(
            includeInactive,
            search,
            cancellationToken));
    }

    [HttpGet("roles")]
    [ProducesResponseType<IReadOnlyList<string>>(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<string>> GetRoles()
    {
        return Ok(ApplicationRoles.All.OrderBy(role => role).ToArray());
    }

    [HttpGet("{id:int}", Name = "GetUserById")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserResponse>> GetById(
        int id,
        CancellationToken cancellationToken)
    {
        var user = await userManagementService.GetByIdAsync(id, cancellationToken);
        return user is null ? NotFound() : Ok(user);
    }

    [HttpPost]
    [ProducesResponseType<UserResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserResponse>> Create(
        CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        var result = await userManagementService.CreateAsync(request, cancellationToken);
        if (result.Status != UserManagementStatus.Success)
        {
            return ToErrorResult(result);
        }

        return CreatedAtAction(nameof(GetById), new { id = result.User!.Id }, result.User);
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserResponse>> Update(
        int id,
        UpdateUserRequest request,
        CancellationToken cancellationToken)
    {
        var result = await userManagementService.UpdateAsync(id, request, cancellationToken);
        return result.Status == UserManagementStatus.Success
            ? Ok(result.User)
            : ToErrorResult(result);
    }

    [HttpPatch("{id:int}/status")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserResponse>> SetActive(
        int id,
        SetUserActiveRequest request,
        CancellationToken cancellationToken)
    {
        var result = await userManagementService.SetActiveAsync(
            id,
            request.IsActive!.Value,
            cancellationToken);
        return result.Status == UserManagementStatus.Success
            ? Ok(result.User)
            : ToErrorResult(result);
    }

    private ObjectResult ToErrorResult(UserManagementResult result)
    {
        var (status, title) = result.Status switch
        {
            UserManagementStatus.NotFound =>
                (StatusCodes.Status404NotFound, "User was not found."),
            UserManagementStatus.ValidationFailed =>
                (StatusCodes.Status400BadRequest, "User details are invalid."),
            UserManagementStatus.Conflict =>
                (StatusCodes.Status409Conflict, "The user could not be changed."),
            UserManagementStatus.Forbidden =>
                (StatusCodes.Status403Forbidden, "This user change is not allowed."),
            _ =>
                (StatusCodes.Status500InternalServerError, "Unexpected user management error.")
        };

        return StatusCode(status, new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = result.ErrorMessage
        });
    }
}
