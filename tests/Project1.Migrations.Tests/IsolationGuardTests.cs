using Microsoft.Data.SqlClient;

namespace Project1.Migrations.Tests;

public sealed class IsolationGuardTests
{
    [Theory]
    [InlineData(null, "true", "ci-password")]
    [InlineData("false", "true", "ci-password")]
    [InlineData("true", null, "ci-password")]
    [InlineData("true", "false", "ci-password")]
    [InlineData("true", "true", null)]
    [InlineData("true", "true", " ")]
    public void MissingGitHubOptInOrPasswordIsRejected(string? github, string? enabled, string? password)
    {
        Assert.Throws<InvalidOperationException>(() => CiSqlServerSettings.Load(name => name switch
        {
            "GITHUB_ACTIONS" => github,
            "PROJECT1_CI_MIGRATION_TESTS" => enabled,
            "PROJECT1_CI_SQLSERVER_PASSWORD" => password,
            _ => null
        }));
    }

    [Fact]
    public void ConnectionIsFixedToLoopbackContainerAndDoesNotUseApplicationSettings()
    {
        var settings = CiSqlServerSettings.Load(name => name switch
        {
            "GITHUB_ACTIONS" or "PROJECT1_CI_MIGRATION_TESTS" => "true",
            "PROJECT1_CI_SQLSERVER_PASSWORD" => "disposable-ci-password",
            _ => throw new InvalidOperationException("Must not read other configuration.")
        });
        var connection = new SqlConnectionStringBuilder(settings.ConnectionString("master"));
        Assert.Equal("tcp:127.0.0.1,14333", connection.DataSource);
        Assert.Equal("master", connection.InitialCatalog);
        Assert.Equal("sa", connection.UserID);
        Assert.False(connection.IntegratedSecurity);
        Assert.False(connection.Pooling);
    }

    [Theory]
    [InlineData("master")]
    [InlineData("fresh")]
    [InlineData("upgrade")]
    public void ConnectionTimeoutIsBoundedForAdminAndOwnedDatabases(string scenario)
    {
        var settings = new CiSqlServerSettings("disposable-ci-password");
        var database = scenario == "master"
            ? "master"
            : $"Project1CiMigration_{Guid.NewGuid():N}_{scenario}";
        var connection = new SqlConnectionStringBuilder(settings.ConnectionString(database));

        Assert.Equal(15, connection.ConnectTimeout);
        Assert.Equal("tcp:127.0.0.1,14333", connection.DataSource);
        Assert.Equal(database, connection.InitialCatalog);
        Assert.False(connection.Pooling);
    }

    [Theory]
    [InlineData("homelab-server", 17)]
    [InlineData("localhost", 17)]
    [InlineData("project1-ci-sqlserver", 16)]
    public void WrongServerOrVersionIsRejectedBeforeDatabaseWrites(string server, int version)
    {
        Assert.Throws<InvalidOperationException>(() => CiSqlServerSettings.ValidateServerIdentity(server, version));
    }

    [Theory]
    [InlineData("Project1Db")]
    [InlineData("master")]
    [InlineData("Project1CiMigration_bad_fresh")]
    [InlineData("Project1CiMigration_00000000000000000000000000000000_other")]
    [InlineData("Project1CiMigration_00000000000000000000000000000000_fresh]; DROP DATABASE Project1Db;--")]
    public void NonOwnedDatabaseNamesAreRejected(string name)
    {
        Assert.Throws<InvalidOperationException>(() => CiSqlServerSettings.ValidateDatabaseName(name));
    }

    [Theory]
    [InlineData("fresh")]
    [InlineData("upgrade")]
    public void OnlyExpectedContainerAndGeneratedDatabaseNamesAreAccepted(string scenario)
    {
        CiSqlServerSettings.ValidateServerIdentity("project1-ci-sqlserver", 17);
        CiSqlServerSettings.ValidateDatabaseName($"Project1CiMigration_{Guid.NewGuid():N}_{scenario}");
    }
}
