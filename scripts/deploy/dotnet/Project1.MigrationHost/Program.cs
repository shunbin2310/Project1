using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Project1.MigrationExecutor;

// Private stdio child, NOT a sudo command. No SQL/credentials/paths in argv or env.
internal static class Program
{
    [DllImport("libc")]
    private static extern uint geteuid();

    private static async Task<string> ReadLineAsync(CancellationToken token)
    {
        var text = new StringBuilder();
        var buffer = new char[1];
        while (await Console.In.ReadAsync(buffer.AsMemory(), token) != 0)
        {
            if (buffer[0] == '\n') return text.ToString();
            if (text.Length >= 15 * 1024 * 1024) throw new InvalidOperationException();
            text.Append(buffer[0]);
        }
        throw new EndOfStreamException();
    }

    private static async Task<int> Main(string[] args)
    {
        if (!OperatingSystem.IsLinux() || geteuid() != 0 || args.Length != 0) return 1;
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        SqlMigrationSession? session = null;
        try
        {
            using var initial = JsonDocument.Parse(await ReadLineAsync(deadline.Token));
            var password = initial.RootElement.GetProperty("execution_password").GetString()!;
            var verifierPassword = initial.RootElement.GetProperty("verifier_password").GetString()!;
            if (!Regex.IsMatch(verifierPassword, @"\AP1![0-9a-f]{64}\z")) throw new InvalidOperationException();
            session = await SqlMigrationSession.OpenProductionAsync(password);
            Write(new { ok = true });
            while (true)
            {
                using var request = JsonDocument.Parse(await ReadLineAsync(deadline.Token));
                var value = request.RootElement;
                switch (value.GetProperty("operation").GetString())
                {
                    case "status":
                        Write(new { ok = true, value = new { schema = 1, server = "homelab-server",
                            database = "Project1Db", login = "project1_execute",
                            checked_at = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'"),
                            migrations = await session.HistoryAsync() } });
                        break;
                    case "before":
                        await session.AssertNoteSchemaAsync(false); Write(new { ok = true }); break;
                    case "after":
                        await session.AssertNoteSchemaAsync(true); Write(new { ok = true }); break;
                    case "apply":
                        await session.ApplyAsync(Convert.FromBase64String(value.GetProperty("sql").GetString()!),
                            value.GetProperty("sha256").GetString()!);
                        Write(new { ok = true }); break;
                    case "backup":
                        Write(new { ok = true, value = await ProductionBackup.VerifyAsync(
                            value.GetProperty("sha256").GetString()!, verifierPassword, deadline.Token) });
                        break;
                    case "close":
                        await session.DisposeAsync(); session = null; Write(new { ok = true }); return 0;
                    default: throw new InvalidOperationException();
                }
            }
        }
        catch
        {
            // No raw SQL errors, input, connection strings or secrets to stdout/stderr.
            Write(new { ok = false });
            return 1;
        }
        finally
        {
            if (session is not null)
            {
                try { await session.DisposeAsync(); } catch { /* Closing the process terminates session. */ }
            }
        }
    }

    private static void Write(object value) => Console.WriteLine(JsonSerializer.Serialize(value));
}
