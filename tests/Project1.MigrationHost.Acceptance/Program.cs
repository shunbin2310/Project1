// CI FIXTURE ONLY. Never install this provisioner or run it on a real server.
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Project1.Api.Data;
using Project1.MigrationExecutor;
using Project1.MigrationHost.Acceptance;

var diagnostics = new CiFixtureDiagnostics(Console.Error);
return await diagnostics.RunAsync(async () =>
{
    diagnostics.Begin(FixtureStage.Guard);
    if (!OperatingSystem.IsLinux() || Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true" ||
        Environment.GetEnvironmentVariable("PROJECT1_HOST_ACCEPTANCE") != "disposable-github-runner-v1" ||
        Environment.GetEnvironmentVariable("GITHUB_REPOSITORY") != "shunbin2310/Project1" ||
        Environment.GetEnvironmentVariable("GITHUB_JOB") != "migration-host-acceptance" ||
        Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") != "github-hosted" ||
        args.Length != 1 || args[0] is not ("initialize" or "before" or "after"))
        throw new InvalidOperationException("CI acceptance fixture refused.");

    const string before = "20260930150622_AddEmailAttachments";
    const string note = "20261009164508_AddProductExampleAttr";
    const string backup = "/var/opt/mssql/backup/project1-host-acceptance.bak";
    using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(4));
    var token = deadline.Token;
    var builder = new SqlConnectionStringBuilder
    {
        DataSource = "tcp:127.0.0.1,1433",
        InitialCatalog = "master",
        UserID = "sa",
        // Public disposable container credential, not a production secret.
        Password = "Project1CiOnly-Tests!2026",
        Encrypt = true,
        TrustServerCertificate = true,
        Pooling = false,
        ConnectRetryCount = 0,
        ConnectTimeout = 5
    };
    await using var admin = new SqlConnection(builder.ConnectionString);
    diagnostics.Begin(FixtureStage.Connect);
    for (var attempt = 0; ; attempt++)
    {
        try { await admin.OpenAsync(token); break; }
        catch (SqlException) when (attempt < 30) { await Task.Delay(2000, token); }
    }
    async Task<object?> Query(string sql)
    {
        await using var command = admin.CreateCommand();
        command.CommandTimeout = 120;
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(token);
    }
    diagnostics.Begin(FixtureStage.Identity);
    if (Convert.ToString(await Query("SELECT @@SERVERNAME;")) != "homelab-server" ||
        Convert.ToInt32(await Query("SELECT CONVERT(int, SERVERPROPERTY('ProductMajorVersion'));")) != 17)
        throw new InvalidOperationException("Wrong disposable SQL identity.");

    if (args[0] == "initialize")
    {
        // Credentials arrive on stdin; never in arguments, environment or output.
        diagnostics.Begin(FixtureStage.Credentials);
        using var credentials = JsonDocument.Parse(await Console.In.ReadLineAsync(token) ?? "");
        string Password(string name)
        {
            var value = credentials.RootElement.GetProperty(name).GetString() ?? "";
            if (!Regex.IsMatch(value, @"\AP1![0-9a-f]{64}\z")) throw new InvalidOperationException();
            return value;
        }
        var execution = Password("execution_password");
        var verifier = Password("verifier_password");
        diagnostics.Begin(FixtureStage.ExistingDatabase);
        if (execution == verifier || await Query("SELECT DB_ID(N'Project1Db');") is not DBNull)
            throw new InvalidOperationException("Refuse existing database or shared credentials.");
        diagnostics.Begin(FixtureStage.CreateDatabase);
        await Query("CREATE DATABASE Project1Db;");
        builder.InitialCatalog = "Project1Db";
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(builder.ConnectionString).Options);
        diagnostics.Begin(FixtureStage.MigrationProfile);
        var ids = context.Database.GetMigrations().ToArray();
        if (ids.LastOrDefault() != note || !ids.Contains(before)) throw new InvalidOperationException("Review new migration profile first.");
        diagnostics.Begin(FixtureStage.MigrateBaseline);
        await context.GetService<IMigrator>().MigrateAsync(before, token);
        await admin.ChangeDatabaseAsync("Project1Db", token);
        diagnostics.Begin(FixtureStage.SeedData);
        await Query("""
        INSERT INTO dbo.ProductCategories (Code, Name, IsActive, CreatedAtUtc)
        VALUES (N'HOST-CATEGORY', N'Host fixture category', 1, '2026-01-01T00:00:00+00:00');
        DECLARE @category int = CONVERT(int, SCOPE_IDENTITY());
        INSERT INTO dbo.UnitsOfMeasure (Code, Name, IsActive, CreatedAtUtc)
        VALUES (N'HOST-UNIT', N'Host fixture unit', 1, '2026-01-01T00:00:00+00:00');
        DECLARE @unit int = CONVERT(int, SCOPE_IDENTITY());
        INSERT INTO dbo.Products (Code, Name, Description, ProductCategoryId, UnitOfMeasureId,
            DefaultUnitPrice, ReorderLevel, IsActive, CreatedAtUtc)
        VALUES (N'HOST-PRODUCT', N'Preserved host product', N'Preserved description',
            @category, @unit, 12.50, 3, 1, '2026-01-01T00:00:00+00:00');
        """);
        diagnostics.Begin(FixtureStage.ExecutionAccount);
        await Query($"""
        CREATE LOGIN project1_execute WITH PASSWORD = '{execution}', CHECK_POLICY = ON, DEFAULT_DATABASE = Project1Db;
        CREATE USER project1_execute FOR LOGIN project1_execute;
        GRANT CONNECT TO project1_execute;
        GRANT ALTER ON OBJECT::dbo.Products TO project1_execute;
        GRANT SELECT, INSERT ON OBJECT::dbo.__EFMigrationsHistory TO project1_execute;
        """);
        diagnostics.Begin(FixtureStage.VerifierAndBackup);
        await admin.ChangeDatabaseAsync("master", token);
        await Query($"""
        CREATE LOGIN project1_backup_verify WITH PASSWORD = '{verifier}', CHECK_POLICY = ON, DEFAULT_DATABASE = master;
        CREATE USER project1_backup_verify FOR LOGIN project1_backup_verify;
        GRANT CONNECT TO project1_backup_verify;
        GRANT CREATE DATABASE TO project1_backup_verify;
        BACKUP DATABASE Project1Db TO DISK = N'{backup}' WITH COPY_ONLY, CHECKSUM, INIT;
        """);
        diagnostics.Begin(FixtureStage.VerifyBackup);
        var finished = await BackupInspector.VerifyAsync(admin, backup, "homelab-server", "Project1Db", token);
        diagnostics.Begin(FixtureStage.GenerateSql);
        var sql = context.GetService<IMigrator>().GenerateScript("0", null, MigrationsSqlGenerationOptions.Idempotent);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            migrations = ids,
            sql,
            completed_at = finished.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'")
        }));
    }
    else
    {
        diagnostics.Begin(FixtureStage.VerifyData);
        await admin.ChangeDatabaseAsync("Project1Db", token);
        var expected = args[0] == "after" ? 1 : 0;
        if (Convert.ToInt32(await Query("SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.Products') AND name=N'Note';")) != expected ||
            Convert.ToInt32(await Query($"SELECT COUNT(*) FROM dbo.__EFMigrationsHistory WHERE MigrationId=N'{note}';")) != expected ||
            Convert.ToInt32(await Query("SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.Products') AND name=N'CiUncommitted';")) != 0 ||
            Convert.ToInt32(await Query("SELECT COUNT(*) FROM dbo.Products;")) != 1 ||
            Convert.ToInt32(await Query("""
            SELECT COUNT(*) FROM dbo.Products WHERE Code=N'HOST-PRODUCT' AND Name=N'Preserved host product'
            AND Description=N'Preserved description' AND DefaultUnitPrice=12.50 AND ReorderLevel=3
            AND IsActive=1 AND CreatedAtUtc='2026-01-01T00:00:00+00:00';
            """)) != 1)
            throw new InvalidOperationException("Fixture schema/history/data check failed.");
        Console.WriteLine("{\"verified\":true}");
    }
    diagnostics.Begin(FixtureStage.Complete);
});
