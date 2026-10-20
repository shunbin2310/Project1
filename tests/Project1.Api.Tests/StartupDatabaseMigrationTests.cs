using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Project1.Api.Data;

namespace Project1.Api.Tests;

public sealed class StartupDatabaseMigrationTests
{
    private static StartupDatabaseMigrator Runner(FakeDatabase database, DatabaseMigrationOptions? options = null) =>
        new(database, Options.Create(options ?? new DatabaseMigrationOptions
        {
            MigrateOnStartup = true, ExpectedDatabase = "StartupFixture"
        }), NullLogger<StartupDatabaseMigrator>.Instance);

    [Fact]
    public async Task DisabledByDefault_DoesNotReadOrMigrateDatabase()
    {
        var database = new FakeDatabase();
        await Runner(database, new DatabaseMigrationOptions()).ApplyAsync();
        Assert.Equal(0, database.NameReads);
        Assert.Equal(0, database.PendingCalls);
        Assert.Equal(0, database.MigrateCalls);
    }

    [Fact]
    public async Task NoPendingMigrations_DoesNotRunMigrator()
    {
        var database = new FakeDatabase();
        await Runner(database).ApplyAsync();
        Assert.Equal(1, database.PendingCalls);
        Assert.Equal(0, database.MigrateCalls);
    }

    [Fact]
    public async Task PendingMigrations_AreAppliedOnceWithConfiguredTimeout()
    {
        var database = new FakeDatabase { Pending = ["20261020000000_Fixture"] };
        await Runner(database).ApplyAsync();
        Assert.Equal(1, database.MigrateCalls);
        Assert.Equal(180, database.Timeout);
    }

    [Fact]
    public async Task WrongDatabase_IsRefusedBeforeHistoryOrMigration()
    {
        var database = new FakeDatabase { Name = "DifferentDatabase" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Runner(database).ApplyAsync());
        Assert.Equal(0, database.PendingCalls);
        Assert.Equal(0, database.MigrateCalls);
    }

    [Theory]
    [InlineData(null, 180)]
    [InlineData("", 180)]
    [InlineData("StartupFixture", 0)]
    [InlineData("StartupFixture", 1801)]
    public async Task InvalidSettings_FailBeforeDatabaseAccess(string? name, int timeout)
    {
        var database = new FakeDatabase();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Runner(database, new DatabaseMigrationOptions
        {
            MigrateOnStartup = true, ExpectedDatabase = name, CommandTimeoutSeconds = timeout
        }).ApplyAsync());
        Assert.Equal(0, database.NameReads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HistoryOrMigrationFailure_StopsStartupAndDoesNotExposeRawError(bool duringMigration)
    {
        var database = new FakeDatabase { Pending = ["20261020000000_Fixture"],
            FailHistory = !duringMigration, FailMigration = duringMigration };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Runner(database).ApplyAsync());
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("private-fixture-detail", error.ToString());
        Assert.Contains("API was not started", error.Message);
        Assert.Equal(duringMigration ? 1 : 0, database.MigrateCalls);
    }

    [Fact]
    public async Task Cancellation_DoesNotStartMigration()
    {
        var database = new FakeDatabase();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Runner(database).ApplyAsync(cancellation.Token));
        Assert.Equal(0, database.PendingCalls);
        Assert.Equal(0, database.MigrateCalls);
    }

    private sealed class FakeDatabase : IStartupMigrationDatabase
    {
        public string Name { get; set; } = "StartupFixture";
        public string[] Pending { get; set; } = [];
        public bool FailHistory { get; set; }
        public bool FailMigration { get; set; }
        public int NameReads { get; private set; }
        public int PendingCalls { get; private set; }
        public int MigrateCalls { get; private set; }
        public int Timeout { get; private set; }
        public string DatabaseName { get { NameReads++; return Name; } }
        public Task<IReadOnlyList<string>> GetPendingAsync(CancellationToken cancellationToken)
        {
            PendingCalls++;
            if (FailHistory) throw new InvalidOperationException("private-fixture-detail");
            return Task.FromResult<IReadOnlyList<string>>(Pending);
        }
        public Task MigrateAsync(int commandTimeoutSeconds, CancellationToken cancellationToken)
        {
            MigrateCalls++;
            Timeout = commandTimeoutSeconds;
            if (FailMigration) throw new InvalidOperationException("private-fixture-detail");
            return Task.CompletedTask;
        }
    }
}
