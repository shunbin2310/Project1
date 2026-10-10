using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Project1.Api.Data;

namespace Project1.Migrations.Tests;

public sealed class MigrationSqlScriptTests
{
    [Fact]
    public void ManifestMatchesCompiledMigrationsWithoutDatabaseAccess()
    {
        using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=tcp:127.0.0.1,1;Database=CiManifestTest;User Id=ci;Password=not-a-real-password;Connect Timeout=1")
            .Options);
        var ids = context.Database.GetMigrations().ToArray();
        MigrationSqlScript.ValidateManifest(JsonSerializer.Serialize(new { schema = 1, migrations = ids }));
        Assert.Throws<InvalidOperationException>(() => MigrationSqlScript.ValidateManifest(
            JsonSerializer.Serialize(new { schema = 1, migrations = ids.Skip(1).ToArray() })));
    }

    [Theory]
    [InlineData("{\"schema\":2,\"migrations\":[]}")]
    [InlineData("{\"schema\":1,\"migrations\":[],\"extra\":true}")]
    [InlineData("{\"schema\":1,\"schema\":1,\"migrations\":[]}")]
    [InlineData("{\"schema\":1,\"migrations\":[]}")]
    public void WrongManifestSchemaOrMigrationListIsRejected(string json)
    {
        Assert.Throws<InvalidOperationException>(() => MigrationSqlScript.ValidateManifest(json));
    }

    [Theory]
    [InlineData("GO")]
    [InlineData(" go ")]
    [InlineData("Go -- next batch")]
    public void StandaloneGoSeparatesBatches(string separator)
    {
        Assert.Equal(new[] { "SELECT 1;", "SELECT 2;" },
            MigrationSqlScript.SplitBatches($"SELECT 1;\r\n{separator}\r\nSELECT 2;\r\nGO\r\n"));
    }

    [Theory]
    [InlineData("SELECT N'first\nGO\nlast';")]
    [InlineData("SELECT N'escaped '' quote\nGO\nlast';")]
    [InlineData("SELECT \"first\nGO\nlast\";")]
    [InlineData("SELECT \"escaped \"\" quote\nGO\nlast\";")]
    [InlineData("SELECT [first\nGO\nlast];")]
    [InlineData("SELECT [escaped ]] bracket\nGO\nlast];")]
    [InlineData("/* first\nGO\nlast */ SELECT 1;")]
    [InlineData("/* outer /* nested */\nGO\nend */ SELECT 1;")]
    [InlineData("-- GO and ' /* [ do not change state\nSELECT 1;")]
    public void GoInsideLiteralsIdentifiersAndCommentsIsNotASeparator(string batch)
    {
        var result = MigrationSqlScript.SplitBatches(batch + "\nGO\nSELECT 2;\nGO\n");
        Assert.Equal(2, result.Count);
        Assert.Equal(batch, result[0].Replace("\r\n", "\n"));
        Assert.Equal("SELECT 2;", result[1]);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public void NewlinesInsideSqlLiteralsArePreservedExactly(string newline)
    {
        var batch = $"SELECT N'first{newline}GO{newline}last';";
        var result = MigrationSqlScript.SplitBatches(batch + newline + "GO" + newline);
        Assert.Equal(new[] { batch }, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \r\nGO\nGO\n")]
    [InlineData("SELECT 'unterminated")]
    [InlineData("SELECT [unterminated")]
    [InlineData("SELECT \"unterminated")]
    [InlineData("/* unterminated")]
    [InlineData("SELECT 1;\nGO 2")]
    [InlineData("SELECT 1;\nGO;")]
    [InlineData(":connect localhost")]
    [InlineData(":r other.sql")]
    [InlineData("!! echo command")]
    public void EmptyMalformedOrUnsupportedScriptsAreRejected(string sql)
    {
        Assert.Throws<InvalidOperationException>(() => MigrationSqlScript.SplitBatches(sql));
    }

    [Fact]
    public void BomAndEmptyBatchesDoNotChangeSql()
    {
        Assert.Equal(new[] { "SELECT 1;" }, MigrationSqlScript.SplitBatches("\uFEFFGO\nSELECT 1;\nGO\nGO\n"));
    }

    [Fact]
    public void CurrentEfIdempotentScriptCanBeSplitWithoutDatabaseAccess()
    {
        // Pure generation/parsing check only; the CI integration cases read the uploaded file.
        using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=tcp:127.0.0.1,1;Database=CiParserOnly;User Id=ci;Password=not-a-real-password;Connect Timeout=1")
            .Options);
        var script = context.GetService<IMigrator>().GenerateScript("0", options: MigrationsSqlGenerationOptions.Idempotent);
        var batches = MigrationSqlScript.SplitBatches(script);
        Assert.NotEmpty(batches);
        Assert.Contains(batches, batch => batch.Contains("ALTER TABLE [Products] ADD [Note]", StringComparison.Ordinal));
        var reconstructed = string.Join("\n", batches);
        foreach (var migration in context.Database.GetMigrations())
        {
            Assert.Contains(migration, reconstructed, StringComparison.Ordinal);
        }
    }
}
