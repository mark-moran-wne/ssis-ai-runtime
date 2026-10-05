using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.SqlServer.Dts.Pipeline.Wrapper;
using Microsoft.SqlServer.Dts.Runtime;
using SsisAiRuntime.Ssis16;
using SsisAiRuntime.Inspectors;
using Newtonsoft.Json.Linq;
using RuntimeWrapper = Microsoft.SqlServer.Dts.Runtime.Wrapper;

namespace SsisAiRuntime.Ssis16IntegrationTests
{
    internal static class Program
    {
        private static string stage = "initialization";

        private static int Main(string[] args)
        {
            if (args.Length > 1)
            {
                Console.Error.WriteLine("Usage: SsisAiRuntime.Ssis16IntegrationTests.exe [package.dtsx]");
                return 2;
            }
            var directory = Path.Combine(Path.GetTempPath(), "SsisAiRuntimeFixture-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var path = Path.Combine(directory, "Conversion.dtsx");
                stage = "fixture.creation";
                Require(Environment.Is64BitProcess, "fixture.architecture");
                CreateFixture(path);
                stage = "ai.question.plan";
                var plan = RunCli(new[] { "ai", "question.plan", "What uses this connection?" }, 0);
                Require((string)plan["tool"] == "question.plan" &&
                    (string)plan["results"]!["intent"] == "DependencyQuery" &&
                    !plan.ToString().Contains("What uses this connection?"), "fixture.ai.plan");
                stage = "ai.package.summary";
                var aiSummary = RunCli(new[] { "ai", "package.summary", path }, 0, 5);
                Require((string)aiSummary["tool"] == "package.summary" &&
                    (string)aiSummary["redaction"]!["policy"] == "metadata-only", "fixture.ai.summary");
                RequireNoValues(aiSummary);
                stage = "ai.dependency.graph";
                var dependencyGraph = RunCli(new[] { "ai", "dependency.graph", path }, 0, 5);
                var graphNodes = (JArray)dependencyGraph["results"]!["nodes"]!;
                Require(graphNodes.Count > 0 && (int)dependencyGraph["results"]!["nodeCount"]! >= graphNodes.Count,
                    "fixture.ai.graph");
                var nodeKey = (string)graphNodes[0]! ["key"]!;
                stage = "ai.selector.resolve";
                var selector = RunCli(new[] { "ai", "selector.resolve", path, "--selector", nodeKey }, 0, 5);
                Require((string)selector["results"]!["status"] == "Resolved" &&
                    (string)selector["results"]!["resolvedNode"]!["key"] == nodeKey, "fixture.ai.selector");
                stage = "ai.dependency.query";
                var dependencyQuery = RunCli(new[] { "ai", "dependency.query", path, "--node", nodeKey, "--recursive" }, 0, 5);
                Require((string)dependencyQuery["tool"] == "dependency.query" &&
                    ((JArray)dependencyQuery["results"]!["nodes"]!).Count > 0, "fixture.ai.query");
                stage = "ai.impact.analysis";
                var impact = RunCli(new[] { "ai", "impact.analysis", path, "--node", nodeKey }, 0, 5);
                Require((string)impact["tool"] == "impact.analysis" &&
                    (string)impact["results"]!["root"]!["key"] == nodeKey, "fixture.ai.impact");
                var incomingEdge = ((JArray)dependencyGraph["results"]!["edges"]!).FirstOrDefault();
                if (incomingEdge != null)
                {
                    stage = "ai.impact.classified";
                    var classified = RunCli(new[] { "ai", "impact.classified", path, "--node", (string)incomingEdge["to"]! }, 0, 5);
                    Require((string)classified["tool"] == "impact.classified" &&
                        (string)classified["results"]!["root"]!["key"] == (string)incomingEdge["to"]!, "fixture.ai.classified");
                    Require((string)classified["results"]!["narrative"]!["redactionPolicy"] == "metadata-only", "fixture.ai.classified.redaction");
                }
                stage = "inspect.selection";
                var selectedReports = RunCli(new[] { "inspect", path, "--include", "lineage" }, 0, 5);
                Require(((JArray)selectedReports["completedOperations"]).Values<string>().SequenceEqual(new[] { "lineage" }) &&
                    ((JObject)selectedReports["results"]).Properties().Select(property => property.Name).SequenceEqual(new[] { "lineage" }) &&
                    ((JArray)selectedReports["skippedOperations"]).Count == 0, "fixture.inspect.selection");
                var before = Hash(path);
                stage = "fixture.reload";
                var load = new PackageLoader().Load(path);
                Require(load.Succeeded, "fixture.load");
                try
                {
                    var report = new PackageDataFlowInspector().InspectDetailed(load.Session);
                    var flow = report.Items.Single();
                    var converted = flow.Components.SelectMany(component => component.OutputColumns)
                        .Single(column => column.SourceInputLineageId.HasValue);
                    Require(converted.SourceInputLineageId > 0 && converted.LineageId != converted.SourceInputLineageId,
                        "fixture.mapping");
                    var conversion = flow.Components.Single(component => component.OutputColumns.Contains(converted));
                    var original = conversion.InputColumns.Single(column => column.LineageId == converted.SourceInputLineageId);
                    Require(original.DataType == "DT_I4" && converted.DataType == "DT_WSTR", "fixture.types");
                    Require(!report.UnsupportedItems.Any(item => item.Id == converted.Id &&
                        item.Name.Equals("SourceInputColumnLineageID", StringComparison.OrdinalIgnoreCase)), "fixture.mapping.coverage");
                    stage = "trace.query";
                    foreach (var upstream in new[] { false, true })
                    {
                        var selected = upstream ? converted : original;
                        var trace = new ColumnLineageQuery().Trace(flow, conversion.Id, selected.Id, upstream);
                        Require(trace.IsComplete && trace.Items.Count == 1 &&
                            trace.Items[0].Links.Any(link => link.Kind == "ExplicitMapping"), "fixture.query");
                        stage = upstream ? "cli.upstream" : "cli.downstream";
                        VerifyCli(path, flow.ExecutableId, conversion.Id, selected.Id, upstream);
                    }
                    Require(Hash(path) == before, "fixture.hash");
                    Console.WriteLine("Native Data Conversion verification: PASS; integer-to-string mapping traced in both directions; fixture hash unchanged; package not executed or validated.");
                    stage = "expression.parser.probe";
                    ProbeExpressionParser(load.Session.Package);
                    Require(Hash(path) == before, "fixture.parser.hash");
                    VerifyDerivedFixture(directory);
                    if (args.Length == 1) { VerifyExistingPackage(args[0]); }
                    return 0;
                }
                finally
                {
                    load.Session.Package.Dispose();
                }
            }
            catch (Exception)
            {
                Console.Error.WriteLine("Native integration verification failed at " + stage + ". Raw exception details are withheld.");
                return 1;
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static void CreateFixture(string path)
        {
            var application = new Application();
            using (var package = new Package { Name = "ConversionFixture", ProtectionLevel = DTSProtectionLevel.DontSaveSensitive })
            {
                var task = (TaskHost)package.Executables.Add("STOCK:PipelineTask");
                task.Name = "Conversion";
                var pipeline = (IDTSPipeline130)task.InnerObject;
                var source = pipeline.ComponentMetaDataCollection.New();
                source.ComponentClassID = FindComponent(application, "DTSAdapter.OleDbSource.");
                source.Instantiate().ProvideComponentProperties();
                source.Name = "MetadataSource";
                var sourceOutput = source.OutputCollection.Cast<IDTSOutput100>().First(output => !output.IsErrorOut);
                var original = sourceOutput.OutputColumnCollection.New();
                original.Name = "OriginalNumber";
                original.SetDataTypeProperties(RuntimeWrapper.DataType.DT_I4, 0, 0, 0, 0);
                var second = sourceOutput.OutputColumnCollection.New();
                second.Name = "SecondNumber";
                second.SetDataTypeProperties(RuntimeWrapper.DataType.DT_I4, 0, 0, 0, 0);

                var conversion = pipeline.ComponentMetaDataCollection.New();
                conversion.ComponentClassID = FindComponent(application, "DTSTransform.DataConvert.");
                var design = conversion.Instantiate();
                design.ProvideComponentProperties();
                conversion.Name = "ConvertNumber";
                var inputPort = conversion.InputCollection[0];
                pipeline.PathCollection.New().AttachPathAndPropagateNotifications(sourceOutput, inputPort);
                design.SetUsageType(inputPort.ID, inputPort.GetVirtualInput(), original.LineageID, DTSUsageType.UT_READONLY);
                design.SetUsageType(inputPort.ID, inputPort.GetVirtualInput(), second.LineageID, DTSUsageType.UT_READONLY);
                var outputPort = conversion.OutputCollection.Cast<IDTSOutput100>().First(output => !output.IsErrorOut);
                var converted = design.InsertOutputColumnAt(outputPort.ID, 0, "ConvertedText", string.Empty);
                design.SetOutputColumnProperty(outputPort.ID, converted.ID, "SourceInputColumnLineageID", original.LineageID);
                design.SetOutputColumnDataTypeProperties(outputPort.ID, converted.ID, RuntimeWrapper.DataType.DT_WSTR, 32, 0, 0, 0);
                application.SaveToXml(path, package, null);
            }
        }

        private static void VerifyDerivedFixture(string directory)
        {
            stage = "derived.fixture.creation";
            var path = Path.Combine(directory, "Derived.dtsx");
            var application = new Application();
            using (var package = new Package { Name = "DerivedFixture", ProtectionLevel = DTSProtectionLevel.DontSaveSensitive })
            {
                var task = (TaskHost)package.Executables.Add("STOCK:PipelineTask");
                package.Variables.Add("ParserFlag", false, "User", "package-string");
                task.Variables.Add("ParserFlag", false, "User", true);
                var pipeline = (IDTSPipeline130)task.InnerObject;
                var source = pipeline.ComponentMetaDataCollection.New();
                source.ComponentClassID = FindComponent(application, "DTSAdapter.OleDbSource.");
                source.Instantiate().ProvideComponentProperties();
                var sourceOutput = source.OutputCollection.Cast<IDTSOutput100>().First(output => !output.IsErrorOut);
                var first = sourceOutput.OutputColumnCollection.New();
                first.Name = "OriginalNumber";
                first.SetDataTypeProperties(RuntimeWrapper.DataType.DT_I4, 0, 0, 0, 0);
                var second = sourceOutput.OutputColumnCollection.New();
                second.Name = "SecondNumber";
                second.SetDataTypeProperties(RuntimeWrapper.DataType.DT_I4, 0, 0, 0, 0);
                var derive = pipeline.ComponentMetaDataCollection.New();
                derive.ComponentClassID = FindComponent(application, "DTSTransform.DerivedColumn.");
                var design = derive.Instantiate();
                design.ProvideComponentProperties();
                derive.Name = "DeriveValues";
                var input = derive.InputCollection[0];
                pipeline.PathCollection.New().AttachPathAndPropagateNotifications(sourceOutput, input);
                var selected = design.SetUsageType(input.ID, input.GetVirtualInput(), first.LineageID, DTSUsageType.UT_READWRITE);
                design.SetUsageType(input.ID, input.GetVirtualInput(), second.LineageID, DTSUsageType.UT_READONLY);
                var output = derive.OutputCollection.Cast<IDTSOutput100>().First(port => !port.IsErrorOut);
                var sum = design.InsertOutputColumnAt(output.ID, 0, "Sum", "");
                design.SetOutputColumnDataTypeProperties(output.ID, sum.ID, RuntimeWrapper.DataType.DT_I4, 0, 0, 0, 0);
                design.SetOutputColumnProperty(output.ID, sum.ID, "Expression", "#" + first.LineageID + " + #" + second.LineageID);
                var constant = design.InsertOutputColumnAt(output.ID, 1, "ConstantText", "");
                design.SetOutputColumnDataTypeProperties(output.ID, constant.ID, RuntimeWrapper.DataType.DT_WSTR, 64, 0, 0, 0);
                design.SetOutputColumnProperty(output.ID, constant.ID, "Expression", "\"expression-secret-literal\"");
                var conditional = design.InsertOutputColumnAt(output.ID, 2, "ScopedChoice", "");
                design.SetOutputColumnDataTypeProperties(output.ID, conditional.ID, RuntimeWrapper.DataType.DT_I4, 0, 0, 0, 0);
                design.SetOutputColumnProperty(output.ID, conditional.ID, "Expression", "@[User::ParserFlag] ? #" + first.LineageID + " : #" + second.LineageID);
                design.SetInputColumnProperty(input.ID, selected.ID, "Expression", "#" + first.LineageID + " + #" + second.LineageID);
                application.SaveToXml(path, package, null);
            }
            var before = Hash(path);
            stage = "derived.fixture.reload";
            var load = new PackageLoader().Load(path);
            Require(load.Succeeded, "derived.load");
            try
            {
                var report = new PackageDataFlowInspector().InspectDetailed(load.Session);
                var flow = report.Items.Single();
                var derive = flow.Components.Single(component => component.Name == "DeriveValues");
                var sum = derive.OutputColumns.Single(column => column.Name == "Sum");
                var constant = derive.OutputColumns.Single(column => column.Name == "ConstantText");
                var conditional = derive.OutputColumns.Single(column => column.Name == "ScopedChoice");
                var replacement = derive.OutputColumns.Single(column => column.IsReplacement);
                Require(sum.ExpressionDependencies.IsResolved && sum.ExpressionDependencies.InputLineageIds.Count == 2, "derived.sum");
                Require(constant.ExpressionDependencies.IsResolved && constant.ExpressionDependencies.InputLineageIds.Count == 0, "derived.constant");
                Require(replacement.ExpressionDependencies.IsResolved && replacement.ExpressionDependencies.InputLineageIds.Count == 2, "derived.replacement");
                Require(conditional.ExpressionDependencies.IsResolved && conditional.ExpressionDependencies.InputLineageIds.Count == 2, "derived.variable.scope");
                foreach (var column in new[] { sum, replacement, constant, conditional })
                {
                    stage = "derived.query." + column.Name;
                    var trace = new ColumnLineageQuery().Trace(flow, derive.Id, column.Id, true);
                    Require(trace.IsComplete && trace.Items.Count == 1, "derived.query");
                    Require(trace.Items[0].Links.Count(link => link.Kind == "ExpressionResolved") == (column == constant ? 0 : 2), "derived.references");
                    Require(!trace.Items[0].Links.Any(link => link.Kind == "ExpressionResolved" && link.Source.IsReplacement), "derived.original.values");
                    stage = "derived.cli." + column.Name;
                    VerifyCli(path, flow.ExecutableId, derive.Id, column.Id, true, column == constant ? null : "ExpressionResolved", "expression-secret-literal");
                }
                Require(Hash(path) == before, "derived.hash");
                Console.WriteLine("Native Derived Column verification: PASS; per-output references, zero-input literal, original-value replacement, CLI redaction, and unchanged fixture hash.");
            }
            finally { load.Session.Package.Dispose(); }
        }

        private static string FindComponent(Application application, string prefix)
        {
            return application.PipelineComponentInfos.Cast<PipelineComponentInfo>()
                .Single(info => info.CreationName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).CreationName;
        }

        private static void ProbeExpressionParser(Package package)
        {
            var pipeline = (IDTSPipeline130)((TaskHost)package.Executables[0]).InnerObject;
            var columns = pipeline.ComponentMetaDataCollection.Cast<IDTSComponentMetaData100>()
                .Single(component => component.Name == "ConvertNumber").InputCollection[0].InputColumnCollection;
            var original = columns[0];
            var second = columns[1];
            package.Variables.Add("ParserFlag", false, "User", true);
            object evaluator = new RuntimeWrapper.ExpressionEvaluatorClass();
            try
            {
                var parser = evaluator as IDTSExpressionEvaluatorEx100;
                if (parser == null) { parser = pipeline as IDTSExpressionEvaluatorEx100; }
                if (parser == null)
                {
                    Console.WriteLine("Native expression parser probe: interface unavailable on evaluator and pipeline objects.");
                    return;
                }
                var dispenser = DtsConvert.GetExtendedInterface(package.VariableDispenser);
                var firstId = original.LineageID;
                var secondId = second.LineageID;
                var cases = new[]
                {
                    new { Label = "column-reference", Expression = "#" + firstId + " + 1", Expected = new[] { firstId }, Valid = true },
                    new { Label = "multiple-columns", Expression = "#" + firstId + " + #" + secondId, Expected = new[] { firstId, secondId }, Valid = true },
                    new { Label = "named-columns", Expression = "OriginalNumber + SecondNumber", Expected = new[] { firstId, secondId }, Valid = true },
                    new { Label = "repeated-column", Expression = "#" + firstId + " + #" + firstId, Expected = new[] { firstId }, Valid = true },
                    new { Label = "conditional-variable", Expression = "@[User::ParserFlag] ? #" + firstId + " : #" + secondId, Expected = new[] { firstId, secondId }, Valid = true },
                    new { Label = "constant", Expression = "1 + 2", Expected = Array.Empty<int>(), Valid = true },
                    new { Label = "string-literal", Expression = "\"#" + firstId + "\"", Expected = Array.Empty<int>(), Valid = true },
                    new { Label = "escaped-literal", Expression = "\"quoted \\\"#" + firstId + "\\\"\"", Expected = Array.Empty<int>(), Valid = true },
                    new { Label = "invalid-syntax", Expression = "#" + firstId + " +", Expected = Array.Empty<int>(), Valid = false }
                };
                foreach (var testCase in cases)
                {
                    var before = SnapshotColumns(columns);
                    var observed = new NativeExpressionInputColumns(columns);
                    var parsed = false;
                    try
                    {
                        parser.Parse(testCase.Expression, dispenser, observed);
                        parsed = true;
                    }
                    catch (System.Runtime.InteropServices.COMException)
                    {
                        parsed = false;
                    }
                    Require(before == SnapshotColumns(columns) && observed.MutationAttempts == 0, "fixture.parser.mutation");
                    Require(parsed == testCase.Valid, "fixture.parser.status." + testCase.Label);
                    if (parsed) { Require(observed.BindingLineageIds.SetEquals(testCase.Expected), "fixture.parser.references." + testCase.Label); }
                    Console.WriteLine("Native parser case " + testCase.Label + ": PASS; parsed=" + parsed +
                        "; resolvedReferences=" + (parsed ? observed.BindingLineageIds.Count : 0) + "; collectionChanged=False.");
                }
            }
            finally
            {
                if (System.Runtime.InteropServices.Marshal.IsComObject(evaluator))
                {
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(evaluator);
                }
            }
        }

        private static string SnapshotColumns(IDTSInputColumnCollection100 columns)
        {
            return string.Join("|", columns.Cast<IDTSInputColumn100>().Select(column =>
                column.ID + ":" + column.LineageID + ":" + column.Name + ":" + column.UsageType + ":" + column.DataType));
        }

        private static void Require(bool condition, string code)
        {
            if (!condition) { throw new InvalidOperationException(code); }
        }

        private static void VerifyCli(string path, string flowId, string componentId, string columnId, bool upstream,
            string expectedKind = "ExplicitMapping", string forbiddenText = null)
        {
            var report = RunCli(new[] { "trace", path, "--flow", flowId, "--component", componentId,
                "--column", columnId, "--direction", upstream ? "upstream" : "downstream", "--details" }, 0);
            Require((bool)report["succeeded"] && (bool)report["isComplete"], "fixture.cli.envelope");
            Require((bool)report["redaction"]["applied"] && (bool)report["redaction"]["expressionTextOmitted"] &&
                (bool)report["redaction"]["settingValuesOmitted"], "fixture.cli.redaction");
            if (expectedKind != null)
            {
                Require(report["results"]["trace"]["links"].Any(link => (string)link["kind"] == expectedKind), "fixture.cli.mapping");
            }
            if (forbiddenText != null) { Require(!report.ToString().Contains(forbiddenText), "fixture.cli.literal"); }
            RequireNoValues(report);
        }

        private static void VerifyExistingPackage(string path)
        {
            stage = "package.smoke.load";
            Require(File.Exists(path), "smoke.file");
            var before = Hash(path);
            var load = new PackageLoader().Load(path);
            Require(load.Succeeded, "smoke.load");
            try
            {
                var session = load.Session;
                var overview = new PackageOverviewInspector().Inspect(session);
                var sql = new PackageSqlInspector().InspectDetailed(session);
                var flows = new PackageDataFlowInspector().InspectDetailed(session);
                var connections = new PackageConnectionInspector().InspectDetailed(session);
                var variables = new PackageVariableInspector().InspectDetailed(session);
                var parameters = new PackageParameterInspector().InspectDetailed(session);
                var expressions = new PackageExpressionInspector().InspectDetailed(session);
                Require(connections.Items.Count == session.Package.Connections.Count && variables.Items.Count == session.Package.Variables.Count &&
                    parameters.Items.Count == session.Package.Parameters.Count, "smoke.native.counts");
                foreach (var command in new[] { "overview", "sql", "lineage", "configuration" })
                {
                    stage = "package.smoke." + command;
                    var report = RunCli(new[] { command, path, "--details" }, 0, 5);
                    Require((bool)report["succeeded"] && (string)report["command"] == command, "smoke.envelope");
                    Require((bool)report["isComplete"] == ((int)report["exitCode"] == 0), "smoke.complete");
                    var expectedGaps = command == "sql" ? sql.UnsupportedItems.Count : command == "lineage" ? flows.UnsupportedItems.Count :
                        command == "configuration" ? connections.UnsupportedItems.Count + variables.UnsupportedItems.Count + parameters.UnsupportedItems.Count + expressions.UnsupportedItems.Count : 0;
                    Require(((JArray)report["unsupportedItems"]).Count == expectedGaps, "smoke.coverage");
                    var expectedCount = command == "overview" ? overview.ConnectionCount : command == "sql" ? sql.Items.Count :
                        command == "lineage" ? flows.Items.Count : connections.Items.Count;
                    var count = command == "overview" ? (int)report["results"]["connectionCount"] :
                        command == "sql" ? ((JArray)report["results"]["sqlStatements"]).Count :
                        command == "lineage" ? ((JArray)report["results"]["dataFlows"]).Count : ((JArray)report["results"]["connections"]).Count;
                    Require(count == expectedCount, "smoke.cli.counts");
                    RequireNoValues(report);
                    Require((string)report["redaction"]["policy"] == "metadata-only", "smoke.redaction.policy");
                    foreach (var field in new[] { "applied", "connectionStringsOmitted", "variableValuesOmitted", "parameterValuesOmitted",
                        "expressionTextOmitted", "sqlTextOmitted", "settingValuesOmitted", "descriptionsOmitted", "diagnosticDetailsOmitted" })
                    {
                        Require((bool)report["redaction"][field], "smoke.redaction.flag");
                    }
                    Console.WriteLine("Package smoke " + command + ": PASS; count=" + count + "; coverageGaps=" + expectedGaps + ".");
                }
                var batch = RunCli(new[] { "inspect", path }, 0, 5);
                Require(((JArray)batch["completedOperations"]).Count == 4 && ((JArray)batch["skippedOperations"]).Count == 0, "smoke.batch");
                var executables = new PackageExecutableInspector().InspectDetailed(session);
                var precedence = new PackagePrecedenceInspector().InspectDetailed(session);
                var catalog = new SemanticHandleCatalogBuilder().Build(overview, connections.Items, variables.Items, parameters.Items, executables.Items, flows.Items);
                var graph = new ControlFlowGraphBuilder().Build(executables.Items, precedence, catalog);
                var graphReport = RunCli(new[] { "control-flow", path }, 0, 5);
                Require((int)graphReport["results"]["counts"]["nodes"] == graph.Nodes.Count &&
                    (int)graphReport["results"]["counts"]["precedenceEdges"] == graph.Edges.Count(edge => edge.Kind == ControlFlowEdgeKind.Precedence), "smoke.graph");
                RunCli(new[] { "execute", path }, 2);
                RunCli(new[] { "overview", Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".dtsx") }, 3);
                Require(Hash(path) == before, "smoke.hash");
                Console.WriteLine("Package smoke: PASS; direct/CLI counts, coverage, graph, error exits, redaction, and unchanged SHA-256; no execution or validation.");
            }
            finally { load.Session.Package.Dispose(); }
        }

        private static void RequireNoValues(JObject report)
        {
            Require(!report.Descendants().OfType<JProperty>().Any(property =>
                property.Name == "statementText" || property.Name == "description" || property.Name == "value" ||
                property.Name == "expression" || property.Name == "friendlyExpression" || property.Name == "connectionString"), "fixture.cli.values");
        }

        private static JObject RunCli(string[] arguments, params int[] allowedExitCodes)
        {
            var cli = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SsisAiRuntime.Cli.exe");
            Require(File.Exists(cli), "fixture.cli.missing");
            var argumentText = string.Join(" ", arguments.Select(Quote));
            using (var process = new Process
            {
                StartInfo = new ProcessStartInfo(cli, argumentText)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            })
            {
                Require(process.Start(), "fixture.cli.start");
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(60000))
                {
                    process.Kill();
                    process.WaitForExit();
                    throw new InvalidOperationException("fixture.cli.timeout");
                }
                System.Threading.Tasks.Task.WaitAll(stdout, stderr);
                Require(allowedExitCodes.Contains(process.ExitCode), "fixture.cli.exit");
                var report = JObject.Parse(stdout.Result);
                Require((string)report["schemaVersion"] == "1.0" && (int)report["exitCode"] == process.ExitCode, "fixture.cli.envelope");
                return report;
            }
        }

        private static string Quote(string value)
        {
            Require(value.IndexOf('"') < 0 && !value.EndsWith("\\", StringComparison.Ordinal), "fixture.argument");
            return "\"" + value + "\"";
        }

        private static string Hash(string path)
        {
            using (var algorithm = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                return Convert.ToBase64String(algorithm.ComputeHash(stream));
            }
        }
    }
}