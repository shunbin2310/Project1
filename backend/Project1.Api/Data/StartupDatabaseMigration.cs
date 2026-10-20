using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Project1.Api.Data;

public sealed class DatabaseMigrationOptions
{
    public const string SectionName = "Database";

    // Explicit opt-in: ordinary tests and EF design-time commands must not migrate.
    public bool MigrateOnStartup { get; set; }
    public string? ExpectedDatabase { get; set; }
    public int CommandTimeoutSeconds { get; set; } = 180;
}

public interface IStartupMigrationDatabase
{
    string DatabaseName { get; }
    Task<IReadOnlyList<string>> GetPendingAsync(CancellationToken cancellationToken);
    Task MigrateAsync(int commandTimeoutSeconds, CancellationToken cancellationToken);
}

public sealed class EfStartupMigrationDatabase(AppDbContext context) : IStartupMigrationDatabase
{
    public string DatabaseName => context.Database.GetDbConnection().Database;

    public async Task<IReadOnlyList<string>> GetPendingAsync(CancellationToken cancellationToken) =>
        (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();

    public async Task MigrateAsync(int commandTimeoutSeconds, CancellationToken cancellationToken)
    {
        var previousTimeout = context.Database.GetCommandTimeout();
        context.Database.SetCommandTimeout(commandTimeoutSeconds);
        try
        {
            // EF handles its migration lock/transactions. Never request a Down migration.
            await context.Database.MigrateAsync(cancellationToken);
        }
        finally
        {
            context.Database.SetCommandTimeout(previousTimeout);
        }
    }
}

public sealed class StartupDatabaseMigrator(
    IStartupMigrationDatabase database,
    IOptions<DatabaseMigrationOptions> options,
    ILogger<StartupDatabaseMigrator> logger)
{
    public async Task ApplyAsync(CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        if (!settings.MigrateOnStartup)
        {
            logger.LogInformation("Startup database migration is disabled.");
            return;
        }

        if (string.IsNullOrWhiteSpace(settings.ExpectedDatabase) ||
            settings.CommandTimeoutSeconds is < 1 or > 1800)
        {
            throw new InvalidOperationException(
                "Startup migration requires Database:ExpectedDatabase and a command timeout between 1 and 1800 seconds.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!string.Equals(database.DatabaseName, settings.ExpectedDatabase, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Unexpected migration database.");
            }

            var pending = await database.GetPendingAsync(cancellationToken);
            if (pending.Count == 0)
            {
                logger.LogInformation("No pending EF migrations; startup migration skipped.");
                return;
            }

            logger.LogInformation("Applying {Count} pending EF migrations before application startup: {Migrations}",
                pending.Count, string.Join(", ", pending));
            await database.MigrateAsync(settings.CommandTimeoutSeconds, cancellationToken);
            logger.LogInformation("Startup EF migration completed.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Fail before seeds, hosted email worker or HTTP listener starts. Do not include
            // raw SQL, connection strings or database values in the outer startup exception.
            logger.LogCritical("Startup database migration failed; application startup is refused.");
            throw new InvalidOperationException(
                "Startup database migration failed. Review database diagnostics before retrying; the API was not started.");
        }
    }
}
