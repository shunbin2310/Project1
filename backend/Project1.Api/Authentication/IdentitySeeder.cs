using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Project1.Api.Data;
using Project1.Api.Entities;
using Project1.Api.Entities.Identity;

namespace Project1.Api.Authentication;

public static class IdentitySeeder
{
    private sealed record DemoDepartmentDefinition(
        string Code,
        string Name,
        string Description);

    private sealed record DemoUserDefinition(
        string Email,
        string FullName,
        string? DepartmentCode,
        IReadOnlyCollection<string> Roles);

    private static readonly DemoDepartmentDefinition[] OperationalDepartments =
    [
        new("FIN", "Finance", "Budget review and financial approval."),
        new("PROC", "Procurement", "Supplier sourcing, quotations, and purchase orders."),
        new("WH", "Warehouse", "Goods receiving and inventory operations."),
        new("CAT", "Catalog Management", "Product and purchasing catalog maintenance.")
    ];

    private static readonly DemoUserDefinition[] DemoUsers =
    [
        new("requester@demo.local", "Demo Requester", null, [ApplicationRoles.Requester]),
        new(
            "department@demo.local",
            "Department Approver",
            null,
            [ApplicationRoles.DepartmentApprover]),
        new("finance@demo.local", "Finance Approver", "FIN", [ApplicationRoles.FinanceApprover]),
        new(
            "procurement@demo.local",
            "Procurement Officer",
            "PROC",
            [ApplicationRoles.ProcurementOfficer]),
        new(
            "warehouse@demo.local",
            "Warehouse Officer",
            "WH",
            [ApplicationRoles.WarehouseOfficer]),
        new(
            "catalog@demo.local",
            "Catalog Manager",
            "CAT",
            [ApplicationRoles.CatalogManager]),
        new("admin@demo.local", "Demo Admin", null, [ApplicationRoles.Admin])
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
        var defaultDepartmentCode = options.DepartmentCode.Trim().ToUpperInvariant();
        var department = await dbContext.Departments
            .SingleOrDefaultAsync(item => item.Code == defaultDepartmentCode);

        if (department is null)
        {
            department = new Department
            {
                Code = defaultDepartmentCode,
                Name = "Information Technology",
                Description = "Demo department for the Project1 interview environment."
            };
            dbContext.Departments.Add(department);
            await dbContext.SaveChangesAsync();
        }

        var departmentsByCode = new Dictionary<string, Department>(StringComparer.OrdinalIgnoreCase)
        {
            [defaultDepartmentCode] = department
        };

        foreach (var definition in OperationalDepartments)
        {
            var operationalDepartment = await dbContext.Departments
                .SingleOrDefaultAsync(item => item.Code == definition.Code);

            if (operationalDepartment is null)
            {
                operationalDepartment = new Department
                {
                    Code = definition.Code,
                    Name = definition.Name,
                    Description = definition.Description
                };
                dbContext.Departments.Add(operationalDepartment);
                await dbContext.SaveChangesAsync();
            }

            departmentsByCode[definition.Code] = operationalDepartment;
        }

        foreach (var definition in DemoUsers)
        {
            var userDepartment = definition.DepartmentCode is null
                ? department
                : departmentsByCode[definition.DepartmentCode];
            var user = await userManager.FindByEmailAsync(definition.Email);

            if (user is null)
            {
                user = new ApplicationUser
                {
                    UserName = definition.Email,
                    Email = definition.Email,
                    EmailConfirmed = true,
                    FullName = definition.FullName,
                    DepartmentId = userDepartment.Id,
                    IsActive = true
                };

                var createResult = await userManager.CreateAsync(user, options.DefaultPassword);
                EnsureSucceeded(createResult, $"create demo user '{definition.Email}'");
            }
            else
            {
                var identityChanged = user.FullName != definition.FullName ||
                                      user.DepartmentId != userDepartment.Id ||
                                      !user.IsActive ||
                                      !user.EmailConfirmed;
                user.FullName = definition.FullName;
                user.DepartmentId = userDepartment.Id;
                user.IsActive = true;
                user.EmailConfirmed = true;
                var updateResult = await userManager.UpdateAsync(user);
                EnsureSucceeded(updateResult, $"update demo user '{definition.Email}'");

                if (identityChanged)
                {
                    var stampResult = await userManager.UpdateSecurityStampAsync(user);
                    EnsureSucceeded(
                        stampResult,
                        $"refresh demo user '{definition.Email}' security stamp");
                }
            }

            var existingRoles = await userManager.GetRolesAsync(user);
            var extraRoles = existingRoles
                .Except(definition.Roles, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var missingRoles = definition.Roles
                .Except(existingRoles, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (extraRoles.Length > 0)
            {
                var removeRolesResult = await userManager.RemoveFromRolesAsync(user, extraRoles);
                EnsureSucceeded(removeRolesResult, $"remove roles from demo user '{definition.Email}'");
            }

            if (missingRoles.Length > 0)
            {
                var addRolesResult = await userManager.AddToRolesAsync(user, missingRoles);
                EnsureSucceeded(addRolesResult, $"assign roles to demo user '{definition.Email}'");
            }

            if (extraRoles.Length > 0 || missingRoles.Length > 0)
            {
                var stampResult = await userManager.UpdateSecurityStampAsync(user);
                EnsureSucceeded(stampResult, $"refresh demo user '{definition.Email}' security stamp");
            }
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
