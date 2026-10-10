using Microsoft.Data.SqlClient;

namespace Project1.Migrations.Tests;

public sealed class MigrationExecutorGuardTests
{
    [Theory]
    [InlineData("sa")]
    [InlineData("project1_migrate")]
    [InlineData("Project1CiExecutor_bad")]
    [InlineData("Project1CiExecutor_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("Project1CiExecutor_00000000000000000000000000000000]; DROP LOGIN sa;--")]
    public void NonOwnedLoginNamesAreRejected(string name)
    {
        Assert.Throws<InvalidOperationException>(() => CiSqlServerSettings.ValidateLoginName(name));
        Assert.Throws<InvalidOperationException>(() =>
            CiMigrationAccount.GrantSql(name, CiMigrationPermissionProfile.ProductNoteExecutor));
    }

    [Fact]
    public void RestrictedConnectionUsesOwnCredentialAndFixedCiEndpoint()
    {
        var settings = new CiSqlServerSettings("admin-disposable-password");
        var database = $"Project1CiMigration_{Guid.NewGuid():N}_upgrade";
        var login = $"Project1CiExecutor_{Guid.NewGuid():N}";
        var connection = new SqlConnectionStringBuilder(
            settings.AccountConnectionString(database, login, "account-disposable-password"));

        Assert.Equal("tcp:127.0.0.1,14333", connection.DataSource);
        Assert.Equal(database, connection.InitialCatalog);
        Assert.Equal(login, connection.UserID);
        Assert.Equal("account-disposable-password", connection.Password);
        Assert.False(connection.IntegratedSecurity);
        Assert.False(connection.Pooling);
        Assert.Throws<InvalidOperationException>(() => settings.AccountConnectionString("Project1Db", login, "test"));
    }

    [Theory]
    [InlineData((int)CiMigrationPermissionProfile.HistoryReadOnly, false, false)]
    [InlineData((int)CiMigrationPermissionProfile.ProductNoteWithoutHistoryInsert, true, false)]
    [InlineData((int)CiMigrationPermissionProfile.ProductNoteExecutor, true, true)]
    public void ProfilesGrantOnlyTheReviewedNoteUpgradePermissions(
        int profile, bool alterProducts, bool insertHistory)
    {
        const string name = "Project1CiExecutor_00000000000000000000000000000000";
        var expected = $"GRANT CONNECT TO [{name}]; GRANT SELECT ON OBJECT::dbo.__EFMigrationsHistory TO [{name}];";
        if (alterProducts) expected += $" GRANT ALTER ON OBJECT::dbo.Products TO [{name}];";
        if (insertHistory) expected += $" GRANT INSERT ON OBJECT::dbo.__EFMigrationsHistory TO [{name}];";
        Assert.Equal(expected, CiMigrationAccount.GrantSql(name, (CiMigrationPermissionProfile)profile));
    }

    [Fact]
    public void UnknownPermissionProfileIsRejected()
    {
        Assert.Throws<InvalidOperationException>(() => CiMigrationAccount.GrantSql(
            $"Project1CiExecutor_{Guid.NewGuid():N}", (CiMigrationPermissionProfile)123));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void PermissionCheckAcceptsOnlyTheExpectedValue(int expected)
    {
        MigrationExecutorSqlIntegrationTests.AssertPermissionValue("History.INSERT", expected, expected);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    [InlineData(2, 0)]
    public void PermissionCheckRejectsUnknownOrUnexpectedValuesWithNamedDiagnostics(int? actual, int expected)
    {
        var error = Assert.ThrowsAny<Xunit.Sdk.XunitException>(() =>
            MigrationExecutorSqlIntegrationTests.AssertPermissionValue("Server.CONTROL SERVER", actual, expected));

        Assert.Contains("Server.CONTROL SERVER", error.Message);
        Assert.Contains($"expected {expected}", error.Message);
        Assert.Contains(actual.HasValue ? $"returned {actual}" : "returned NULL (unknown)", error.Message);
    }

    [Theory]
    [InlineData(1088, false, false)]
    [InlineData(1088, true, true)]
    [InlineData(229, false, true)]
    [InlineData(207, true, false)]
    [InlineData(245, true, false)]
    [InlineData(102, true, false)]
    [InlineData(916, true, false)]
    [InlineData(266, true, false)]
    public void HiddenObjectErrorRequiresVerifiedExistenceAndOtherErrorsAreNotPermissionErrors(
        int number, bool verifiedExistingObject, bool expected)
    {
        Assert.Equal(expected, MigrationExecutorSqlIntegrationTests.IsPermissionErrorNumber(number, verifiedExistingObject));
    }

    [Fact]
    public void GuardedCommandInlinesOriginalBatchWithoutExecuteAndRethrowsAfterRollback()
    {
        const string batch = "SELECT N'quoted '' text\r\nGO\r\n'; -- trailing comment without newline";
        using var connection = new SqlConnection();
        using var command = CiDatabase.CreateScriptBatchCommand(connection, batch, guardTransactions: true);

        Assert.Equal("SET XACT_ABORT ON;\nBEGIN TRY\n" + batch +
            "\nEND TRY\nBEGIN CATCH\nIF XACT_STATE() <> 0 ROLLBACK TRANSACTION;\nTHROW;\nEND CATCH;",
            command.CommandText);
        Assert.Contains(batch, command.CommandText); // Includes the original CRLF inside the literal.
        Assert.DoesNotContain("sp_executesql", command.CommandText);
        Assert.Empty(command.Parameters.Cast<SqlParameter>());
        Assert.Equal(60, command.CommandTimeout);
    }

    [Fact]
    public void ClientCleanupOnlyRollsBackAnOpenTransactionAndNeverCommits()
    {
        Assert.Equal("IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;", CiDatabase.RollbackOpenTransactionSql);
    }

    [Fact]
    public void RawArtifactCommandStillExecutesOriginalBatchWithoutAWrapper()
    {
        const string batch = "SELECT N'unchanged raw artifact';";
        using var connection = new SqlConnection();
        using var command = CiDatabase.CreateScriptBatchCommand(connection, batch, guardTransactions: false);

        Assert.Equal(batch, command.CommandText);
        Assert.Empty(command.Parameters.Cast<SqlParameter>());
    }
}
