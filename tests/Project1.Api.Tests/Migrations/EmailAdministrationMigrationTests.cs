using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Project1.Api.Data;

namespace Project1.Api.Tests.Migrations;

public sealed class EmailAdministrationMigrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GenerateScript_DefersBackfillCompilationUntilNewColumnsExist(bool idempotent)
    {
        // Generate SQL only: this test does not connect to a SQL Server database.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=localhost;Database=MigrationScriptTests;Integrated Security=True;TrustServerCertificate=True")
            .Options;
        using var context = new AppDbContext(options);
        var migrator = context.GetService<IMigrator>();

        var script = migrator.GenerateScript(
            fromMigration: "20260927150556_AddPurchaseOrderEmailOutbox",
            toMigration: "20260928141008_AddEmailAdministrationRecords",
            options: idempotent ? MigrationsSqlGenerationOptions.Idempotent : MigrationsSqlGenerationOptions.Default);

        Assert.Matches(@"EXEC\(N'\s*UPDATE email", script);
        Assert.Contains("SourceType = ''PurchaseOrder''", script);
        Assert.Contains("FromAddress = ''purchasing@project1.local''", script);
        Assert.Contains("FromName = ''Project1 Purchasing''", script);

        var addColumnPosition = script.IndexOf("ADD [SourceType]", StringComparison.Ordinal);
        var backfillPosition = script.IndexOf("UPDATE email", StringComparison.Ordinal);
        var createIndexPosition = script.IndexOf("CREATE INDEX [IX_EmailOutboxes_SourceType_SourceId]", StringComparison.Ordinal);
        Assert.True(addColumnPosition >= 0 && addColumnPosition < backfillPosition);
        Assert.True(backfillPosition < createIndexPosition);
    }
}
