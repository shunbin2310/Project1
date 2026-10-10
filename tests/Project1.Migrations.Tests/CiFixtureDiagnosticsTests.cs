using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Project1.MigrationHost.Acceptance;

namespace Project1.Migrations.Tests;

public sealed class CiFixtureDiagnosticsTests
{
    private const string Secret = "private password; raw SQL;\n::error::injected";

    [Fact]
    public void StagesHaveFixedContiguousIdsAndRejectUnknownValues()
    {
        using var output = new StringWriter();
        var diagnostics = new CiFixtureDiagnostics(output);
        var stages = Enum.GetValues<FixtureStage>();
        Assert.Equal(Enumerable.Range(0, 15), stages.Select(stage => (int)stage));
        foreach (var stage in stages) diagnostics.Begin(stage);
        Assert.Equal(stages.Select(stage => $"CI-FIXTURE stage=F{(int)stage:D2} event=begin"),
            output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
        var before = output.ToString();
        Assert.Throws<ArgumentOutOfRangeException>(() => diagnostics.Begin((FixtureStage)999));
        Assert.Equal(before, output.ToString());
    }

    [Theory]
    [InlineData("cancelled")]
    [InlineData("timeout")]
    [InlineData("invalid-operation")]
    [InlineData("invalid-input")]
    [InlineData("io")]
    [InlineData("unexpected")]
    public async Task FailureUsesSafeCategoriesReturnsNonzeroAndStopsLaterWork(string category)
    {
        Exception error = category switch
        {
            "cancelled" => new TaskCanceledException(Secret),
            "timeout" => new TimeoutException(Secret),
            "invalid-operation" => new InvalidOperationException(Secret),
            "invalid-input" => new JsonException(Secret),
            "io" => new IOException(Secret),
            _ => new Exception(Secret)
        };
        error.Data[Secret] = Secret;
        using var output = new StringWriter();
        var diagnostics = new CiFixtureDiagnostics(output);
        var laterWork = false;
        var status = await diagnostics.RunAsync(async () =>
        {
            diagnostics.Begin(FixtureStage.MigrateBaseline);
            await Task.FromException(error);
            laterWork = true;
        });
        Assert.Equal(1, status);
        Assert.False(laterWork);
        Assert.Equal($"CI-FIXTURE stage=F07 event=begin{Environment.NewLine}" +
            $"CI-FIXTURE stage=F07 event=error category={category}{Environment.NewLine}", output.ToString());
        Assert.DoesNotContain(Secret, output.ToString());
    }

    [Fact]
    public async Task SynchronousGuardRejectionIsCaughtWithoutConnectingOrRenderingException()
    {
        using var output = new StringWriter();
        var diagnostics = new CiFixtureDiagnostics(output);
        Assert.Equal(1, await diagnostics.RunAsync(() => throw new InvalidOperationException(Secret)));
        Assert.Equal($"CI-FIXTURE stage=F00 event=error category=invalid-operation{Environment.NewLine}", output.ToString());
    }

    [Fact]
    public async Task SuccessKeepsExistingPayloadChannelSeparateAndReturnsZero()
    {
        using var output = new StringWriter();
        var diagnostics = new CiFixtureDiagnostics(output);
        var status = await diagnostics.RunAsync(() =>
        {
            diagnostics.Begin(FixtureStage.Complete);
            return Task.CompletedTask;
        });
        Assert.Equal(0, status);
        Assert.Equal($"CI-FIXTURE stage=F14 event=begin{Environment.NewLine}", output.ToString());
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(18456)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void SqlNumbersUseInvariantIntegersOnly(int number)
    {
        using var output = new StringWriter(CultureInfo.GetCultureInfo("ar-SA"));
        var diagnostics = new CiFixtureDiagnostics(output);
        diagnostics.ReportSqlNumber(number);
        Assert.Equal("CI-FIXTURE stage=F00 event=error category=sql number=" +
            number.ToString(CultureInfo.InvariantCulture) + Environment.NewLine, output.ToString());
    }

    [Fact]
    public async Task ActualSqlExceptionReportsBoundedNumbersWithoutMessagesIncludingInnerException()
    {
        // SqlClient exposes no public exception constructor. Reflect only the pinned
        // package's test data factories; no connection, SQL or credentials are used.
        var errors = (SqlErrorCollection)Activator.CreateInstance(typeof(SqlErrorCollection), nonPublic: true)!;
        var constructor = typeof(SqlError).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .First(item => item.GetParameters()[0].ParameterType == typeof(int));
        var add = typeof(SqlErrorCollection).GetMethod("Add", BindingFlags.NonPublic | BindingFlags.Instance)!;
        for (var i = 0; i < 12; i++)
        {
            var arguments = constructor.GetParameters().Select(parameter => parameter.ParameterType == typeof(string)
                ? (object)Secret : parameter.ParameterType.IsValueType ? Activator.CreateInstance(parameter.ParameterType) : null).ToArray();
            arguments[0] = 51000 + i;
            add.Invoke(errors, [constructor.Invoke(arguments)]);
        }
        var factory = typeof(SqlException).GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(item => item.Name == "CreateException" && item.GetParameters().Length == 2);
        var sql = (SqlException)factory.Invoke(null, [errors, "16.0.0"])!;
        using var output = new StringWriter();
        var diagnostics = new CiFixtureDiagnostics(output);
        diagnostics.Begin(FixtureStage.ExecutionAccount);
        Assert.Equal(1, await diagnostics.RunAsync(() =>
            Task.FromException(new InvalidOperationException(Secret, sql))));
        var lines = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(9, lines.Length); // One stage plus at most eight SQL errors.
        Assert.Equal(Enumerable.Range(51000, 8).Select(number =>
            $"CI-FIXTURE stage=F09 event=error category=sql number={number}"), lines.Skip(1));
        Assert.DoesNotContain("private", output.ToString());
        Assert.DoesNotContain("::error::", output.ToString());
    }
}
