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
}
