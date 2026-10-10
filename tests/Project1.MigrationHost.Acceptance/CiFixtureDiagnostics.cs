// CI ONLY. Shared with portable tests as source, never with the production host.
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace Project1.MigrationHost.Acceptance;

internal enum FixtureStage
{
    Guard, Connect, Identity, Credentials, ExistingDatabase, CreateDatabase,
    MigrationProfile, MigrateBaseline, SeedData, ExecutionAccount, VerifierAndBackup,
    VerifyBackup, GenerateSql, VerifyData, Complete
}

internal sealed class CiFixtureDiagnostics(TextWriter output)
{
    private FixtureStage stage = FixtureStage.Guard;

    internal void Begin(FixtureStage next)
    {
        if (!Enum.IsDefined(next)) throw new ArgumentOutOfRangeException(nameof(next));
        stage = next;
        Write("event=begin");
    }

    internal async Task<int> RunAsync(Func<Task> action)
    {
        try
        {
            await action();
            return 0;
        }
        catch (Exception error)
        {
            // Never render a message, type name, stack, Data, SQL or credentials.
            // Unwrap only a bounded inner chain to retain SqlClient numeric errors.
            Exception? current = error;
            for (var depth = 0; current is not null && depth < 4; depth++, current = current.InnerException)
            {
                if (current is not SqlException sql) continue;
                foreach (SqlError item in sql.Errors.Cast<SqlError>().Take(8)) ReportSqlNumber(item.Number);
                return 1;
            }
            var category = error switch
            {
                OperationCanceledException => "cancelled",
                TimeoutException => "timeout",
                InvalidOperationException => "invalid-operation",
                JsonException or FormatException or ArgumentException or KeyNotFoundException => "invalid-input",
                IOException => "io",
                _ => "unexpected"
            };
            Write($"event=error category={category}");
            return 1;
        }
    }

    internal void ReportSqlNumber(int number) =>
        Write("event=error category=sql number=" + number.ToString(CultureInfo.InvariantCulture));

    private void Write(string fields)
    {
        output.WriteLine("CI-FIXTURE stage=F" + ((int)stage).ToString("D2", CultureInfo.InvariantCulture) + " " + fields);
        output.Flush();
    }
}
