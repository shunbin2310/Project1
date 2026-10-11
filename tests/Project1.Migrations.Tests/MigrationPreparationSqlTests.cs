using Microsoft.Data.SqlClient;

namespace Project1.Migrations.Tests;

// Same disposable container/identity guard as existing migration tests. No Ubuntu access.
public sealed class MigrationPreparationSqlTests
{
    [SqlServerCiFact]
    public async Task SignedBackupProcedure_VerifiesRealBackup_WithoutGivingLoginDdlPermissions()
    {
        await using var database = await CiDatabase.CreateAsync("fresh");
        var settings = CiSqlServerSettings.Load(Environment.GetEnvironmentVariable);
        await using var admin = new SqlConnection(settings.ConnectionString("master"));
        await admin.OpenAsync();
        await CiDatabase.ValidateServerAsync(admin);
        var token = Guid.NewGuid().ToString("N");
        var login = "Project1CiBackup_" + token;
        var certificate = "Project1CiBackupCert_" + token;
        var procedure = "Project1CiBackupProc_" + token;
        var password = "P1!" + Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Project1.slnx")))
            root = root.Parent;
        Assert.NotNull(root);
        var template = await File.ReadAllTextAsync(Path.Combine(root.FullName, "scripts", "deploy", "install_migration_prepare.sql"));
        // Only this guarded CI fixture substitutes identities/paths. Production accepts no overrides.
        var script = template.Replace("homelab-server", CiSqlServerSettings.ServerName)
            .Replace("Project1Db", database.Name)
            .Replace("project1_backup", login)
            .Replace("Project1PrepareBackupCert", certificate)
            .Replace("project1_prepare_backup", procedure)
            .Replace("__BACKUP_PASSWORD__", password)
            .Replace("__SIGNING_PASSWORD__", "P1!" + Guid.NewGuid().ToString("N"))
            .Replace("N'/var/opt/mssql/backup/project1-prepare-' + @Token + N'/" + database.Name + ".bak'",
                     "N'/var/opt/mssql/data/Project1CiPrepare-' + @Token + N'.bak'");
        Assert.DoesNotContain("/var/opt/mssql/backup/project1-prepare-", script);
        try
        {
            await ExecuteAsync(admin, $"CREATE TABLE [{database.Name}].dbo.KeepMe (Id int NOT NULL); INSERT [{database.Name}].dbo.KeepMe VALUES (7);");
            foreach (var batch in SqlBatchParser.SplitBatches(script))
                await ExecuteAsync(admin, batch);
            var connectionString = new SqlConnectionStringBuilder(settings.ConnectionString("master"))
            {
                UserID = login,
                Password = password
            }.ConnectionString;
            await using var limited = new SqlConnection(connectionString);
            await limited.OpenAsync();
            Assert.Equal(0, Convert.ToInt32(await ScalarAsync(limited, "SELECT IS_SRVROLEMEMBER('sysadmin');")));
            Assert.Equal(0, Convert.ToInt32(await ScalarAsync(limited, "SELECT HAS_PERMS_BY_NAME(NULL, 'SERVER', 'CREATE ANY DATABASE');")));
            await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(limited,
                $"BACKUP DATABASE [{database.Name}] TO DISK=N'/var/opt/mssql/data/unauthorized-{token}.bak';"));
            await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(limited,
                $"ALTER TABLE [{database.Name}].dbo.KeepMe ADD ShouldNotExist int NULL;"));
            await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(limited,
                $"EXEC dbo.[{procedure}] @Token=N'../escape';"));
            Assert.Equal("PROJECT1_PREPARE_BACKUP_V1", await ScalarAsync(limited,
                $"EXEC dbo.[{procedure}] @Token=N'{token}';"));
            Assert.Equal(0, Convert.ToInt32(await ScalarAsync(limited, "SELECT HAS_PERMS_BY_NAME(NULL, 'SERVER', 'CREATE ANY DATABASE');")));
            Assert.Equal(7, Convert.ToInt32(await ScalarAsync(admin, $"SELECT Id FROM [{database.Name}].dbo.KeepMe;")));
            Assert.Equal(1, Convert.ToInt32(await ScalarAsync(admin, $"""
                SELECT COUNT(*) FROM msdb.dbo.backupset s
                JOIN msdb.dbo.backupmediafamily f ON s.media_set_id=f.media_set_id
                WHERE s.database_name=N'{database.Name}' AND s.type='D'
                  AND s.is_copy_only=1 AND s.has_backup_checksums=1
                  AND f.physical_device_name=N'/var/opt/mssql/data/Project1CiPrepare-{token}.bak';
                """)));
        }
        finally
        {
            // Validated CI identity and generated names only. Files vanish with the container.
            await ExecuteAsync(admin, $"""
                DROP PROCEDURE IF EXISTS dbo.[{procedure}];
                IF USER_ID(N'{login}') IS NOT NULL DROP USER [{login}];
                IF SUSER_ID(N'{login}') IS NOT NULL DROP LOGIN [{login}];
                IF SUSER_ID(N'{certificate}Login') IS NOT NULL DROP LOGIN [{certificate}Login];
                IF CERT_ID(N'{certificate}') IS NOT NULL DROP CERTIFICATE [{certificate}];
                """);
        }
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 60;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(SqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 60;
        return await command.ExecuteScalarAsync();
    }
}
