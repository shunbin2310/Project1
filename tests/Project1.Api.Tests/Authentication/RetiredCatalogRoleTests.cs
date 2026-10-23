using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Project1.Api.Authentication;
using Project1.Api.Data;
using Project1.Api.Entities.Identity;
using Project1.Api.Entities.Workflows;
using Project1.Api.Migrations;
using Project1.Api.Services.Workflows;

namespace Project1.Api.Tests.Authentication;

public sealed class RetiredCatalogRoleTests
{
    [Fact]
    public async Task FreshMigrationAndRepeatedDemoSeeding_DoNotRecreateRetiredRoleOrAccount()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        connection.CreateFunction("NEWID", () => Guid.NewGuid().ToString());
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(connection));
        services.AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole<int>>()
            .AddEntityFrameworkStores<AppDbContext>();
        services.AddSingleton<IOptions<DemoUserOptions>>(Options.Create(new DemoUserOptions
        {
            Enabled = true, DefaultPassword = "Project1Demo123!", DepartmentCode = "IT"
        }));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await context.Database.EnsureCreatedAsync();
        var sql = Assert.IsType<SqlOperation>(Assert.Single(new RetireCatalogManagerRole().UpOperations)).Sql;
        await context.Database.ExecuteSqlRawAsync(sql);
        await provider.SeedIdentityAsync();
        await provider.SeedIdentityAsync();
        Assert.Equal(7, await context.Roles.CountAsync());
        Assert.Equal(7, await context.Users.CountAsync());
        Assert.False(await context.Roles.AnyAsync(role => role.NormalizedName == "CATALOG_MANAGER"));
        Assert.False(await context.Users.AnyAsync(user => user.Email == "catalog@demo.local"));
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var procurement = await manager.FindByEmailAsync("procurement@demo.local");
        Assert.Equal([ApplicationRoles.ProcurementOfficer], await manager.GetRolesAsync(procurement!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Migration_TransfersAccountsAndWorkflowAssignmentsWithoutLosingHistory(bool procurementExists)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        // Run the migration's actual data SQL locally; SQL Server provides NEWID natively.
        connection.CreateFunction("NEWID", () => Guid.NewGuid().ToString());
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();

        var retired = new IdentityRole<int>("CATALOG_MANAGER") { NormalizedName = "CATALOG_MANAGER" };
        var warehouse = new IdentityRole<int>(ApplicationRoles.WarehouseOfficer)
            { NormalizedName = ApplicationRoles.WarehouseOfficer };
        var procurement = new IdentityRole<int>(ApplicationRoles.ProcurementOfficer)
            { NormalizedName = ApplicationRoles.ProcurementOfficer };
        context.Roles.AddRange(retired, warehouse);
        if (procurementExists) context.Roles.Add(procurement);
        context.Users.AddRange(User(101), User(102, active: false), User(103));
        await context.SaveChangesAsync();
        context.UserRoles.AddRange(
            new IdentityUserRole<int> { UserId = 101, RoleId = retired.Id },
            new IdentityUserRole<int> { UserId = 101, RoleId = warehouse.Id },
            new IdentityUserRole<int> { UserId = 102, RoleId = retired.Id },
            new IdentityUserRole<int> { UserId = 103, RoleId = warehouse.Id });
        context.RoleClaims.Add(new IdentityRoleClaim<int>
            { RoleId = retired.Id, ClaimType = "product", ClaimValue = "manage" });
        if (procurementExists)
        {
            context.UserRoles.Add(new IdentityUserRole<int> { UserId = 102, RoleId = procurement.Id });
            context.RoleClaims.Add(new IdentityRoleClaim<int>
                { RoleId = procurement.Id, ClaimType = "product", ClaimValue = "manage" });
        }
        var review = await context.WorkflowActionerTemplates.SingleAsync(item => item.Id == 2);
        review.ActionerKey = "CATALOG_MANAGER";
        if (procurementExists)
        {
            context.WorkflowActionerTemplates.Add(new WorkflowActionerTemplate
            {
                ActionTemplateId = review.ActionTemplateId,
                ActionerType = WorkflowActionerType.Role, ActionerKey = ApplicationRoles.ProcurementOfficer
            });
        }
        await context.SaveChangesAsync();
        var engine = new WorkflowEngine(context);
        var started = await engine.StartAsync("PurchaseRequest", 123,
            new WorkflowActor(101, "Original account", ["CATALOG_MANAGER"]), CancellationToken.None);
        Assert.Equal(WorkflowExecutionStatus.Success, started.Status);
        var history = await context.WorkflowHistory.AsNoTracking().SingleAsync();
        var originalHistory = (history.Id, history.ActionBy, history.Comment, history.ActionAtUtc);

        var sql = Assert.IsType<SqlOperation>(Assert.Single(new RetireCatalogManagerRole().UpOperations)).Sql;
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            await context.Database.ExecuteSqlRawAsync(sql);
            await transaction.CommitAsync();
        }
        context.ChangeTracker.Clear();
        Assert.False(await context.Roles.AnyAsync(role => role.NormalizedName == "CATALOG_MANAGER"));
        var newRole = await context.Roles.SingleAsync(role => role.NormalizedName == ApplicationRoles.ProcurementOfficer);
        if (procurementExists) Assert.Equal(procurement.Id, newRole.Id);
        Assert.Equal(3, await context.Users.CountAsync());
        Assert.Equal(2, await context.UserRoles.CountAsync(link => link.RoleId == newRole.Id));
        Assert.True(await context.UserRoles.AnyAsync(link => link.UserId == 101 && link.RoleId == warehouse.Id));
        Assert.Single(await context.RoleClaims.Where(claim => claim.RoleId == newRole.Id).ToListAsync());
        foreach (var id in new[] { 101, 102 })
        {
            var user = await context.Users.SingleAsync(item => item.Id == id);
            Assert.Equal($"Account {id}", user.FullName);
            Assert.Equal($"password-hash-{id}", user.PasswordHash);
            Assert.NotEqual($"security-{id}", user.SecurityStamp);
        }
        Assert.False((await context.Users.SingleAsync(user => user.Id == 102)).IsActive);
        Assert.Equal("security-103", (await context.Users.SingleAsync(user => user.Id == 103)).SecurityStamp);
        Assert.False(await context.WorkflowActionerTemplates.AnyAsync(item => item.ActionerKey == "CATALOG_MANAGER"));
        Assert.False(await context.WorkflowActionerInstances.AnyAsync(item => item.ActionerKey == "CATALOG_MANAGER"));
        Assert.Single(await context.WorkflowActionerTemplates.Where(item =>
            item.ActionTemplateId == review.ActionTemplateId && item.ActionerKey == ApplicationRoles.ProcurementOfficer).ToListAsync());
        var storedHistory = await context.WorkflowHistory.SingleAsync();
        Assert.Equal(originalHistory, (storedHistory.Id, storedHistory.ActionBy, storedHistory.Comment, storedHistory.ActionAtUtc));

        // Replaying the data operation must neither duplicate memberships nor expire sessions again.
        var securityStamp = (await context.Users.SingleAsync(user => user.Id == 101)).SecurityStamp;
        await context.Database.ExecuteSqlRawAsync(sql);
        context.ChangeTracker.Clear();
        Assert.Equal(securityStamp, (await context.Users.SingleAsync(user => user.Id == 101)).SecurityStamp);
        Assert.Equal(2, await context.UserRoles.CountAsync(link => link.RoleId == newRole.Id));
        var actor = new WorkflowActor(101, "Original account", [ApplicationRoles.ProcurementOfficer]);
        Assert.Equal(WorkflowExecutionStatus.Success, (await engine.ExecuteActionAsync(
            "PurchaseRequest", 123, "SUBMIT", actor, null, CancellationToken.None)).Status);
        Assert.Equal(WorkflowExecutionStatus.Success, (await engine.ExecuteActionAsync(
            "PurchaseRequest", 123, "APPROVE", actor, null, CancellationToken.None)).Status);
    }

    private static ApplicationUser User(int id, bool active = true) => new()
    {
        Id = id, UserName = $"user{id}@example.test", NormalizedUserName = $"USER{id}@EXAMPLE.TEST",
        Email = $"user{id}@example.test", FullName = $"Account {id}",
        PasswordHash = $"password-hash-{id}", SecurityStamp = $"security-{id}", IsActive = active
    };
}
