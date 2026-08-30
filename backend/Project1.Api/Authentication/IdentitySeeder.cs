using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Project1.Api.Data;
using Project1.Api.Entities;
using Project1.Api.Entities.Identity;

namespace Project1.Api.Authentication;

public static class IdentitySeeder
{
    private sealed record DemoUserDefinition(
        string Email,
        string FullName,
        IReadOnlyCollection<string> Roles);

    private static readonly DemoUserDefinition[] DemoUsers =
    [
        new("requester@demo.local", "Demo Requester", [ApplicationRoles.Requester]),
        new(
            "department@demo.local",
            "Department Approver",
            [ApplicationRoles.DepartmentApprover]),
        new("finance@demo.local", "Finance Approver", [ApplicationRoles.FinanceApprover]),
        new("admin@demo.local", "Demo Admin", [ApplicationRoles.Admin])
    ];

    public static async Task SeedIdentityAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<int>>>();

        foreach (var roleName in ApplicationRoles.All)
        {
            if (await roleManager.RoleExistsAsync(roleName))
            {
                continue;
            }

            var roleResult = await roleManager.CreateAsync(new IdentityRole<int>(roleName));
            EnsureSucceeded(roleResult, $"create role '{roleName}'");
        }

        var options = scope.ServiceProvider.GetRequiredService<IOptions<DemoUserOptions>>().Value;
        if (!options.Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(options.DefaultPassword))
        {
            throw new InvalidOperationException(
                "Demo users are enabled, but DemoUsers:DefaultPassword is not configured.");
        }

        if (string.IsNullOrWhiteSpace(options.DepartmentCode))
        {
            throw new InvalidOperationException(
                "Demo users are enabled, but DemoUsers:DepartmentCode is not configured.");
        }

        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var department = await dbContext.Departments
            .SingleOrDefaultAsync(item => item.Code == options.DepartmentCode);

        if (department is null)
        {
            department = new Department
            {
                Code = options.DepartmentCode.Trim().ToUpperInvariant(),
                Name = "Information Technology",
                Description = "Demo department for the Project1 interview environment."
            };
            dbContext.Departments.Add(department);
            await dbContext.SaveChangesAsync();
        }

        foreach (var definition in DemoUsers)
        {
            var user = await userManager.FindByEmailAsync(definition.Email);

            if (user is null)
            {
                user = new ApplicationUser
                {
                    UserName = definition.Email,
                    Email = definition.Email,
                    EmailConfirmed = true,
                    FullName = definition.FullName,
                    DepartmentId = department.Id,
                    IsActive = true
                };

                var createResult = await userManager.CreateAsync(user, options.DefaultPassword);
                EnsureSucceeded(createResult, $"create demo user '{definition.Email}'");
            }
            else
            {
                user.FullName = definition.FullName;
                user.DepartmentId = department.Id;
                user.IsActive = true;
                user.EmailConfirmed = true;
                var updateResult = await userManager.UpdateAsync(user);
                EnsureSucceeded(updateResult, $"update demo user '{definition.Email}'");
            }

            var existingRoles = await userManager.GetRolesAsync(user);
            var missingRoles = definition.Roles.Except(existingRoles, StringComparer.OrdinalIgnoreCase);
            var addRolesResult = await userManager.AddToRolesAsync(user, missingRoles);
            EnsureSucceeded(addRolesResult, $"assign roles to demo user '{definition.Email}'");
        }
    }

    private static void EnsureSucceeded(IdentityResult result, string operation)
    {
        if (result.Succeeded)
        {
            return;
        }

        var errors = string.Join("; ", result.Errors.Select(error => error.Description));
        throw new InvalidOperationException($"Unable to {operation}: {errors}");
    }
}
