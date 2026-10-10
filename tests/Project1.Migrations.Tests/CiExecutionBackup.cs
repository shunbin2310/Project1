using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Project1.MigrationExecutor;

namespace Project1.Migrations.Tests;

// Independent ADMIN fixture, never an executor privilege or a production adapter.
// BACKUP/header/VERIFYONLY/hash operate only in this job's disposable SQL container.
internal sealed class CiExecutionBackup(CiDatabase database)
{
    private readonly string path = $"/var/opt/mssql/data/{database.Name}_executor.bak";
    internal string Digest { get; private set; } = "";
    internal DateTimeOffset CompletedAt { get; private set; }

    internal static string UtcText(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture);

    private static string Container()
    {
        _ = CiSqlServerSettings.Load(Environment.GetEnvironmentVariable);
        var value = Environment.GetEnvironmentVariable("PROJECT1_CI_SQLSERVER_CONTAINER");
        if (!OperatingSystem.IsLinux() || value is null || !Regex.IsMatch(value, @"\A[0-9a-f]{12,64}\z"))
            throw new InvalidOperationException("An isolated Linux CI container ID is required.");
        return value;
    }

    private async Task<string> HashAsync()
    {
        CiSqlServerSettings.ValidateDatabaseName(database.Name);
        var start = new ProcessStartInfo("/usr/bin/docker")
        {
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true
        };
        foreach (var argument in new[] { "exec", Container(), "sha256sum", path })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("CI hash process did not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { process.Kill(entireProcessTree: true); throw; }
        _ = await errors; // Never echo arbitrary container output.
        var parts = (await output).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (process.ExitCode != 0 || parts.Length != 2 || parts[1] != path ||
            !Regex.IsMatch(parts[0], @"\A[0-9a-f]{64}\z"))
            throw new InvalidOperationException("CI backup file hashing failed.");
        return parts[0];
    }

    internal async Task CreateAsync()
    {
        _ = Container();
        CiSqlServerSettings.ValidateDatabaseName(database.Name);
        await using var connection = database.CreateConnection();
        await connection.OpenAsync();
        await CiDatabase.ValidateServerAsync(connection);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 60;
        command.CommandText = $"BACKUP DATABASE [{database.Name}] TO DISK = @path WITH COPY_ONLY, CHECKSUM, NO_COMPRESSION, STOP_ON_ERROR;";
        command.Parameters.Add("@path", SqlDbType.NVarChar, 4000).Value = path;
        await command.ExecuteNonQueryAsync();
        CompletedAt = DateTimeOffset.UtcNow;
        Digest = await HashAsync();
        _ = await VerifyAsync();
    }

    internal async Task<object> VerifyAsync()
    {
        if (Digest.Length != 64 || await HashAsync() != Digest)
            throw new InvalidOperationException("CI backup file changed before verification.");
        await using var connection = database.CreateConnection();
        await connection.OpenAsync();
        await CiDatabase.ValidateServerAsync(connection);
        // Exercise the SAME SQL header/VERIFYONLY implementation as the uninstalled host.
        _ = await BackupInspector.VerifyAsync(connection, path, CiSqlServerSettings.ServerName,
            database.Name, CancellationToken.None);
        if (await HashAsync() != Digest) throw new InvalidOperationException("CI backup changed during verification.");
        // The fixture observes completion time; it does not infer UTC from a local header timestamp.
        return new { server = CiSqlServerSettings.ServerName, database = database.Name, sha256 = Digest,
            completed_at = UtcText(CompletedAt), checked_at = UtcText(DateTimeOffset.UtcNow),
            checksum_valid = true, restore_verified = true, copy_only = true };
    }
}
