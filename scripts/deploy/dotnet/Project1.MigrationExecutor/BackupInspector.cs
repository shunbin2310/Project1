using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;

namespace Project1.MigrationExecutor;

// Trusted host/test fixture only. Never exposed as a deployment-user SQL API.
public static class BackupInspector
{
    public static DateTime ReadUtcFinish(object finish, object zone)
    {
        // Header's original clock must also be UTC; today's server offset is insufficient.
        var text = Convert.ToString(zone, CultureInfo.InvariantCulture);
        if (text is not ("0" or "UTC" or "Etc/UTC" or "+00:00" or "00:00"))
            throw new InvalidOperationException("Backup clock is not a verified UTC clock.");
        if (finish is not DateTime time) throw new InvalidOperationException("Invalid backup finish time.");
        return DateTime.SpecifyKind(time, DateTimeKind.Utc);
    }

    public static async Task<DateTime> VerifyAsync(SqlConnection connection, string path,
        string server, string database, CancellationToken token)
    {
        await using var header = connection.CreateCommand();
        header.CommandTimeout = 60;
        // Read ALL headers and reject appended/ambiguous media, not merely FILE=1.
        header.CommandText = "RESTORE HEADERONLY FROM DISK = @path WITH CHECKSUM, STOP_ON_ERROR;";
        header.Parameters.Add("@path", SqlDbType.NVarChar, 4000).Value = path;
        DateTime finished;
        await using (var reader = await header.ExecuteReaderAsync(token))
        {
            if (!await reader.ReadAsync(token) || !Equals(reader["ServerName"], server) ||
                !Equals(reader["DatabaseName"], database) || Convert.ToInt32(reader["BackupType"]) != 1 ||
                Convert.ToInt32(reader["Position"]) != 1 || !Convert.ToBoolean(reader["HasBackupChecksums"]) ||
                !Convert.ToBoolean(reader["IsCopyOnly"]) || Convert.ToBoolean(reader["IsDamaged"]))
                throw new InvalidOperationException("Backup header does not match reviewed policy.");
            finished = ReadUtcFinish(reader["BackupFinishDate"], reader["TimeZone"]);
            if (await reader.ReadAsync(token)) throw new InvalidOperationException("Multiple backup sets are refused.");
        }
        await using var verify = connection.CreateCommand();
        verify.CommandTimeout = 120;
        verify.CommandText = "RESTORE VERIFYONLY FROM DISK = @path WITH FILE = 1, CHECKSUM, STOP_ON_ERROR;";
        verify.Parameters.Add("@path", SqlDbType.NVarChar, 4000).Value = path;
        await verify.ExecuteNonQueryAsync(token);
        return finished;
    }
}
