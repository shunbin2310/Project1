using Microsoft.Data.SqlClient;
using Project1.MigrationExecutor;

namespace Project1.Migrations.Tests;

public sealed class MigrationExecutionSessionGuardTests
{
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
