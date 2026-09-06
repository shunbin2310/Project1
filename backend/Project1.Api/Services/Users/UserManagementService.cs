using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Project1.Api.Authentication;
using Project1.Api.Data;
using Project1.Api.DTOs.Users;
using Project1.Api.Entities.Identity;
using Project1.Api.Services.Authentication;

namespace Project1.Api.Services.Users;

public sealed class UserManagementService(
    AppDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    ICurrentUserContext currentUser,
    IOptions<DemoUserOptions> demoUserOptions) : IUserManagementService
{
    private static readonly HashSet<string> ValidRoles =
        ApplicationRoles.All.ToHashSet(StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<UserResponse>> GetAllAsync(
        bool includeInactive,
        string? search,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Users
            .AsNoTracking()
            .Include(user => user.Department)
            .AsQueryable();

        if (!includeInactive)
        {
            query = query.Where(user => user.IsActive);
        }

        var term = search?.Trim();
        if (!string.IsNullOrWhiteSpace(term))
        {
            query = query.Where(user =>
                (user.Email != null && user.Email.Contains(term)) ||
                user.FullName.Contains(term) ||
                (user.Department != null &&
                    (user.Department.Code.Contains(term) || user.Department.Name.Contains(term))));
        }

        var users = await query
            .OrderBy(user => user.Email)
            .ToListAsync(cancellationToken);
        var responses = new List<UserResponse>(users.Count);

        foreach (var user in users)
        {
            responses.Add(await ToResponseAsync(user));
        }

        return responses;
    }

    public async Task<UserResponse?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Users
            .AsNoTracking()
            .Include(item => item.Department)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        return user is null ? null : await ToResponseAsync(user);
    }

    public async Task<UserManagementResult> CreateAsync(
        CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateInputAsync(
            request.FullName,
            request.DepartmentId,
            request.Roles,
            cancellationToken);
        if (validation.Error is not null)
        {
            return ValidationFailed(validation.Error);
        }

        var defaultPassword = demoUserOptions.Value.DefaultPassword;
        if (string.IsNullOrWhiteSpace(defaultPassword))
        {
            return ValidationFailed(
                "The default password for new users is not configured.");
        }

        var email = request.Email.Trim();
        if (await userManager.FindByEmailAsync(email) is not null)
        {
            return Conflict($"A user with email '{email}' already exists.");
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = request.FullName.Trim(),
            DepartmentId = request.DepartmentId,
            IsActive = true
        };

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            cancellationToken);
        var createResult = await userManager.CreateAsync(user, defaultPassword);
        if (!createResult.Succeeded)
        {
            return IdentityFailure(createResult);
        }

        var roleResult = await userManager.AddToRolesAsync(user, validation.Roles!);
        if (!roleResult.Succeeded)
        {
            return IdentityFailure(roleResult);
        }

        await transaction.CommitAsync(cancellationToken);
        return new UserManagementResult(
            UserManagementStatus.Success,
            await GetByIdAsync(user.Id, cancellationToken));
    }

    public async Task<UserManagementResult> UpdateAsync(
        int id,
        UpdateUserRequest request,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return NotFound();
        }

        var validation = await ValidateInputAsync(
            request.FullName,
            request.DepartmentId,
            request.Roles,
            cancellationToken);
        if (validation.Error is not null)
        {
            return ValidationFailed(validation.Error);
        }

        var validatedRoles = validation.Roles!;

        var existingRoles = await userManager.GetRolesAsync(user);
        var removesAdmin = existingRoles.Contains(
            ApplicationRoles.Admin,
            StringComparer.OrdinalIgnoreCase) &&
            !validatedRoles.Contains(
                ApplicationRoles.Admin,
                StringComparer.OrdinalIgnoreCase);

        if (id == currentUser.UserId && removesAdmin)
        {
            return Forbidden("You cannot remove your own ADMIN role.");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        if (user.IsActive && removesAdmin && await IsLastActiveAdminAsync())
        {
            return Conflict("The system must keep at least one active administrator.");
        }

        user.FullName = request.FullName.Trim();
        user.DepartmentId = request.DepartmentId;
        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            return IdentityFailure(updateResult);
        }

        var rolesToRemove = existingRoles
            .Except(validatedRoles, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var rolesToAdd = validatedRoles
            .Except(existingRoles, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (rolesToRemove.Length > 0)
        {
            var removeResult = await userManager.RemoveFromRolesAsync(user, rolesToRemove);
            if (!removeResult.Succeeded)
            {
                return IdentityFailure(removeResult);
            }
        }

        if (rolesToAdd.Length > 0)
        {
            var addResult = await userManager.AddToRolesAsync(user, rolesToAdd);
            if (!addResult.Succeeded)
            {
                return IdentityFailure(addResult);
            }
        }

        var stampResult = await userManager.UpdateSecurityStampAsync(user);
        if (!stampResult.Succeeded)
        {
            return IdentityFailure(stampResult);
        }

        await transaction.CommitAsync(cancellationToken);
        return new UserManagementResult(
            UserManagementStatus.Success,
            await GetByIdAsync(id, cancellationToken));
    }

    public async Task<UserManagementResult> SetActiveAsync(
        int id,
        bool isActive,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return NotFound();
        }

        if (!isActive && id == currentUser.UserId)
        {
            return Forbidden("You cannot deactivate your own account.");
        }

        if (user.IsActive == isActive)
        {
            return new UserManagementResult(
                UserManagementStatus.Success,
                await GetByIdAsync(id, cancellationToken));
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var roles = await userManager.GetRolesAsync(user);

        if (!isActive &&
            roles.Contains(ApplicationRoles.Admin, StringComparer.OrdinalIgnoreCase) &&
            await IsLastActiveAdminAsync())
        {
            return Conflict("The system must keep at least one active administrator.");
        }

        user.IsActive = isActive;
        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            return IdentityFailure(updateResult);
        }

        var stampResult = await userManager.UpdateSecurityStampAsync(user);
        if (!stampResult.Succeeded)
        {
            return IdentityFailure(stampResult);
        }

        await transaction.CommitAsync(cancellationToken);
        return new UserManagementResult(
            UserManagementStatus.Success,
            await GetByIdAsync(id, cancellationToken));
    }

    private async Task<(IReadOnlyList<string>? Roles, string? Error)> ValidateInputAsync(
        string fullName,
        int? departmentId,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken)
    {
        var normalizedName = fullName.Trim();
        if (normalizedName.Length is < 2 or > 100)
        {
            return (null, "Full name must contain between 2 and 100 characters.");
        }

        if (departmentId.HasValue &&
            !await dbContext.Departments.AnyAsync(
                department => department.Id == departmentId && department.IsActive,
                cancellationToken))
        {
            return (null, "The selected active department does not exist.");
        }

        var normalizedRoles = roles
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Select(role => role.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(role => role)
            .ToArray();

        if (normalizedRoles.Length == 0)
        {
            return (null, "Select at least one role.");
        }

        var invalidRoles = normalizedRoles.Where(role => !ValidRoles.Contains(role)).ToArray();
        if (invalidRoles.Length > 0)
        {
            return (null, $"Unknown roles: {string.Join(", ", invalidRoles)}.");
        }

        if (normalizedRoles.Contains(ApplicationRoles.Admin) && normalizedRoles.Length > 1)
        {
            return (null, "ADMIN already has full access and cannot be combined with other roles.");
        }

        return (normalizedRoles, null);
    }

    private async Task<bool> IsLastActiveAdminAsync()
    {
        var administrators = await userManager.GetUsersInRoleAsync(ApplicationRoles.Admin);
        return administrators.Count(user => user.IsActive) <= 1;
    }

    private async Task<UserResponse> ToResponseAsync(ApplicationUser user)
    {
        var roles = await userManager.GetRolesAsync(user);
        return new UserResponse(
            user.Id,
            user.Email ?? string.Empty,
            user.FullName,
            user.DepartmentId,
            user.Department?.Code,
            user.Department?.Name,
            user.IsActive,
            roles.OrderBy(role => role).ToArray(),
            user.CreatedAtUtc);
    }

    private static UserManagementResult IdentityFailure(IdentityResult result)
    {
        var isConflict = result.Errors.Any(error =>
            error.Code is "DuplicateEmail" or "DuplicateUserName");
        return new UserManagementResult(
            isConflict ? UserManagementStatus.Conflict : UserManagementStatus.ValidationFailed,
            ErrorMessage: string.Join("; ", result.Errors.Select(error => error.Description)));
    }

    private static UserManagementResult NotFound() =>
        new(UserManagementStatus.NotFound, ErrorMessage: "User was not found.");

    private static UserManagementResult ValidationFailed(string message) =>
        new(UserManagementStatus.ValidationFailed, ErrorMessage: message);

    private static UserManagementResult Conflict(string message) =>
        new(UserManagementStatus.Conflict, ErrorMessage: message);

    private static UserManagementResult Forbidden(string message) =>
        new(UserManagementStatus.Forbidden, ErrorMessage: message);
}
