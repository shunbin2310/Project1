using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Project1.Api.Authentication;
using Project1.Api.Entities.Identity;

namespace Project1.Migrations.Tests;

public sealed class CatalogRoleMigrationIntegrationTests
{
    [SqlServerCiFact]
    public async Task SqlServer_ConvertsExistingMembershipsAndInvalidatesOnlyAffectedSessions()
    {
        await using var database = await CiDatabase.CreateAsync("upgrade");
        await using var context = database.CreateContext();
        var ids = context.Database.GetMigrations().ToArray();
        var index = Array.FindIndex(ids, id => id.EndsWith("_RetireCatalogManagerRole", StringComparison.Ordinal));
        await context.GetService<IMigrator>().MigrateAsync(ids[index - 1]);
        var retired = new IdentityRole<int>("CATALOG_MANAGER") { NormalizedName = "CATALOG_MANAGER" };
        var procurement = new IdentityRole<int>(ApplicationRoles.ProcurementOfficer)
            { NormalizedName = ApplicationRoles.ProcurementOfficer };
        context.Roles.AddRange(retired, procurement);
        var account = new ApplicationUser
        {
            UserName = "existing@example.test", FullName = "Existing account", SecurityStamp = "original"
        };
        var unchanged = new ApplicationUser
        {
            UserName = "other@example.test", FullName = "Other account", SecurityStamp = "untouched"
        };
        context.Users.AddRange(account, unchanged);
        await context.SaveChangesAsync();
        context.UserRoles.AddRange(
            new IdentityUserRole<int> { UserId = account.Id, RoleId = retired.Id },
            new IdentityUserRole<int> { UserId = account.Id, RoleId = procurement.Id });
        await context.SaveChangesAsync();
        await context.Database.MigrateAsync();
        context.ChangeTracker.Clear();
        Assert.False(await context.Roles.AnyAsync(role => role.NormalizedName == "CATALOG_MANAGER"));
        var membership = await context.UserRoles.SingleAsync();
        Assert.Equal(procurement.Id, membership.RoleId);
        Assert.Equal(account.Id, membership.UserId);
        Assert.Equal("Existing account", (await context.Users.SingleAsync(user => user.Id == account.Id)).FullName);
        var changedStamp = (await context.Users.SingleAsync(user => user.Id == account.Id)).SecurityStamp;
        Assert.NotEqual("original", changedStamp);
        Assert.Equal("untouched", (await context.Users.SingleAsync(user => user.Id == unchanged.Id)).SecurityStamp);
        await context.Database.MigrateAsync();
        context.ChangeTracker.Clear();
        Assert.Equal(changedStamp, (await context.Users.SingleAsync(user => user.Id == account.Id)).SecurityStamp);
        await MigrationIntegrationTests.AssertAllMigrationsAppliedAsync(context);
    }
}
