using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Project1.Api.Data;

namespace Project1.Migrations.Tests;

// A deliberately limited reader for EF-generated SQL Server scripts, not a general sqlcmd client.
internal static class MigrationSqlScript
{
    internal static IReadOnlyList<string> ReadFromCiWorkspace()
    {
        _ = CiSqlServerSettings.Load(Environment.GetEnvironmentVariable);
        var workspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        if (string.IsNullOrWhiteSpace(workspace) || !Path.IsPathFullyQualified(workspace))
        {
            throw new InvalidOperationException("The GitHub workspace is required to read the generated SQL.");
        }

        // No arbitrary SQL path, application configuration, or production connection string.
        var path = Path.Combine(workspace, "artifacts", "migrations", "migrations.sql");
        var file = new FileInfo(path);
        if (!file.Exists || file.Length is <= 0 or > 10 * 1024 * 1024)
        {
            throw new InvalidOperationException("The generated migration SQL is missing, empty, or too large.");
        }

        var manifestFile = new FileInfo(Path.Combine(workspace, "artifacts", "migrations", "migration-manifest.json"));
        if (!manifestFile.Exists || manifestFile.Length is <= 0 or > 160000)
        {
            throw new InvalidOperationException("The migration manifest is missing, empty, or too large.");
        }

        ValidateManifest(File.ReadAllText(manifestFile.FullName, Encoding.UTF8));
        return SplitBatches(File.ReadAllText(path, Encoding.UTF8));
    }

    internal static void ValidateManifest(string json)
    {
        using var manifest = JsonDocument.Parse(json);
        var root = manifest.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 2 ||
            root.GetProperty("schema").GetInt32() != 1)
        {
            throw new InvalidOperationException("Invalid migration manifest schema.");
        }

        var ids = root.GetProperty("migrations").EnumerateArray().Select(item => item.GetString()).ToArray();
        using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=tcp:127.0.0.1,1;Database=CiManifestOnly;User Id=ci;Password=not-a-real-password;Connect Timeout=1")
            .Options);
        if (!ids.SequenceEqual(context.Database.GetMigrations()))
        {
            throw new InvalidOperationException("The migration manifest does not match this compiled CI version.");
        }
    }

    internal static IReadOnlyList<string> SplitBatches(string sql) =>
        Project1.MigrationExecutor.SqlBatchParser.SplitBatches(sql);
}
