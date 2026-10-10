using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Project1.MigrationExecutor;
using static Project1.Migrations.Tests.MigrationIntegrationTests;

namespace Project1.Migrations.Tests;

public sealed class MigrationExecutionWorkflowSqlTests
{
    [SqlServerCiFact]
    public Task Coordinator_ActualBackupScopedSqlAndDurableLedgerSucceedAndRefuseReplay() => WorkflowAsync(false);

    [SqlServerCiFact]
    public Task Coordinator_ActualSqlFailureRollsBackAndDurableFailureRefusesReplay() => WorkflowAsync(true);

    private static async Task WorkflowAsync(bool failure)
    {
        _ = MigrationSqlScript.ReadFromCiWorkspace(); // Includes compiled manifest validation.
        var workspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE")!;
        var sql = await File.ReadAllBytesAsync(Path.Combine(workspace, "artifacts", "migrations", "migrations.sql"));
        if (failure)
        {
            var text = new UTF8Encoding(false, true).GetString(sql);
            var commit = text.LastIndexOf("COMMIT;", StringComparison.Ordinal);
            Assert.True(commit >= 0);
            // Deliberate CI fault after the last upgrade's DDL/history but before its COMMIT.
            text = text.Insert(commit, "THROW 51055, N'CI workflow rollback test', 1;\n");
            sql = Encoding.UTF8.GetBytes(text);
        }
        await using var database = await CiDatabase.CreateAsync("upgrade");
        await using var context = database.CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(BeforeNote);
        var productId = await InsertPreMigrationProductAsync(database);
        var before = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        await using var account = await CiMigrationAccount.CreateAsync(database, CiMigrationPermissionProfile.ProductNoteExecutor);
        var backup = new CiExecutionBackup(database);
        await backup.CreateAsync();
        await using (var session = await account.CreateExecutionSessionAsync())
        {
            await RunPythonAsync(workspace, database, account, session, backup, sql,
                context.Database.GetMigrations().ToArray(), failure);
        }
        if (failure)
        {
            Assert.Equal(before, (await context.Database.GetAppliedMigrationsAsync()).ToArray());
            await using var observer = database.CreateConnection();
            await observer.OpenAsync();
            await using var command = observer.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Products') AND name = N'Note';";
            Assert.Equal(0, Convert.ToInt32(await command.ExecuteScalarAsync()));
        }
        else
        {
            await AssertAllMigrationsAppliedAsync(context);
            await AssertNoteColumnAsync(database);
            var product = await context.Products.SingleAsync();
            Assert.Equal(productId, product.Id);
            Assert.Equal("Existing description must survive", product.Description);
            Assert.Equal("Product created before Note", product.Name);
            Assert.Equal(12.50m, product.DefaultUnitPrice);
            Assert.Equal(3m, product.ReorderLevel);
            Assert.Null(product.Note);
        }
        await using var read = database.CreateConnection();
        await read.OpenAsync();
        await using var count = read.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM dbo.Products WHERE Id = @id AND Code = N'CI-PRODUCT';";
        count.Parameters.AddWithValue("@id", productId);
        Assert.Equal(1, Convert.ToInt32(await count.ExecuteScalarAsync()));
        // A fresh session can take the lock after cleanup; no transaction/lock was leaked.
        await using var reopened = await account.CreateExecutionSessionAsync();
        Assert.Equal(failure ? before : context.Database.GetMigrations().ToArray(), await reopened.HistoryAsync());
    }

    [SqlServerCiFact]
    public async Task Session_DifferentExecutorSessionCannotTakeTheHeldDatabaseLock()
    {
        await using var database = await CiDatabase.CreateAsync("upgrade");
        await using var context = database.CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(BeforeNote);
        await using var account = await CiMigrationAccount.CreateAsync(database, CiMigrationPermissionProfile.ProductNoteExecutor);
        await using (var held = await account.CreateExecutionSessionAsync())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => account.CreateExecutionSessionAsync());
            Assert.Equal((await context.Database.GetAppliedMigrationsAsync()).ToArray(), await held.HistoryAsync());
        }
        await using var acquired = await account.CreateExecutionSessionAsync();
        await acquired.AssertNoteSchemaAsync(after: false);
    }

    [SqlServerCiFact]
    public async Task Session_SchemaDriftAndWrongDigestAreRefusedBeforeSqlWrites()
    {
        await using var database = await CiDatabase.CreateAsync("upgrade");
        await using var context = database.CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(BeforeNote);
        var before = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        await using var account = await CiMigrationAccount.CreateAsync(database, CiMigrationPermissionProfile.ProductNoteExecutor);
        await using var session = await account.CreateExecutionSessionAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ApplyAsync(Encoding.UTF8.GetBytes("SELECT 1;"), new string('a', 64)));
        await using var admin = database.CreateConnection();
        await admin.OpenAsync();
        await using var command = admin.CreateCommand();
        command.CommandText = "ALTER TABLE dbo.Products ADD Note int NULL;";
        await command.ExecuteNonQueryAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.AssertNoteSchemaAsync(after: false));
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.AssertNoteSchemaAsync(after: true));
        Assert.Equal(before, await session.HistoryAsync());
    }

    [SqlServerCiFact]
    public async Task Session_CrossBatchRuntimeAndCompileErrorsRollbackAndCannotRetry()
    {
        foreach (var failingBatch in new[] { "THROW 51056, N'CI cross-batch error', 1;", "SELECT * FROM dbo.CiMissingExecutorObject;" })
        {
            await using var database = await CiDatabase.CreateAsync("upgrade");
            await using var context = database.CreateContext();
            await context.GetService<IMigrator>().MigrateAsync(BeforeNote);
            var before = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
            await using var account = await CiMigrationAccount.CreateAsync(database, CiMigrationPermissionProfile.ProductNoteExecutor);
            await using var session = await account.CreateExecutionSessionAsync();
            var sql = Encoding.UTF8.GetBytes($"""
                BEGIN TRANSACTION;
                GO
                ALTER TABLE dbo.Products ADD Note nvarchar(max) NULL;
                INSERT INTO dbo.__EFMigrationsHistory (MigrationId, ProductVersion)
                VALUES (N'{AddNote}', N'10.0.10');
                GO
                {failingBatch}
                GO
                COMMIT;
                GO
                """);
            var digest = Convert.ToHexStringLower(SHA256.HashData(sql));
            var error = await Assert.ThrowsAsync<SqlException>(() => session.ApplyAsync(sql, digest));
            var expected = failingBatch.StartsWith("THROW", StringComparison.Ordinal) ? 51056 : 208;
            Assert.Contains(error.Errors.Cast<SqlError>(), item => item.Number == expected);
            Assert.Equal(before, await session.HistoryAsync());
            await session.AssertNoteSchemaAsync(after: false);
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.ApplyAsync(sql, digest));
        }
    }

    [SqlServerCiFact]
    public async Task Session_TransactionAcrossGoBatchesCanCommitAndStillHoldTheLock()
    {
        await using var database = await CiDatabase.CreateAsync("upgrade");
        await using var context = database.CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(BeforeNote);
        await using var account = await CiMigrationAccount.CreateAsync(database, CiMigrationPermissionProfile.ProductNoteExecutor);
        await using var session = await account.CreateExecutionSessionAsync();
        var sql = Encoding.UTF8.GetBytes($"""
            BEGIN TRANSACTION;
            GO
            ALTER TABLE dbo.Products ADD Note nvarchar(max) NULL;
            GO
            INSERT INTO dbo.__EFMigrationsHistory (MigrationId, ProductVersion)
            VALUES (N'{AddNote}', N'10.0.10');
            GO
            COMMIT;
            GO
            """);
        await session.ApplyAsync(sql, Convert.ToHexStringLower(SHA256.HashData(sql)));
        Assert.Equal(context.Database.GetMigrations().ToArray(), await session.HistoryAsync());
        await session.AssertNoteSchemaAsync(after: true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => account.CreateExecutionSessionAsync());
    }

    private static async Task RunPythonAsync(string workspace, CiDatabase database, CiMigrationAccount account,
        SqlMigrationSession session, CiExecutionBackup backup, byte[] sql, string[] migrations, bool failure)
    {
        Assert.True(OperatingSystem.IsLinux());
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var start = new ProcessStartInfo("/usr/bin/python3")
        {
            WorkingDirectory = workspace, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add(Path.Combine(workspace, "scripts", "deploy", "tests", "ci_execution_workflow.py"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        using var process = Process.Start(start) ?? throw new InvalidOperationException("CI Python fixture did not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        var exit = process.WaitForExitAsync(timeout.Token);
        var diagnostics = new List<string>();
        var applied = 0;
        try
        {
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new
            {
                database = database.Name, login = account.Name, token,
                port = ((IPEndPoint)listener.LocalEndpoint).Port, sql = Convert.ToBase64String(sql), migrations,
                commit = Environment.GetEnvironmentVariable("GITHUB_SHA"),
                run_id = long.Parse(Environment.GetEnvironmentVariable("GITHUB_RUN_ID")!),
                run_attempt = int.Parse(Environment.GetEnvironmentVariable("GITHUB_RUN_ATTEMPT")!), failure,
                backup_sha256 = backup.Digest, backup_completed_at = CiExecutionBackup.UtcText(backup.CompletedAt)
            }));
            process.StandardInput.Close();
            var accept = listener.AcceptTcpClientAsync(timeout.Token).AsTask();
            Assert.True(await Task.WhenAny(accept, exit) == accept, "CI Python fixture exited before opening its test channel.");
            using var client = await accept;
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            while (true)
            {
                var line = await reader.ReadLineAsync(timeout.Token);
                Assert.NotNull(line);
                Assert.InRange(line.Length, 1, 16 * 1024 * 1024);
                using var request = JsonDocument.Parse(line);
                var root = request.RootElement;
                Assert.Equal(token, root.GetProperty("token").GetString());
                var operation = root.GetProperty("operation").GetString();
                object? value = null;
                var ok = true;
                try
                {
                    switch (operation)
                    {
                        case "lock":
                            _ = await session.HistoryAsync(); break;
                        case "status":
                            value = new { schema = 1, server = CiSqlServerSettings.ServerName, database = database.Name,
                                login = account.Name, checked_at = CiExecutionBackup.UtcText(DateTimeOffset.UtcNow), migrations = await session.HistoryAsync() };
                            break;
                        case "before": await session.AssertNoteSchemaAsync(after: false); break;
                        case "after": await session.AssertNoteSchemaAsync(after: true); break;
                        case "backup": value = await backup.VerifyAsync(); break;
                        case "provenance":
                            var manifest = root.GetProperty("manifest");
                            Assert.Equal(Environment.GetEnvironmentVariable("GITHUB_SHA"), manifest.GetProperty("commit").GetString());
                            Assert.Equal(Environment.GetEnvironmentVariable("GITHUB_RUN_ID"), manifest.GetProperty("run_id").ToString());
                            Assert.Equal(Environment.GetEnvironmentVariable("GITHUB_RUN_ATTEMPT"), manifest.GetProperty("run_attempt").ToString());
                            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(sql)), manifest.GetProperty("sql_sha256").GetString());
                            break;
                        case "apply":
                            applied++;
                            Assert.Equal(1, applied);
                            var received = Convert.FromBase64String(root.GetProperty("sql").GetString()!);
                            Assert.Equal(sql, received);
                            await session.ApplyAsync(received, root.GetProperty("sha256").GetString()!);
                            break;
                        case "done": break;
                        default: throw new InvalidOperationException("Unknown CI test operation.");
                    }
                }
                catch (Exception error)
                {
                    ok = false;
                    diagnostics.Add(operation + ":" + error.GetType().Name + (error is SqlException sqlError
                        ? ":" + string.Join(',', sqlError.Errors.Cast<SqlError>().Select(item => item.Number)) : ""));
                }
                await writer.WriteLineAsync(JsonSerializer.Serialize(new { ok, value }));
                if (operation == "done") break;
            }
            await exit;
            _ = await errors; // No raw Python exceptions/settings in logs.
            Assert.True(process.ExitCode == 0, "CI Python workflow failed; safe operation diagnostics: " + string.Join(';', diagnostics));
            using var result = JsonDocument.Parse((await output).Trim());
            Assert.Equal(failure ? "failed" : "succeeded", result.RootElement.GetProperty("phase").GetString());
            Assert.True(result.RootElement.GetProperty("replay_blocked").GetBoolean());
            Assert.Equal(1, applied);
            if (failure) Assert.Contains(diagnostics, item => item.StartsWith("apply:SqlException:") && item.Contains("51055"));
            else Assert.Empty(diagnostics);
        }
        finally
        {
            listener.Stop();
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }
}
