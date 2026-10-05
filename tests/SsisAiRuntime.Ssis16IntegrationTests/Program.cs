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

        private static int Main()
        {
            var directory = Path.Combine(Path.GetTempPath(), "SsisAiRuntimeFixture-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var path = Path.Combine(directory, "Conversion.dtsx");
                stage = "fixture.creation";
                Require(Environment.Is64BitProcess, "fixture.architecture");
                CreateFixture(path);
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
                    return 0;
                }
                finally
                {
                    load.Session.Package.Dispose();
                }
            }
            catch (Exception)
            {
                Console.Error.WriteLine("Native Data Conversion verification failed at " + stage + ". Raw exception details are withheld.");
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
            var cli = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SsisAiRuntime.Cli.exe");
            Require(File.Exists(cli), "fixture.cli.missing");
            var arguments = string.Join(" ", new[] { "trace", path, "--flow", flowId, "--component", componentId,
                "--column", columnId, "--direction", upstream ? "upstream" : "downstream" }.Select(Quote));
            using (var process = new Process
            {
                StartInfo = new ProcessStartInfo(cli, arguments)
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
                Require(process.ExitCode == 0, "fixture.cli.exit");
                var report = JObject.Parse(stdout.Result);
                Require((string)report["schemaVersion"] == "1.0" && (bool)report["succeeded"] && (bool)report["isComplete"], "fixture.cli.envelope");
                Require((bool)report["redaction"]["applied"] && (bool)report["redaction"]["expressionTextOmitted"] &&
                    (bool)report["redaction"]["settingValuesOmitted"], "fixture.cli.redaction");
                if (expectedKind != null)
                {
                    Require(report["results"]["trace"]["links"].Any(link => (string)link["kind"] == expectedKind), "fixture.cli.mapping");
                }
                if (forbiddenText != null) { Require(!stdout.Result.Contains(forbiddenText), "fixture.cli.literal"); }
                Require(!report.Descendants().OfType<JProperty>().Any(property =>
                    property.Name == "statementText" || property.Name == "description" || property.Name == "value" || property.Name == "expression"), "fixture.cli.values");
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