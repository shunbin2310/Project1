using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Project1.MigrationExecutor;

internal static class ProductionBackup
{
    public static async Task<object> VerifyAsync(string digest, string password, CancellationToken token)
    {
        if (!Regex.IsMatch(digest, @"\A[0-9a-f]{64}\z")) throw new InvalidOperationException();
        var path = "/var/lib/project1-migration-backups/" + digest + ".bak";
        var connectionString = new SqlConnectionStringBuilder
        {
            DataSource = "tcp:127.0.0.1,1433", InitialCatalog = "master",
            UserID = "project1_backup_verify", Password = password,
            Encrypt = true, TrustServerCertificate = true, Pooling = false,
            ConnectRetryCount = 0, ConnectTimeout = 15
        }.ConnectionString;
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(token);
        await using var identity = connection.CreateCommand();
        identity.CommandTimeout = 15;
        identity.CommandText = """
            SELECT CONVERT(nvarchar(128), @@SERVERNAME), DB_NAME(), ORIGINAL_LOGIN(),
                CONVERT(int, SERVERPROPERTY('ProductMajorVersion')),
                DATEPART(TZOFFSET, SYSDATETIMEOFFSET()),
                IS_SRVROLEMEMBER('sysadmin'), IS_SRVROLEMEMBER('securityadmin'),
                IS_SRVROLEMEMBER('dbcreator'), IS_ROLEMEMBER('db_owner'),
                HAS_PERMS_BY_NAME(NULL, NULL, 'CONTROL SERVER'),
                HAS_PERMS_BY_NAME(NULL, NULL, 'ALTER ANY LOGIN'),
                HAS_PERMS_BY_NAME(NULL, NULL, 'ALTER ANY DATABASE'),
                HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'CREATE DATABASE');
            """;
        await using (var reader = await identity.ExecuteReaderAsync(token))
        {
            if (!await reader.ReadAsync(token) || reader.GetString(0) != "homelab-server" ||
                reader.GetString(1) != "master" || reader.GetString(2) != "project1_backup_verify" ||
                reader.GetInt32(3) != 17 || reader.GetInt32(4) != 0) throw new InvalidOperationException();
            for (var i = 5; i < reader.FieldCount; i++)
                if (reader.IsDBNull(i) || reader.GetInt32(i) != (i == 12 ? 1 : 0))
                    throw new InvalidOperationException("Unexpected verifier privileges.");
        }
        var finished = await BackupInspector.VerifyAsync(connection, path, "homelab-server", "Project1Db", token);
        return new { server = "homelab-server", database = "Project1Db", sha256 = digest,
            // Host requires UTC server clock. Header time is independently read, not supplied by approval.
            completed_at = DateTime.SpecifyKind(finished, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'"),
            checked_at = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'"),
            checksum_valid = true, restore_verified = true, copy_only = true };
    }
}
