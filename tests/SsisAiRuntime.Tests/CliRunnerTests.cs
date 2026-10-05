using Newtonsoft.Json.Linq;
using SsisAiRuntime.Cli;
using SsisAiRuntime.Core;
using SsisAiRuntime.Inspectors;

namespace SsisAiRuntime.Tests;

public class CliRunnerTests
{
    [Theory]
    [InlineData("overview")]
    [InlineData("sql")]
    [InlineData("lineage")]
    [InlineData("configuration")]
    public void CommandsDispatchAndWriteVersionedJson(string command)
    {
        WithPackage(path =>
        {
            var output = new StringWriter();
            var calls = 0;
            var runner = new CliRunner((actualCommand, actualPath) =>
            {
                calls++;
                Assert.Equal(command, actualCommand);
                Assert.Equal(path, actualPath);
                return Success();
            });

            Assert.Equal(0, runner.Run(new[] { command, path }, output));
            var json = JObject.Parse(output.ToString());
            Assert.Equal(1, calls);
            Assert.Equal("1.0", json["schemaVersion"]);
            Assert.Equal(command, json["command"]);
            Assert.True((bool)json["succeeded"]!);
            Assert.True((bool)json["isComplete"]!);
            Assert.Empty((JArray)json["diagnostics"]!);
            Assert.Empty((JArray)json["unsupportedItems"]!);
            Assert.Equal("metadata-only", json["redaction"]!["policy"]);
        });
    }

    [Theory]
    [InlineData()]
    [InlineData("execute", "password=secret")]
    [InlineData("overview")]
    [InlineData("overview", " ")]
    [InlineData("overview", "package.dtsx", "--password=secret")]
    public void InvalidArgumentsNeverInvokeService(params string[] args)
    {
        var output = new StringWriter();
        var runner = new CliRunner((_, _) => throw new InvalidOperationException("secret"));
        Assert.Equal(2, runner.Run(args, output));
        Assert.Equal("cli.usage", JObject.Parse(output.ToString())["diagnostics"]![0]!["code"]);
        Assert.DoesNotContain("secret", output.ToString());
    }

    [Fact]
    public void MissingFileReturnsLoadFailureWithoutEchoingPath()
    {
        var output = new StringWriter();
        var runner = new CliRunner((_, _) => throw new InvalidOperationException("must not load"));
        Assert.Equal(3, runner.Run(new[] { "overview", Path.Combine(Path.GetTempPath(), Guid.NewGuid() + "secret.dtsx") }, output));
        Assert.DoesNotContain("secret", output.ToString());
        Assert.Equal(JTokenType.Null, JObject.Parse(output.ToString())["results"]!.Type);
    }

    [Fact]
    public void LoadDiagnosticsAndExceptionsNeverExposeDetails()
    {
        WithPackage(path =>
        {
            var output = new StringWriter();
            var runner = new CliRunner((_, _) => new CliInspection(null,
                new RuntimeDiagnostics("16", "x64", new[] { new RuntimeDiagnostic("ssis.package.load_failed", RuntimeDiagnosticSeverity.Error, "password=secret") }),
                Array.Empty<UnsupportedItem>()));
            Assert.Equal(3, runner.Run(new[] { "overview", path }, output));
            Assert.DoesNotContain("secret", output.ToString());
            output.GetStringBuilder().Clear();
            runner = new CliRunner((_, _) => throw new InvalidOperationException("password=secret"));
            Assert.Equal(4, runner.Run(new[] { "sql", path }, output));
            Assert.DoesNotContain("secret", output.ToString());
            Assert.False((bool)JObject.Parse(output.ToString())["succeeded"]!);
        });
    }

    [Fact]
    public void IncompleteOutputRetainsMetadataButOmitsSqlSettingsDescriptionsAndReasons()
    {
        WithPackage(path =>
        {
            var package = new PackageOverview(Guid.Empty, "Demo", "id", "description-secret", DateTime.MinValue,
                1, 0, 0, "DontSaveSensitive", "Default", 0, 0, 0, 0, 0, false);
            var sql = new SqlStatementOverview("id", "Task", "SQL", "DirectInput", "SELECT 'sql-secret'", false, "", "", 0);
            var setting = new DataFlowSettingOverview("Component", "id", "SqlCommand", "setting-secret", false);
            var unsupported = new UnsupportedItem("id", "Task", "SQL", "reason-secret");
            var runner = new CliRunner((_, _) => Success(new { package, sql, setting }, new[] { unsupported }));
            var output = new StringWriter();

            Assert.Equal(5, runner.Run(new[] { "sql", path }, output));
            var json = JObject.Parse(output.ToString());
            Assert.True((bool)json["succeeded"]!);
            Assert.False((bool)json["isComplete"]!);
            Assert.Single((JArray)json["unsupportedItems"]!);
            Assert.Equal("Task", json["results"]!["sql"]!["taskName"]);
            Assert.Null(json["results"]!["sql"]!["statementText"]);
            Assert.Null(json["results"]!["setting"]!["value"]);
            Assert.DoesNotContain("secret", output.ToString());
        });
    }

    [Fact]
    public void SummaryBoundsLargeCoverageWithoutLosingTotalsOrLeakingReasons()
    {
        WithPackage(path =>
        {
            var items = Enumerable.Range(0, 1869).Select(index => new UnsupportedItem(index.ToString(),
                "Setting" + index % 20, new string('X', 500), "reason-secret")).ToArray();
            var runner = new CliRunner((_, _) => Success(Package(), items));
            var output = new StringWriter();

            Assert.Equal(5, runner.Run(new[] { "overview", path, "--summary" }, output));
            var json = JObject.Parse(output.ToString());
            Assert.Equal(1869, (int)json["coverage"]!["unsupportedCount"]!);
            Assert.Equal(12, (int)json["coverage"]!["groupsOmitted"]!);
            Assert.Equal(1864, (int)json["coverage"]!["examplesOmitted"]!);
            Assert.Equal(8, ((JArray)json["coverage"]!["groups"]!).Count);
            Assert.Equal(5, ((JArray)json["unsupportedItems"]!).Count);
            Assert.Equal(120, ((string)json["unsupportedItems"]![0]!["creationName"]!).Length);
            Assert.True(output.ToString().Length < 6000);
            Assert.DoesNotContain("reason-secret", output.ToString());
        });
    }

    [Fact]
    public void SummaryRejectsWrongResultTypeAndCountsEmptyCollectionsAsZero()
    {
        WithPackage(path =>
        {
            var output = new StringWriter();
            var runner = new CliRunner((_, _) => Success());
            Assert.Equal(4, runner.Run(new[] { "sql", path, "--summary" }, output));
            output.GetStringBuilder().Clear();
            var context = new PackageContextBuilder().BuildSql(Package(), InspectionResult<SqlStatementOverview>.Complete(Array.Empty<SqlStatementOverview>()));
            runner = new CliRunner((_, _) => Success(context));
            Assert.Equal(0, runner.Run(new[] { "sql", path, "--summary" }, output));
            Assert.Equal(0, (int)JObject.Parse(output.ToString())["results"]!["counts"]!["sqlTasks"]!);
        });
    }

    private static PackageOverview Package() => new(Guid.NewGuid(), "Demo", "package-id", "", DateTime.MinValue,
        1, 0, 0, "DontSaveSensitive", "Default", 0, 0, 0, 0, 0, false);

    [Fact]
    public void AllRetainsReportsAndContinuesThroughIncompleteCoverage()
    {
        WithPackage(path =>
        {
            var package = Package();
            var builder = new PackageContextBuilder();
            var gap = new UnsupportedItem("id", "Setting", "Component", "secret");
            var reports = new Dictionary<string, CliInspection>
            {
                ["overview"] = Success(package),
                ["sql"] = Success(builder.BuildSql(package, InspectionResult<SqlStatementOverview>.Complete(Array.Empty<SqlStatementOverview>()))),
                ["lineage"] = Success(builder.BuildLineage(package, new InspectionResult<DataFlowOverview>(Array.Empty<DataFlowOverview>(), new[] { gap })), new[] { gap }),
                ["configuration"] = Success(builder.BuildConfiguration(package,
                    InspectionResult<ConnectionOverview>.Complete(Array.Empty<ConnectionOverview>()),
                    InspectionResult<VariableOverview>.Complete(Array.Empty<VariableOverview>()),
                    InspectionResult<ParameterOverview>.Complete(Array.Empty<ParameterOverview>()),
                    InspectionResult<ExpressionOverview>.Complete(Array.Empty<ExpressionOverview>())))
            };
            var calls = 0;
            var runner = new CliRunner((command, _) => { calls++; Assert.Equal("all", command); return Success(new CliInspectionBatch(reports)); });
            var output = new StringWriter();
            Assert.Equal(5, runner.Run(new[] { "all", path, "--summary" }, output));
            var json = JObject.Parse(output.ToString());
            Assert.Equal(1, calls);
            Assert.Equal(4, ((JArray)json["completedOperations"]!).Count);
            Assert.Empty((JArray)json["skippedOperations"]!);
            Assert.Equal(0, (int)json["results"]!["configuration"]!["exitCode"]!);
            Assert.Equal(1, (int)json["coverage"]!["unsupportedCount"]!);
            Assert.Equal(1, (int)json["coverage"]!["examplesOmitted"]!);
            Assert.DoesNotContain("secret", output.ToString());
        });
    }

    [Fact]
    public void AllFailurePreservesEarlierReportsAndListsSkippedOperations()
    {
        WithPackage(path =>
        {
            var reports = new Dictionary<string, CliInspection>
            {
                ["overview"] = Success(Package()),
                ["sql"] = new CliInspection(null, new RuntimeDiagnostics("16", "x64", new[]
                {
                    new RuntimeDiagnostic("cli.inspection.failed", RuntimeDiagnosticSeverity.Error, "secret")
                }), Array.Empty<UnsupportedItem>(), 4)
            };
            var runner = new CliRunner((_, _) => Success(new CliInspectionBatch(reports)));
            var output = new StringWriter();
            Assert.Equal(4, runner.Run(new[] { "all", path, "--summary" }, output));
            var json = JObject.Parse(output.ToString());
            Assert.False((bool)json["succeeded"]!);
            Assert.Equal(0, (int)json["results"]!["overview"]!["exitCode"]!);
            Assert.Equal(new[] { "lineage", "configuration" }, json["skippedOperations"]!.Values<string>());
            Assert.DoesNotContain("secret", output.ToString());
        });
    }

    [Theory]
    [InlineData(UnsupportedItem.IntentionalOmissionCode)]
    [InlineData(UnsupportedItem.UnsupportedMetadataCode)]
    [InlineData(UnsupportedItem.ReadFailureCode)]
    [InlineData(UnsupportedItem.UnspecifiedCode)]
    public void SummaryExposesOnlyAllowlistedReasonCodes(string reasonCode)
    {
        WithPackage(path =>
        {
            var gap = new UnsupportedItem("id", "Setting", "Component", "reason-secret", reasonCode);
            var runner = new CliRunner((_, _) => Success(Package(), new[] { gap }));
            var output = new StringWriter();
            Assert.Equal(5, runner.Run(new[] { "overview", path, "--summary" }, output));
            var json = JObject.Parse(output.ToString());
            Assert.Equal(reasonCode, (string)json["unsupportedItems"]![0]!["reasonCode"]!);
            Assert.Equal(reasonCode, (string)json["coverage"]!["reasonCounts"]![0]!["reasonCode"]!);
            Assert.Equal(1, (int)json["coverage"]!["reasonCounts"]![0]!["count"]!);
            Assert.DoesNotContain("reason-secret", output.ToString());
        });
        var exception = Assert.Throws<ArgumentException>(() => new UnsupportedItem("id", "Setting", "Component", "", "code-secret"));
        Assert.DoesNotContain("code-secret", exception.Message);
    }

    [Fact]
    public void SummaryBoundsDiagnosticsAndReportsOmittedCounts()
    {
        WithPackage(path =>
        {
            var diagnostics = new RuntimeDiagnostics("16", "x64", Enumerable.Range(0, 20).Select(index =>
                new RuntimeDiagnostic(new string('X', 500) + index, RuntimeDiagnosticSeverity.Warning, "message-secret")));
            var runner = new CliRunner((_, _) => new CliInspection(Package(), diagnostics, Array.Empty<UnsupportedItem>()));
            var output = new StringWriter();
            Assert.Equal(0, runner.Run(new[] { "overview", path, "--summary" }, output));
            var json = JObject.Parse(output.ToString());
            Assert.Equal(8, ((JArray)json["diagnostics"]!).Count);
            Assert.Equal(20, (int)json["coverage"]!["diagnosticCount"]!);
            Assert.Equal(12, (int)json["coverage"]!["diagnosticsOmitted"]!);
            Assert.True(output.ToString().Length < 6000);
            Assert.DoesNotContain("message-secret", output.ToString());
        });
    }

    [Fact]
    public void SummaryRejectsWrongContextAndMissingPackageIdentity()
    {
        WithPackage(path =>
        {
            var sql = new PackageContextBuilder().BuildSql(Package(), InspectionResult<SqlStatementOverview>.Complete(Array.Empty<SqlStatementOverview>()));
            var runner = new CliRunner((_, _) => Success(sql));
            Assert.Equal(4, runner.Run(new[] { "configuration", path, "--summary" }, new StringWriter()));
            var package = new PackageOverview(Guid.NewGuid(), "Demo", "", "", DateTime.MinValue,
                1, 0, 0, "DontSaveSensitive", "Default", 0, 0, 0, 0, 0, false);
            runner = new CliRunner((_, _) => Success(package));
            Assert.Equal(4, runner.Run(new[] { "overview", path, "--summary" }, new StringWriter()));
        });
    }

    private static CliInspection Success(object? results = null, UnsupportedItem[]? unsupported = null) =>
        new(results ?? new { packageName = "Demo" }, new RuntimeDiagnostics("16", "x64", Array.Empty<RuntimeDiagnostic>()),
            unsupported ?? Array.Empty<UnsupportedItem>());

    private static void WithPackage(Action<string> test)
    {
        var path = Path.GetTempFileName();
        try { test(path); }
        finally { File.Delete(path); }
    }
}