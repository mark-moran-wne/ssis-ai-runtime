using System;
using System.Collections.Generic;
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
using DtsRuntime = Microsoft.SqlServer.Dts.Runtime;

namespace SsisAiRuntime.Ssis16IntegrationTests
{
    internal static class Program
    {
        private static string stage = "initialization";

        private static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--create-designer-fixtures")
            {
                return CreateDesignerFixtures(args[1]);
            }
            if (args.Length == 2 && args[0] == "--inspect-designer-fixtures")
            {
                return InspectDesignerFixtures(args[1]);
            }
            if (args.Length == 1 && args[0] == "--designer-layout-probe")
            {
                return DesignerLayoutTests.Run();
            }
            if (args.Length == 1 && args[0] == "--column-resize-probe")
            {
                return ColumnResizeLifecycleTests.Run();
            }
            if (args.Length == 1 && args[0] == "--mutation-host-probe")
            {
                return MutationHostLifecycleTests.Run();
            }
            if (args.Length == 1 && args[0] == "--execute-flow-probe")
            {
                return VerifyFlowExecution();
            }
            if (args.Length > 1)
            {
                Console.Error.WriteLine("Usage: SsisAiRuntime.Ssis16IntegrationTests.exe [package.dtsx | --execute-flow-probe | --mutation-host-probe | --column-resize-probe | --designer-layout-probe | --create-designer-fixtures <directory> | --inspect-designer-fixtures <directory>]");
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
                stage = "pipeline.in_memory.edits";
                var editApplication = new Application();
                InMemoryPipelineEditTests.Run(prefix => FindComponent(editApplication, prefix));
                stage = "pipeline.in_memory.schema_edits";
                InMemorySchemaEditTests.Run(prefix => FindComponent(editApplication, prefix));
                stage = "flow.component.catalog";
                VerifyComponentCatalog();
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
                VerifyCorpusCli(path, directory);
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
                    VerifyHeuristicExpressions(directory);
                    VerifySqlDependencies(directory);
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

        private static int CreateDesignerFixtures(string directory)
        {
            try
            {
                var paths = DesignerLayoutFixtureGenerator.Create(directory);
                DesignerLayoutFixtureGenerator.Verify(Path.GetFullPath(directory));
                Console.WriteLine("Created and native-reload verified " + paths.Count + " synthetic layout fixtures with no coordinates. Open them in SSDT, arrange and save each, then run --inspect-designer-fixtures against the saved directory.");
                foreach (var path in paths) { Console.WriteLine(Path.GetFileName(path)); }
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("Designer fixture generation failed: " + error.Message);
                return 1;
            }
        }

        private static int InspectDesignerFixtures(string directory)
        {
            try
            {
                DesignerLayoutFixtureGenerator.Inspect(directory);
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("Designer fixture inspection failed: " + error.Message);
                return 1;
            }
        }

        private static void VerifySqlDependencies(string directory)
        {
            stage = "sql.fixture.creation";
            var path = Path.Combine(directory, "SqlDependencies.dtsx");
            var application = new Application();
            using (var package = new Package { Name = "SqlDependencies", ProtectionLevel = DTSProtectionLevel.DontSaveSensitive })
            {
                var contextVariable = package.Variables.Add("ContextValue", false, "User", "variable-fixture-secret");
                contextVariable.Expression = "\"expression-fixture-secret\"";
                contextVariable.EvaluateAsExpression = true;
                foreach (var suffix in new[] { "A", "B" })
                {
                    stage = "sql.fixture.connection";
                    var connection = package.Connections.Add("OLEDB");
                    connection.Name = "SqlConnection" + suffix;
                    stage = "sql.fixture.provider";
                    connection.ConnectionString = "Provider=SQLOLEDB;Data Source=(local);Initial Catalog=Fixture" + suffix + ";Integrated Security=SSPI;";
                    stage = "sql.fixture.task";
                    var host = (TaskHost)package.Executables.Add("STOCK:SQLTask");
                    host.Name = "SqlTask" + suffix;
                    var task = host.InnerObject;
                    var type = task.GetType();
                    stage = "sql.fixture.task.connection";
                    type.GetProperty("Connection").SetValue(task, connection.ID, null);
                    stage = "sql.fixture.source.type";
                    var sourceType = type.GetProperty("SqlStatementSourceType");
                    sourceType.SetValue(task, Enum.Parse(sourceType.PropertyType, "DirectInput"), null);
                    stage = "sql.fixture.statement";
                    type.GetProperty("SqlStatementSource").SetValue(task,
                        "-- select * from dbo.CommentOnly\n select 'sql-fixture-secret' as LiteralValue; select * from dbo.Student; exec dbo.LoadStudent; select dbo.GetTerm(); exec sys.sp_executesql @sql;", null);
                }
                stage = "sql.fixture.save";
                application.SaveToXml(path, package, null);
            }
            stage = "sql.fixture.reload";
            var before = Hash(path);
            var loaded = new PackageLoader().Load(path);
            stage = "sql.fixture.load.result";
            Require(loaded.Succeeded, "sql.load");
            try
            {
                stage = "sql.fixture.inspect";
                var sql = new PackageSqlInspector().InspectDetailed(loaded.Session);
                stage = "sql.fixture.analysis";
                Console.WriteLine("Native SQL coverage: tasks=" + sql.Items.Count + "; parsed=" + sql.Items.Count(item => item.SqlDependencies != null && item.SqlDependencies.Parsed) +
                    "; codes=" + string.Join(",", sql.UnsupportedItems.Select(gap => gap.ReasonCode).Distinct()));
                Require(sql.Items.Count == 2 && sql.Items.All(item => item.SqlDependencies != null && item.SqlDependencies.Parsed), "sql.analysis");
                Require(sql.Items.All(item => item.SqlDependencies.References.Count == 3), "sql.references");
                Require(sql.UnsupportedItems.Any(gap => gap.ReasonCode == "sql.dynamic_sql"), "sql.dynamic");
                var graph = new PackageAnalysisSnapshotFactory().Create(loaded.Session).Items.Single().Dependencies;
                Require(graph.Nodes.Count(node => node.Kind == SemanticObjectKind.SchemaObject && node.Name == "[dbo].[Student]") == 2, "sql.connection.scope");
                Require(graph.Edges.Any(edge => edge.Kind == DependencyKind.ReadsSchemaObject && edge.Evidence == "ParsedSchemaObject") &&
                    graph.Edges.Any(edge => edge.Kind == DependencyKind.ExecutesSchemaObject && edge.Evidence == "ParsedExecuteTarget") &&
                    graph.Edges.Any(edge => edge.Kind == DependencyKind.ReferencesSqlFunction && edge.Evidence == "ParsedFunctionReference"), "sql.evidence");
                stage = "sql.cli.default";
                var cli = RunCli(new[] { "ai", "dependency.graph", path }, 0, 5);
                RequireNoValues(cli);
                Require(!cli.ToString().Contains("sql-fixture-secret") && !cli.ToString().Contains("CommentOnly"), "sql.redaction");
                Require(((JArray)cli["results"]["edges"]).Any(edge => (string)edge["evidence"] == "ParsedSchemaObject"), "sql.cli.evidence");
                var focused = RunCli(new[] { "sql", path, "--details" }, 0, 5);
                RequireNoValues(focused);
                Require(!focused.ToString().Contains("sql-fixture-secret") && !focused.ToString().Contains("CommentOnly"), "sql.focused.redaction");
                stage = "sql.cli.context.default";
                var metadata = RunCli(new[] { "ai", "context", path }, 0, 5);
                Require(metadata["results"]["sanitizedTexts"] == null && (string)metadata["redaction"]["policy"] == "metadata-only", "context.default");
                RequireNoValues(metadata);
                stage = "sql.cli.context.optin";
                var context = RunCli(new[] { "ai", "context", path, "--include-sanitized-text" }, 0, 5);
                Require((string)context["redaction"]["policy"] == "sanitized-context-opt-in" && (bool)context["redaction"]["rawTextOmitted"], "context.policy");
                var texts = (JArray)context["results"]["sanitizedTexts"];
                Require(texts.Any(text => (string)text["kind"] == "Sql") && texts.Any(text => (string)text["kind"] == "Expression"), "context.snippets");
                Require(context["results"]["dependencyNodes"] != null && context["results"]["controlFlowFacts"] != null && context["results"]["lineageFacts"] != null, "context.evidence");
                Require(!context.ToString().Contains("sql-fixture-secret") && !context.ToString().Contains("expression-fixture-secret") &&
                    !context.ToString().Contains("variable-fixture-secret") && !context.ToString().Contains("CommentOnly"), "context.redaction");
                Require(texts.All(text => ((string)text["sanitizedText"] ?? "").Length <= SanitizedTextFactory.MaximumTextLength &&
                    (string)text["trust"] == "untrusted-package-content"), "context.bounds");
                RunCli(new[] { "ai", "context", path, "--include-sanitized-text", "--include-sanitized-text" }, 2);
                RunCli(new[] { "ai", "dependency.graph", path, "--include-sanitized-text" }, 2);
                Require(Hash(path) == before, "sql.hash");
                Console.WriteLine("Parsed SQL verification: PASS; connection-scoped schema objects, parsed evidence, dynamic gaps, default redaction, unchanged hash; no database connection, execution, or validation.");
            }
            finally { loaded.Session.Package.Dispose(); }
        }

        private static void VerifyCorpusCli(string packagePath, string directory)
        {
            var baselinePath = Path.Combine(directory, "corpus-baseline.json");
            stage = "corpus.snapshot";
            var snapshot = RunCli(new[] { "ai", "corpus.snapshot", packagePath }, 0, 5);
            Require((string)snapshot["tool"] == "corpus.snapshot" &&
                (string)snapshot["results"]!["snapshot"]!["schemaVersion"] == "1.0" &&
                (int)snapshot["results"]!["snapshot"]!["nodeCount"] > 0, "corpus.snapshot.result");

            stage = "corpus.approve";
            var approved = RunCli(new[] { "ai", "corpus.approve", packagePath, "--baseline", baselinePath }, 0, 5);
            Require((bool)approved["results"]!["approved"]! && File.Exists(baselinePath), "corpus.approve.result");

            stage = "corpus.diff.match";
            var diff = RunCli(new[] { "ai", "corpus.diff", packagePath, "--baseline", baselinePath }, 0, 5);
            Require((bool)diff["results"]!["isMatch"]!, "corpus.diff.match");

            stage = "corpus.verify.match";
            var verified = RunCli(new[] { "ai", "corpus.verify", packagePath, "--baseline", baselinePath }, 0, 5);
            Require((bool)verified["results"]!["isMatch"]! && ((JArray)verified["diagnostics"]!).Count == 0,
                "corpus.verify.match");

            var baseline = JObject.Parse(File.ReadAllText(baselinePath));
            baseline["snapshot"]!["nodes"]![0]!["name"] = "changed-baseline-metadata";
            File.WriteAllText(baselinePath, baseline.ToString());
            stage = "corpus.verify.mismatch";
            var mismatch = RunCli(new[] { "ai", "corpus.verify", packagePath, "--baseline", baselinePath }, 4);
            Require(!(bool)mismatch["succeeded"]! &&
                (string)mismatch["diagnostics"]![0]!["code"] == "corpus.verify.mismatch" &&
                ((JArray)mismatch["results"]!["changes"]!["changedNodes"]!).Count > 0,
                "corpus.verify.mismatch.result");

            baseline["schemaVersion"] = "2.0";
            File.WriteAllText(baselinePath, baseline.ToString());
            stage = "corpus.approve.upgrade.required";
            var upgradeRequired = RunCli(new[] { "ai", "corpus.approve", packagePath, "--baseline", baselinePath }, 4);
            Require((string)upgradeRequired["diagnostics"]![0]!["code"] == "corpus.baseline.incompatible",
                "corpus.approve.upgrade.required.result");
            stage = "corpus.approve.upgrade";
            var upgraded = RunCli(new[] { "ai", "corpus.approve", packagePath, "--baseline", baselinePath, "--upgrade" }, 0, 5);
            Require((bool)upgraded["results"]!["approved"]! && (bool)upgraded["results"]!["upgraded"]! &&
                (string)JObject.Parse(File.ReadAllText(baselinePath))["schemaVersion"] == "1.0",
                "corpus.approve.upgrade.result");
            Console.WriteLine("Corpus baseline lifecycle: PASS; snapshot, approve, matching diff/verify, mismatch failure, and explicit schema upgrade.");
        }

        private static void VerifyHeuristicExpressions(string directory)
        {
            stage = "heuristic.fixture.creation";
            var path = Path.Combine(directory, "ExpressionScopes.dtsx");
            using (var package = new Package { Name = "ExpressionScopes", ProtectionLevel = DTSProtectionLevel.DontSaveSensitive })
            {
                package.Variables.Add("Flag", false, "User", false);
                package.Parameters.Add("Toggle", TypeCode.Boolean).Value = false;
                package.SetExpression("Disable", "@[User::Flag]");
                package.SetExpression("DelayValidation", "@[$Package::Toggle]");
                package.Variables.Add("FixturePath", false, "User", Path.Combine(directory, "unused.txt"));
                var connection = package.Connections.Add("FILE");
                connection.Name = "ConnectionOwner";
                connection.SetExpression("ConnectionString", "@[User::FixturePath]");
                var task = (TaskHost)package.Executables.Add("STOCK:PipelineTask");
                task.Name = "TaskOwner";
                task.Variables.Add("Flag", false, "User", true);
                task.SetExpression("Disable", "@[User::Flag] || @[User::Flag]");
                task.SetExpression("DelayValidation", "@[$Package::Toggle]");
                var outer = (Sequence)package.Executables.Add("STOCK:Sequence");
                outer.Name = "OuterOwner";
                outer.Variables.Add("Flag", false, "User", true);
                outer.SetExpression("Disable", "@[User::Flag]");
                var inner = (Sequence)outer.Executables.Add("STOCK:Sequence");
                inner.Name = "InnerOwner";
                inner.Variables.Add("Flag", false, "User", false);
                inner.SetExpression("Disable", "@[User::Flag]");
                var child = (TaskHost)inner.Executables.Add("STOCK:PipelineTask");
                child.Name = "ChildOwner";
                child.SetExpression("Disable", "@[User::Flag]");
                var loop = (ForLoop)package.Executables.Add("STOCK:ForLoop");
                loop.Name = "LoopOwner";
                loop.Variables.Add("Counter", false, "User", 0);
                loop.InitExpression = "@[User::Counter] = 0";
                loop.EvalExpression = "@[User::Counter] < 2";
                loop.AssignExpression = "@[User::Counter] = @[User::Counter] + 1";
                var constraint = inner.PrecedenceConstraints.Add(child, (TaskHost)inner.Executables.Add("STOCK:PipelineTask"));
                constraint.EvalOp = DTSPrecedenceEvalOp.ExpressionAndConstraint;
                constraint.Expression = "@[User::Flag]";
                var handler = (DtsEventHandler)task.EventHandlers.Add("OnError");
                handler.Variables.Add("HandlerFlag", false, "User", false);
                handler.Variables.Add("Flag", false, "User", false);
                handler.SetExpression("Disable", "@[User::Flag]");
                handler.SetExpression("DelayValidation", "@[$Package::Toggle]");
                package.Variables.Add("Number", false, "User", 1);
                var expressionVariable = package.Variables.Add("Calculated", false, "User", 0);
                expressionVariable.Expression = "@[User::Number] + 1";
                expressionVariable.EvaluateAsExpression = true;
                new Application().SaveToXml(path, package, null);
            }
            stage = "heuristic.fixture.reload";
            var before = Hash(path);
            var loaded = new PackageLoader().Load(path);
            Require(loaded.Succeeded, "heuristic.load");
            try
            {
                var report = new PackageExpressionDependencyInspector().Inspect(loaded.Session);
                Require(report.Catalog != null, "heuristic.catalog");
                Require(report.Analyses.UnsupportedItems.Count == 0, "heuristic.coverage");
                var package = loaded.Session.Package;
                var task = (TaskHost)package.Executables.Cast<Executable>().Single(item => ((IDTSName)item).Name == "TaskOwner");
                var outer = (Sequence)package.Executables.Cast<Executable>().Single(item => ((IDTSName)item).Name == "OuterOwner");
                var inner = (Sequence)outer.Executables.Cast<Executable>().Single();
                var child = (TaskHost)inner.Executables.Cast<Executable>().Single(item => ((IDTSName)item).Name == "ChildOwner");
                foreach (var owner in new DtsContainer[] { package, task, outer, inner, child })
                {
                    var expectedScope = owner == child ? inner.ID : owner.ID;
                    var resolutions = report.Analyses.Items.Where(item => item.OwnerNativeId == owner.ID)
                        .SelectMany(item => item.Resolutions).Where(item => item.Reference.Name == "Flag").ToArray();
                    Require(resolutions.Length > 0 && resolutions.All(item => item.ResolvedSymbol != null && item.ResolvedSymbol.ScopeId == expectedScope), "heuristic.shadowing");
                    var scope = report.Catalog.Scopes.Single(item => item.Id == owner.ID);
                    Require(scope.Symbols.All(symbol => symbol.ScopeId == owner.ID), "heuristic.declarations");
                }
                Require(report.Analyses.Items.SelectMany(item => item.Resolutions).Any(item => item.ResolvedSymbol != null &&
                    item.ResolvedSymbol.Kind == SsisAiRuntime.Inspectors.Expressions.ExpressionSymbolKind.PackageParameter), "heuristic.parameter");
                Require(report.Analyses.Items.Where(item => item.PropertyName == "LoopExpression").Count() == 3 &&
                    report.Analyses.Items.Where(item => item.PropertyName == "LoopExpression").All(item => item.IsComplete), "heuristic.loop");
                Require(report.Analyses.Items.Any(item => item.PropertyName == "ConstraintExpression" && item.IsComplete), "heuristic.constraint");
                Require(report.Catalog.Scopes.Any(scope => scope.Kind == SsisAiRuntime.Inspectors.Expressions.ExpressionScopeKind.EventHandler &&
                    scope.Symbols.Any(symbol => symbol.Name == "HandlerFlag")), "heuristic.handler");
                var snapshot = new PackageAnalysisSnapshotFactory().Create(loaded.Session).Items.Single();
                var handler = task.EventHandlers.Cast<DtsEventHandler>().Single();
                var connection = package.Connections.Cast<ConnectionManager>().Single();
                VerifyExpressionOwner(report, snapshot.Dependencies, package.Variables["User::Calculated"].ID, package.ID,
                    "Number", package.Variables["User::Number"].ID, DependencyKind.UsesVariable);
                VerifyExpressionOwner(report, snapshot.Dependencies, task.ID, task.ID,
                    "Flag", task.Variables["User::Flag"].ID, DependencyKind.UsesVariable);
                VerifyExpressionOwner(report, snapshot.Dependencies, connection.ID, package.ID,
                    "FixturePath", package.Variables["User::FixturePath"].ID, DependencyKind.UsesVariable);
                VerifyExpressionOwner(report, snapshot.Dependencies, handler.ID, handler.ID,
                    "Flag", handler.Variables["User::Flag"].ID, DependencyKind.UsesVariable);
                foreach (var owner in new[] { package.ID, task.ID, handler.ID })
                {
                    VerifyExpressionOwner(report, snapshot.Dependencies, owner, owner,
                        "Toggle", package.Parameters["Toggle"].ID, DependencyKind.UsesParameter);
                }
                var eventScope = report.Catalog.Scopes.Single(scope => scope.Kind == SsisAiRuntime.Inspectors.Expressions.ExpressionScopeKind.EventHandler);
                Require(snapshot.Dependencies.Edges.Any(edge => edge.Kind == DependencyKind.ContainsTask &&
                    edge.From == "Executable:" + task.ID && edge.To == "Executable:" + eventScope.Id), "heuristic.handler.containment");
                var invalidProject = new[] { new SsisAiRuntime.Inspectors.Expressions.ExpressionSymbol("invalid-project", "missing-scope",
                    SsisAiRuntime.Inspectors.Expressions.ExpressionSymbolKind.ProjectParameter, "$Project", "Unavailable") };
                var invalidSnapshot = new PackageAnalysisSnapshotFactory().Create(loaded.Session, invalidProject).Items.Single();
                Require(invalidSnapshot.Dependencies.UnsupportedItems.Any(gap => gap.ReasonCode == "expression.target_not_projected") &&
                    !invalidSnapshot.Dependencies.Edges.Any(edge => edge.Kind == DependencyKind.UsesVariable || edge.Kind == DependencyKind.UsesParameter), "heuristic.catalog.failure");
                var edges = snapshot.Dependencies.Edges.Where(edge => edge.Kind == DependencyKind.UsesVariable || edge.Kind == DependencyKind.UsesParameter).ToArray();
                Require(edges.Length >= 7 && edges.All(edge => edge.Evidence == "LexicalAndScopeResolved"), "heuristic.edges");
                stage = "heuristic.cli.graph";
                var cli = RunCli(new[] { "ai", "dependency.graph", path }, 0, 5);
                var projectedEdges = ((JArray)cli["results"]["edges"]).Where(edge => (string)edge["kind"] == "UsesVariable" || (string)edge["kind"] == "UsesParameter").ToArray();
                Require(projectedEdges.Length > 0 && projectedEdges.All(edge => (string)edge["evidence"] == "LexicalAndScopeResolved"), "heuristic.cli.evidence");
                RequireNoValues(cli);
                Require(!cli.ToString().Contains("@[User::Flag]") && !cli.ToString().Contains("@[$Package::Toggle]"), "heuristic.cli.text");
                Require(Hash(path) == before, "heuristic.hash");
                Console.WriteLine("Heuristic expression verification: PASS; native declaration IDs, nearest-scope shadowing, package parameters, redaction, CLI evidence, and unchanged hash; no execution or validation.");
            }
            finally { loaded.Session.Package.Dispose(); }
        }

        private static void VerifyExpressionOwner(SsisAiRuntime.Inspectors.Expressions.ExpressionDependencyInspection report,
            PackageDependencyGraph graph, string ownerId, string scopeId, string referenceName, string targetId, DependencyKind kind)
        {
            var resolutions = report.Analyses.Items.Where(item => item.OwnerNativeId == ownerId && item.OwnerScopeId == scopeId)
                .SelectMany(item => item.Resolutions).Where(item => item.Reference.Name == referenceName).ToArray();
            Require(resolutions.Length > 0 && resolutions.All(item => item.ResolvedSymbol != null && item.ResolvedSymbol.NativeId == targetId), "heuristic.owner.resolution");
            var owner = graph.Nodes.Single(node => node.NativeId == ownerId && node.Kind != SemanticObjectKind.DataFlow);
            var target = graph.Nodes.Single(node => node.NativeId == targetId);
            Require(graph.Edges.Any(edge => edge.From == owner.Key && edge.To == target.Key && edge.Kind == kind &&
                edge.Evidence == "LexicalAndScopeResolved"), "heuristic.owner.edge");
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
                var dependencies = new PackageAnalysisSnapshotFactory().Create(load.Session).Items.Single().Dependencies;
                var variableEdge = dependencies.Edges.Single(edge => edge.From == "DataFlowComponent:" + flow.ExecutableId + ":" + derive.Id && edge.Kind == DependencyKind.UsesVariable);
                var target = dependencies.Nodes.Single(node => node.Key == variableEdge.To);
                Require(target.ParentId == flow.ExecutableId && variableEdge.Evidence == "LexicalAndScopeResolved", "derived.heuristic.scope");
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
            var taskHost = (TaskHost)package.Executables[0];
            var pipeline = (IDTSPipeline130)taskHost.InnerObject;
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
                ProbeVariableParameterBindings(package, taskHost, columns, parser);
                ProbeUnwrappedReferenceSyntax(package, columns, parser);
            }
            finally
            {
                if (System.Runtime.InteropServices.Marshal.IsComObject(evaluator))
                {
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(evaluator);
                }
            }
        }

        private static void ProbeUnwrappedReferenceSyntax(Package package, IDTSInputColumnCollection100 columns, IDTSExpressionEvaluatorEx100 parser)
        {
            stage = "expression.unwrapped.syntax";
            var candidates = new[]
            {
                new { Label = "simple", Text = "@ParserFlag", Expected = true },
                new { Label = "dot", Text = "@User.ParserFlag", Expected = false },
                new { Label = "namespace", Text = "@User::ParserFlag", Expected = false },
                new { Label = "package-namespace", Text = "@Package::ProbeParameter", Expected = false },
                new { Label = "package-dollar", Text = "@$Package::ProbeParameter", Expected = false },
                new { Label = "dollar", Text = "@Parser$Flag", Expected = false },
                new { Label = "hash", Text = "@Parser#Flag", Expected = false },
                new { Label = "wrapped-variable", Text = "@[User::ParserFlag]", Expected = true },
                new { Label = "wrapped-parameter", Text = "@[$Package::ProbeParameter]", Expected = true }
            };
            foreach (var candidate in candidates)
            {
                var parsed = false;
                try
                {
                    parser.Parse(candidate.Text, DtsConvert.GetExtendedInterface(package.VariableDispenser), new NativeExpressionInputColumns(columns));
                    parsed = true;
                }
                catch { }
                var lexical = new SsisAiRuntime.Inspectors.Expressions.ExpressionReferenceParser().Analyze(candidate.Text);
                Require(parsed == candidate.Expected && lexical.Succeeded == candidate.Expected &&
                    lexical.References.Count == (candidate.Expected ? 1 : 0), "expression.unwrapped.syntax." + candidate.Label);
                Console.WriteLine("Native unwrapped syntax " + candidate.Label + ": parsed=" + parsed +
                    "; lexicalParsed=" + lexical.Succeeded + "; candidates=" + lexical.References.Count + "; bindingNotProven=True.");
            }
        }

        private static void ProbeVariableParameterBindings(Package package, TaskHost taskHost,
            IDTSInputColumnCollection100 columns, IDTSExpressionEvaluatorEx100 parser)
        {
            stage = "expression.binding.fixture.variables";
            package.Variables.Add("ScopeProbe", false, "User", "package-fixture-value");
            taskHost.Variables.Add("ScopeProbe", false, "User", true);
            stage = "expression.binding.fixture.nested";
            var outerExecutable = package.Executables.Add("STOCK:Sequence");
            ((IDTSName)outerExecutable).Name = "OuterScopeProbe";
            var outerSequence = (DtsRuntime.DtsContainer)outerExecutable;
            var outerVariable = outerSequence.Variables.Add("NestedProbe", false, "User", true);
            var innerExecutable = ((DtsRuntime.IDTSSequence)outerExecutable).Executables.Add("STOCK:Sequence");
            ((IDTSName)innerExecutable).Name = "InnerScopeProbe";
            var innerSequence = (DtsRuntime.DtsContainer)innerExecutable;
            var innerVariable = innerSequence.Variables.Add("NestedProbe", false, "User", false);

            stage = "expression.binding.fixture.parameter";
            var packageParameter = package.Parameters.Add("ProbeParameter", TypeCode.String);
            packageParameter.Value = "parameter-fixture-value";

            var scopes = new[]
            {
                new NativeExpressionScope("Package", package.ID, package.Name),
                new NativeExpressionScope("Task", ((IDTSName)taskHost).ID, ((IDTSName)taskHost).Name),
                new NativeExpressionScope("Sequence", ((IDTSName)outerExecutable).ID, ((IDTSName)outerExecutable).Name),
                new NativeExpressionScope("Sequence", ((IDTSName)innerExecutable).ID, ((IDTSName)innerExecutable).Name)
            };

            stage = "expression.binding.package-scope";
            var packageProbe = ProbeVariableExpression("package-scope-shadow", "@[User::ScopeProbe] == \"package-fixture-value\"",
                package.VariableDispenser, scopes, columns, parser);
            Require(packageProbe.Parsed, "expression.binding.package-parse");
            ReportVariableBindingProbe("package-scope-shadow", packageProbe, package.ID);

            stage = "expression.binding.task-scope";
            var taskProbe = ProbeVariableExpression("task-scope-shadow", "@[User::ScopeProbe] ? 1 : 0",
                taskHost.VariableDispenser, scopes, columns, parser);
            var taskId = ((IDTSName)taskHost).ID;
            Require(taskProbe.Parsed, "expression.binding.task-parse");
            ReportVariableBindingProbe("task-scope-shadow", taskProbe, taskId);

            var outerId = ((IDTSName)outerExecutable).ID;
            stage = "expression.binding.outer-scope";
            var outerProbe = ProbeVariableExpression("outer-container-shadow", "@[User::NestedProbe] ? 1 : 0",
                outerSequence.VariableDispenser, scopes, columns, parser);
            Require(outerProbe.Parsed, "expression.binding.outer-parse");
            ReportVariableBindingProbe("outer-container-shadow", outerProbe, outerId);
            var innerId = ((IDTSName)innerExecutable).ID;
            stage = "expression.binding.inner-scope";
            var innerProbe = ProbeVariableExpression("nested-container-shadow", "@[User::NestedProbe] ? 1 : 0",
                innerSequence.VariableDispenser, scopes, columns, parser);
            Require(innerProbe.Parsed, "expression.binding.inner-parse");
            ReportVariableBindingProbe("nested-container-shadow", innerProbe, innerId);
            Require(outerVariable.QualifiedName == innerVariable.QualifiedName && outerId != innerId,
                "expression.binding.nested-fixture");
            Console.WriteLine("Native nested-scope fixture: same qualified name exists in distinct containers.");

            stage = "expression.binding.constant";
            var constantProbe = ProbeVariableExpression("constant", "1 + 2", taskHost.VariableDispenser,
                scopes, columns, parser);
            Require(constantProbe.Parsed && constantProbe.EmittedReferences.Length == 0, "expression.binding.constant");
            Console.WriteLine("Native constant syntax probe: parsed=True; observedBindings=" +
                constantProbe.ProvisionalReferences.Length + ".");

            stage = "expression.binding.malformed";
            var malformedProbe = ProbeVariableExpression("malformed-after-reference", "@[User::ScopeProbe] +",
                taskHost.VariableDispenser, scopes, columns, parser);
            Require(!malformedProbe.Parsed && malformedProbe.EmittedReferences.Length == 0,
                "expression.binding.failed-parse-discards");
            Console.WriteLine("Native variable binding malformed parse: parsed=False; provisionalReferences=" +
                malformedProbe.ProvisionalReferences.Length + "; emittedReferences=0.");

            stage = "expression.binding.package-parameter";
            var packageParameterProbe = ProbeVariableExpression("package-parameter", "$Package::ProbeParameter == \"parameter-fixture-value\"",
                package.VariableDispenser, scopes, columns, parser);
            var packageParameterWrappedProbe = ProbeVariableExpression("package-parameter-wrapped", "@[$Package::ProbeParameter] == \"parameter-fixture-value\"",
                package.VariableDispenser, scopes, columns, parser);
            stage = "expression.binding.project-parameter";
            var projectParameterProbe = ProbeVariableExpression("project-parameter", "$Project::ProbeParameter == \"parameter-fixture-value\"",
                package.VariableDispenser, scopes, columns, parser);
            var projectParameterWrappedProbe = ProbeVariableExpression("project-parameter-wrapped", "@[$Project::ProbeParameter] == \"parameter-fixture-value\"",
                package.VariableDispenser, scopes, columns, parser);
            Console.WriteLine("Native package-parameter syntax probe: parsed=" + packageParameterProbe.Parsed +
                "; dispenserReadRequests=" + packageParameterProbe.ReadLockRequests.Length +
                "; observedVariableBindings=" + packageParameterProbe.ProvisionalReferences.Length + ".");
            Console.WriteLine("Native wrapped package-parameter syntax probe: parsed=" + packageParameterWrappedProbe.Parsed +
                "; dispenserReadRequests=" + packageParameterWrappedProbe.ReadLockRequests.Length +
                "; observedVariableBindings=" + packageParameterWrappedProbe.ProvisionalReferences.Length + ".");
            Console.WriteLine("Native project-parameter syntax probe in standalone package: parsed=" + projectParameterProbe.Parsed +
                "; dispenserReadRequests=" + projectParameterProbe.ReadLockRequests.Length +
                "; observedVariableBindings=" + projectParameterProbe.ProvisionalReferences.Length +
                "; project-backed fixture unavailable.");
            Console.WriteLine("Native wrapped project-parameter syntax probe in standalone package: parsed=" + projectParameterWrappedProbe.Parsed +
                "; dispenserReadRequests=" + projectParameterWrappedProbe.ReadLockRequests.Length +
                "; observedVariableBindings=" + projectParameterWrappedProbe.ProvisionalReferences.Length +
                "; project-backed fixture unavailable.");
        }

        private static void ReportVariableBindingProbe(string label, NativeExpressionProbeResult result, string expectedScopeId)
        {
            var scopeMatched = result.EmittedReferences.Length == 1 && result.EmittedReferences[0].ScopeId == expectedScopeId;
            Console.WriteLine("Native variable binding probe " + label + ": parsed=" + result.Parsed +
                "; readLockRequests=" + result.ReadLockRequests.Length +
                "; observedBindings=" + result.ProvisionalReferences.Length +
                "; expectedScopeObserved=" + scopeMatched + "; nativeId=" +
                (result.ProvisionalReferences.Any(binding => binding.NativeId.Length > 0) ? "available" : "unavailable") + ".");
        }

        private static NativeExpressionProbeResult ProbeVariableExpression(string label, string expression,
            VariableDispenser dispenser, IEnumerable<NativeExpressionScope> scopes,
            IDTSInputColumnCollection100 columns, IDTSExpressionEvaluatorEx100 parser)
        {
            var observer = new NativeExpressionVariableDispenserObserver(DtsConvert.GetExtendedInterface(dispenser), scopes);
            var columnObserver = new NativeExpressionInputColumns(columns);
            var before = SnapshotColumns(columns);
            var parsed = false;
            try
            {
                parser.Parse(expression, observer, columnObserver);
                parsed = true;
            }
            catch (Exception)
            {
                parsed = false;
            }

            Require(before == SnapshotColumns(columns) && columnObserver.MutationAttempts == 0,
                "expression.binding.column-collection-read-only." + label);
            Require(observer.WriteAttempts == 0 && observer.MutationAttempts == 0 && observer.ValueReadAttempts == 0,
                "expression.binding.variable-observer-read-only." + label);
            var provisional = observer.Bindings.ToArray();
            var emitted = parsed ? provisional : Array.Empty<NativeExpressionVariableBinding>();
            return new NativeExpressionProbeResult(parsed, observer.ReadLockRequests.ToArray(), provisional, emitted);
        }

        private sealed class NativeExpressionProbeResult
        {
            public NativeExpressionProbeResult(bool parsed, string[] readLockRequests,
                NativeExpressionVariableBinding[] provisionalReferences, NativeExpressionVariableBinding[] emittedReferences)
            {
                Parsed = parsed;
                ReadLockRequests = readLockRequests;
                ProvisionalReferences = provisionalReferences;
                EmittedReferences = emittedReferences;
            }

            public bool Parsed { get; }
            public string[] ReadLockRequests { get; }
            public NativeExpressionVariableBinding[] ProvisionalReferences { get; }
            public NativeExpressionVariableBinding[] EmittedReferences { get; }
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

        private static void VerifyComponentCatalog()
        {
            var runner = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SsisAiRuntime.FlowRunner.exe");
            var catalog = RunExecutable(runner, new[] { "components" }, 0);
            var repeated = RunExecutable(runner, new[] { "components" }, 0);
            Require(JToken.DeepEquals(catalog, repeated), "flow.catalog.deterministic");
            Require((string)catalog["command"] == "flow.components" && !(bool)catalog["packageExecuted"] &&
                !(bool)catalog["databaseConnections"], "flow.catalog.readonly");
            var components = (JArray)catalog["components"];
            var native = new Application().PipelineComponentInfos.Cast<PipelineComponentInfo>().ToArray();
            Require(components.Count == native.Length && (int)catalog["componentCount"] == native.Length,
                "flow.catalog.count");
            Require(components.Select(component => (string)component["creationName"])
                .OrderBy(name => name, StringComparer.Ordinal)
                .SequenceEqual(native.Select(component => component.CreationName).OrderBy(name => name, StringComparer.Ordinal)),
                "flow.catalog.registration.coverage");
            Require(components.All(component => (bool)component["discovered"] &&
                !(bool)component["executionTestedThisInvocation"]), "flow.catalog.no_test_claim");
            var supported = components.Where(component => (bool)component["configurable"]).ToArray();
            Require(supported.Length == 4 && (int)catalog["configurableCount"] == supported.Length &&
                supported.All(component => (bool)component["executionTestAvailable"] &&
                    ((string)component["recipe"] == "synthetic-derived-int32" ||
                     (string)component["recipe"] == "synthetic-data-conversion-int32")), "flow.catalog.supported");
            Require(components.Where(component => !(bool)component["configurable"])
                .All(component => !(bool)component["executionTestAvailable"] && component["recipe"].Type == JTokenType.Null),
                "flow.catalog.unsupported");
            RequireNoValues(catalog);
            RunExecutable(runner, new[] { "components", "--execute" }, 2);
            VerifyDeveloperTools(runner);
            Console.WriteLine("Installed SSIS catalog: PASS; " + native.Length +
                " registrations, exact native coverage, deterministic metadata, four recipe-supported components; no execution.");
        }

        private static void VerifyDeveloperTools(string runner)
        {
            foreach (var id in new[] { "microsoft.derived-column", "microsoft.data-conversion" })
            {
                var description = RunExecutable(runner, new[] { "describe", id }, 0);
                Require(!(bool)description["packageExecuted"] &&
                    (string)description["results"]["evidence"] == "NativeInitializedMetadata" &&
                    ((JArray)description["results"]["inputs"]).Count == 1 &&
                    ((JArray)description["results"]["connectionSlots"]).Count == 0, "tools.describe.structure");
                var expectedProperty = id == "microsoft.derived-column" ? "Expression" : "SourceInputColumnLineageID";
                Require(((JArray)description["results"]["outputColumnTemplate"]["properties"])
                    .Any(property => (string)property["name"] == expectedProperty), "tools.describe.column_defaults");
            }
            var missing = RunExecutable(runner, new[] { "describe", "missing-component" }, 4);
            Require((string)missing["diagnostics"][0]["stage"] == "resolve" &&
                !string.IsNullOrWhiteSpace((string)missing["diagnostics"][0]["message"]), "tools.describe.failure");
            var catalog = RunExecutable(runner, new[] { "catalog", "validate" }, 0);
            Require((int)catalog["results"]["definitionCount"] >= 59 &&
                !(bool)catalog["results"]["nativeSupportVerified"], "tools.catalog.validation");
            var invalidPath = Path.Combine(Path.GetTempPath(), "SsisCatalogValidation-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                File.WriteAllText(invalidPath, "{\"schemaVersion\":\"2.0\"}");
                RunExecutable(runner, new[] { "catalog", "validate", invalidPath }, 2);
            }
            finally { File.Delete(invalidPath); }
            var compareRequest = new JObject
            {
                ["schemaVersion"] = "1.0", ["beforeXml"] = "<flow width='4'/>", ["afterXml"] = "<flow width='64'/>"
            };
            var comparison = RunExecutableWithInput(runner, new[] { "compare" }, compareRequest.ToString(), 0);
            Require((int)comparison["results"]["changeCount"] == 1 &&
                !(bool)comparison["results"]["semanticsVerified"], "tools.compare.change");
            Console.WriteLine("Developer tools: PASS; native defaults/templates, missing selector diagnostics, catalog validation, and structural XML comparison.");
        }

        private static int VerifyFlowExecution()
        {
            try
            {
                Require(Environment.Is64BitProcess, "flow.architecture");
                VerifyComponentCatalog();
                var before = Directory.GetDirectories(Path.GetTempPath(), "SsisFlowProbe-*");
                var runner = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SsisAiRuntime.FlowRunner.exe");
                var report = RunExecutable(runner, new[] { "demo" }, 0);
                Require((bool)report["succeeded"] && (string)report["code"] == "flow.completed" &&
                    (int)report["rowCount"] == 3 && !(bool)report["databaseConnections"], "flow.execution.result");
                RequireNoValues(report);
                Require(!report.ToString().Contains(Path.GetTempPath()) &&
                    !report.Descendants().OfType<JProperty>().Any(property =>
                        property.Name == "path" || property.Name == "exception"), "flow.execution.redaction");
                RunExecutable(runner, Array.Empty<string>(), 2);
                RunExecutable(runner, new[] { "execute", "production.dtsx" }, 2);
                var request = new JObject
                {
                    ["schemaVersion"] = "1.0",
                    ["values"] = new JArray(-2, 0, 3),
                    ["expression"] = "Value * 2",
                    ["expectedValues"] = new JArray(-4, 0, 6)
                };
                var configured = RunExecutableWithInput(runner, new[] { "run" }, request.ToString(), 0);
                Require((string)configured["command"] == "flow.run" && (int)configured["rowCount"] == 3,
                    "flow.configured.result");
                RequireNoValues(configured);
                var namedProbe = RunExecutableWithInput(runner, new[] { "probe", "derived-column" }, request.ToString(), 0);
                Require((string)namedProbe["command"] == "flow.probe", "tools.probe.alias");
                RunExecutableWithInput(runner, new[] { "probe", "data-conversion" }, request.ToString(), 2);
                request["expression"] = "Value < 0 ? -Value : Value";
                request["expectedValues"] = new JArray(2, 0, 3);
                RunExecutableWithInput(runner, new[] { "run" }, request.ToString(), 0);
                request["expectedValues"] = new JArray(99, 99, 99);
                var mismatch = RunExecutableWithInput(runner, new[] { "run" }, request.ToString(), 4);
                Require(!(bool)mismatch["succeeded"] && (string)mismatch["code"] == "flow.assertions.failed",
                    "flow.configured.mismatch");
                Require(((JArray)mismatch["diagnostics"]).Any(item => (string)item["code"] == "flow.assertion.mismatch" &&
                    item["expected"] != null && item["actual"] != null), "tools.probe.mismatch_details");
                request["expression"] = "Value +";
                var invalidExpression = RunExecutableWithInput(runner, new[] { "run" }, request.ToString(), 4);
                Require(!(bool)invalidExpression["succeeded"], "flow.configured.expression_invalid");
                request["schemaVersion"] = "2.0";
                var invalidRequest = RunExecutableWithInput(runner, new[] { "run" }, request.ToString(), 2);
                Require((string)invalidRequest["code"] == "flow.request.invalid", "flow.configured.schema_invalid");
                var conversion = new JObject
                {
                    ["schemaVersion"] = "1.1", ["recipe"] = "data-conversion",
                    ["values"] = new JArray(-32768, 0, 32767), ["conversionType"] = "Int16",
                    ["expectedValues"] = new JArray(-32768, 0, 32767)
                };
                RunExecutableWithInput(runner, new[] { "run" }, conversion.ToString(), 0);
                conversion["conversionType"] = "Int64";
                conversion["values"] = new JArray(int.MinValue, 0, int.MaxValue);
                conversion["expectedValues"] = new JArray(int.MinValue, 0, int.MaxValue);
                RunExecutableWithInput(runner, new[] { "run" }, conversion.ToString(), 0);
                conversion["conversionType"] = "Int16";
                conversion["values"] = new JArray(32768);
                conversion["expectedValues"] = new JArray(32768);
                var overflow = RunExecutableWithInput(runner, new[] { "run" }, conversion.ToString(), 4);
                Require(!(bool)overflow["succeeded"] && (string)overflow["code"] == "flow.execution.failed",
                    "flow.conversion.overflow");
                Require(((JArray)overflow["diagnostics"]).Any(item => (string)item["code"] == "flow.native.error" &&
                    item["nativeCode"] != null && !string.IsNullOrWhiteSpace((string)item["component"]) &&
                    !string.IsNullOrWhiteSpace((string)item["message"])), "tools.probe.native_errors");
                Require((bool)overflow["cleanupSucceeded"], "tools.probe.failure_cleanup");
                var text = new JObject
                {
                    ["schemaVersion"] = "1.1", ["recipe"] = "flat-file-text",
                    ["values"] = new JArray("Long student name", "Unicode " + char.ConvertFromUtf32(0x03A9)),
                    ["expectedValues"] = new JArray("Long student name", "Unicode " + char.ConvertFromUtf32(0x03A9)),
                    ["sourceWidth"] = 4, ["destinationWidth"] = 64
                };
                var sourceTruncation = RunExecutableWithInput(runner, new[] { "run" }, text.ToString(), 4);
                Require((string)sourceTruncation["code"] == "flow.text.execution.failed", "flow.text.source.truncation");
                text["sourceWidth"] = 64;
                text["destinationWidth"] = 4;
                var destinationTruncation = RunExecutableWithInput(runner, new[] { "run" }, text.ToString(), 4);
                Require((string)destinationTruncation["code"] == "flow.text.width_verification.failed", "flow.text.destination.width_mismatch");
                text["sourceWidth"] = 4;
                text["widenTo"] = 64;
                var widened = RunExecutableWithInput(runner, new[] { "run" }, text.ToString(), 0);
                Require((int)widened["rowCount"] == 2, "flow.text.widen.preserved");
                RequireNoValues(widened);
                var after = Directory.GetDirectories(Path.GetTempPath(), "SsisFlowProbe-*");
                Require(!after.Except(before, StringComparer.OrdinalIgnoreCase).Any(), "flow.execution.cleanup");
                Console.WriteLine("SSIS execution probe: PASS; Derived Column, Data Conversion boundaries/overflow, Flat File source/destination truncation and Unicode width repair, output assertions and cleanup; no database connections.");
                return 0;
            }
            catch (Exception)
            {
                Console.Error.WriteLine("SSIS execution probe failed. Raw exception details are withheld.");
                return 1;
            }
        }

        private static JObject RunCli(string[] arguments, params int[] allowedExitCodes) =>
            RunExecutable(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SsisAiRuntime.Cli.exe"), arguments, allowedExitCodes);

        private static JObject RunExecutable(string cli, string[] arguments, params int[] allowedExitCodes) =>
            RunExecutableWithInput(cli, arguments, null, allowedExitCodes);

        private static JObject RunExecutableWithInput(string cli, string[] arguments, string standardInput, params int[] allowedExitCodes)
        {
            Require(File.Exists(cli), "fixture.cli.missing");
            var argumentText = string.Join(" ", arguments.Select(Quote));
            using (var process = new Process
            {
                StartInfo = new ProcessStartInfo(cli, argumentText)
                {
                    UseShellExecute = false,
                    RedirectStandardInput = standardInput != null,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            })
            {
                Require(process.Start(), "fixture.cli.start");
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                var stdin = standardInput == null ? System.Threading.Tasks.Task.CompletedTask :
                    System.Threading.Tasks.Task.Run(() =>
                    {
                        process.StandardInput.Write(standardInput);
                        process.StandardInput.Close();
                    });
                if (!process.WaitForExit(75000))
                {
                    process.Kill();
                    process.WaitForExit();
                    throw new InvalidOperationException("fixture.cli.timeout");
                }
                System.Threading.Tasks.Task.WaitAll(stdin, stdout, stderr);
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