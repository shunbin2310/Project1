using Microsoft.Data.SqlClient;
using Project1.MigrationExecutor;

namespace Project1.Migrations.Tests;

public sealed class MigrationExecutionSessionGuardTests
{
    [Fact]
    public void BackupFinishUsesOriginalUtcHeaderAndRefusesUnknownOrLocalOffsets()
    {
        var time = new DateTime(2026, 10, 10, 1, 2, 3, DateTimeKind.Unspecified);
        Assert.Equal(DateTimeKind.Utc, BackupInspector.ReadUtcFinish(time, 0).Kind);
        Assert.Equal(time.Ticks, BackupInspector.ReadUtcFinish(time, "UTC").Ticks);
        foreach (var zone in new object[] { DBNull.Value, 127, 8, "+08:00", "unknown" })
            Assert.Throws<InvalidOperationException>(() => BackupInspector.ReadUtcFinish(time, zone));
    }
    [Fact]
    public void ProductionConnectionPinsExecutionIdentityWithoutChangingReadonlyOrCiAccounts()
    {
        var value = new SqlConnectionStringBuilder(SqlMigrationSession.ProductionConnectionString("P1!" + new string('a', 64)));
        Assert.Equal("tcp:127.0.0.1,1433", value.DataSource);
        Assert.Equal("Project1Db", value.InitialCatalog);
        Assert.Equal("project1_execute", value.UserID);
        Assert.False(value.Pooling);
        Assert.False(value.IntegratedSecurity);
        Assert.Equal(0, value.ConnectRetryCount);
        Assert.Equal(15, value.ConnectTimeout);
        Assert.Throws<InvalidOperationException>(() => SqlMigrationSession.ProductionConnectionString("password;User ID=sa"));
        Assert.Throws<InvalidOperationException>(() => SqlMigrationSession.ProductionConnectionString("P1!" + new string('a', 64) + "\n"));
    }
    [Fact]
    public void SqlEvidenceClockUsesSixDigitUtcPrecisionAcceptedByThePythonValidator()
    {
        var text = CiExecutionBackup.UtcText(DateTimeOffset.UtcNow);
        Assert.Matches(@"\A[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}\.[0-9]{6}Z\z", text);
    }

    [Fact]
    public void ExecutorConnectionIsOwnedCiOnlyUnpooledAndCannotReconnectWithoutItsSessionLock()
    {
        var database = $"Project1CiMigration_{Guid.NewGuid():N}_upgrade";
        var login = $"Project1CiExecutor_{Guid.NewGuid():N}";
        var value = new SqlConnectionStringBuilder(SqlMigrationSession.CiConnectionString(database, login, "ci-only", _ => "true"));
        Assert.Equal("tcp:127.0.0.1,14333", value.DataSource);
        Assert.Equal(database, value.InitialCatalog);
        Assert.Equal(login, value.UserID);
        Assert.Equal(15, value.ConnectTimeout);
        Assert.Equal(0, value.ConnectRetryCount);
        Assert.False(value.Pooling);
        Assert.False(value.IntegratedSecurity);
        Assert.Throws<InvalidOperationException>(() => SqlMigrationSession.CiConnectionString("Project1Db", login, "ci-only", _ => "true"));
        Assert.Throws<InvalidOperationException>(() => SqlMigrationSession.CiConnectionString(database, "sa", "ci-only", _ => "true"));
        Assert.Throws<InvalidOperationException>(() => SqlMigrationSession.CiConnectionString(database, login, "ci-only", _ => "false"));
    }
}
