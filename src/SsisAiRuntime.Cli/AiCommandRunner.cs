#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using SsisAiRuntime.AI;
using SsisAiRuntime.Inspectors;
using SsisAiRuntime.Ssis16;

namespace SsisAiRuntime.Cli
{
    internal sealed class AiCommandRunner
    {
        private const int ResultItemLimit = 100;
        private const int UnsupportedExampleLimit = 5;

        public int Run(string[]? args, TextWriter output)
        {
            string? tool = null;
            try
            {
                if (args == null || args.Length < 2 || args[0] != "ai")
                {
                    return WriteError(output, null, 2, "ai.usage", UsageMessage);
                }

                tool = args[1];
                if (tool == "question.plan")
                {
                    if (args.Length != 3 || string.IsNullOrWhiteSpace(args[2]))
                    {
                        return WriteError(output, tool, 2, "ai.usage", UsageMessage);
                    }
                    var plan = new QuestionPlanner().Plan(args[2]);
                    return Write(output, tool, 0, Project(plan), Array.Empty<UnsupportedItem>());
                }

                if (!IsPackageTool(tool) || args.Length < 3 || string.IsNullOrWhiteSpace(args[2]))
                {
                    return WriteError(output, IsKnownTool(tool) ? tool : null, 2, "ai.usage", UsageMessage);
                }
                if (!HasValidOptions(args, tool))
                {
                    return WriteError(output, tool, 2, "ai.usage", UsageMessage);
                }
                if (!File.Exists(args[2]))
                {
                    return WriteError(output, tool, 3, "package.path.not_found",
                        "The package file is missing or inaccessible. The path is omitted.");
                }

                var load = new PackageLoader().Load(args[2]);
                if (!load.Succeeded)
                {
                    return WriteError(output, tool, 3, "ssis.package.load_failed",
                        "The SSIS runtime failed to load the package. Details are omitted.");
                }

                try
                {
                    var snapshotResult = new PackageAnalysisSnapshotFactory().Create(load.Session);
                    if (snapshotResult.Items.Count != 1)
                    {
                        return WriteError(output, tool, 4, "ai.snapshot.failed",
                            "The package analysis snapshot could not be produced.");
                    }

                    var snapshot = snapshotResult.Items[0];
                    var unsupported = snapshot.UnsupportedItems;
                    JObject results;
                    switch (tool)
                    {
                        case "package.summary":
                            var summary = new PackageAnalysisEngine().Execute(snapshot,
                                new AiToolRequest(AiToolNames.PackageSummary));
                            if (!summary.Succeeded)
                            {
                                return WriteError(output, tool, 4, "ai.summary.failed",
                                    "The package summary could not be created.");
                            }
                            results = Project(new AiContextBuilder().Build(summary, ResultItemLimit));
                            unsupported = summary.UnsupportedItems;
                            break;
                        case "dependency.graph":
                            results = Project(snapshot.Dependencies);
                            unsupported = snapshot.Dependencies.UnsupportedItems;
                            break;
                        case "dependency.query":
                            TryGetNode(args, true, out var queryNode, out var recursive);
                            var query = new DependencyQuery().Find(snapshot.Dependencies, queryNode, true, recursive);
                            if (query.Items.Count != 1)
                            {
                                return WriteError(output, tool, 4, "ai.dependency.selection_invalid",
                                    "The dependency node was missing or ambiguous.");
                            }
                            results = Project(query.Items[0]);
                            unsupported = query.UnsupportedItems;
                            break;
                        case "impact.analysis":
                            TryGetNode(args, false, out var impactNode, out _);
                            var impact = new ImpactAnalysisQuery().Analyze(snapshot.Dependencies, impactNode);
                            if (impact.Items.Count != 1)
                            {
                                return WriteError(output, tool, 4, "ai.impact.selection_invalid",
                                    "The impact node was missing or ambiguous.");
                            }
                            results = Project(impact.Items[0]);
                            unsupported = impact.UnsupportedItems;
                            break;
                        default:
                            return WriteError(output, null, 2, "ai.usage", UsageMessage);
                    }

                    return Write(output, tool, unsupported.Count == 0 ? 0 : 5, results, unsupported);
                }
                finally
                {
                    load.Session.Package.Dispose();
                }
            }
            catch (Exception)
            {
                return WriteError(output, tool != null && IsKnownTool(tool) ? tool : null, 4, "ai.tool.failed",
                    "The AI analysis tool failed. Exception details are omitted.");
            }
        }

        private static bool IsKnownTool(string tool) => tool == "package.summary" || tool == "dependency.graph" ||
            tool == "dependency.query" || tool == "impact.analysis" || tool == "question.plan";

        private static bool IsPackageTool(string tool) => tool == "package.summary" || tool == "dependency.graph" ||
            tool == "dependency.query" || tool == "impact.analysis";

        private static bool HasValidOptions(string[] args, string tool)
        {
            if (tool == "package.summary" || tool == "dependency.graph") { return args.Length == 3; }
            return TryGetNode(args, tool == "dependency.query", out _, out _);
        }

        private static bool TryGetNode(string[] args, bool allowRecursive, out string nodeKey, out bool recursive)
        {
            nodeKey = string.Empty;
            recursive = false;
            var nodeSpecified = false;
            for (var index = 3; index < args.Length; index++)
            {
                if (args[index] == "--node")
                {
                    if (nodeSpecified || index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]) ||
                        args[index + 1].StartsWith("--")) { return false; }
                    nodeKey = args[++index];
                    nodeSpecified = true;
                }
                else if (args[index] == "--recursive" && allowRecursive && !recursive)
                {
                    recursive = true;
                }
                else
                {
                    return false;
                }
            }
            return nodeSpecified;
        }

        private static JObject Project(QuestionPlan plan) => new JObject
        {
            ["intent"] = plan.Intent.ToString(),
            ["toolName"] = plan.ToolName,
            ["confidence"] = plan.Confidence,
            ["requiredSelectors"] = new JArray(plan.RequiredSelectors)
        };

        private static JObject Project(AiContext context) => new JObject
        {
            ["succeeded"] = context.Succeeded,
            ["isComplete"] = context.IsComplete,
            ["redactionPolicy"] = context.RedactionPolicy,
            ["facts"] = new JArray(context.Facts.Take(ResultItemLimit).Select(fact => new JObject
            {
                ["kind"] = CliSummary.LimitText(fact.Kind),
                ["name"] = CliSummary.LimitText(fact.Name),
                ["reference"] = CliSummary.LimitText(fact.Reference)
            })),
            ["factsOmitted"] = context.FactsOmitted
        };

        private static JObject Project(PackageDependencyGraph graph) => new JObject
        {
            ["isComplete"] = graph.IsComplete,
            ["nodeCount"] = graph.Nodes.Count,
            ["edgeCount"] = graph.Edges.Count,
            ["nodesOmitted"] = Math.Max(0, graph.Nodes.Count - ResultItemLimit),
            ["edgesOmitted"] = Math.Max(0, graph.Edges.Count - ResultItemLimit),
            ["nodes"] = new JArray(graph.Nodes.Take(ResultItemLimit).Select(Project)),
            ["edges"] = new JArray(graph.Edges.Take(ResultItemLimit).Select(Project))
        };

        private static JObject Project(ImpactAnalysisResult impact) => new JObject
        {
            ["root"] = Project(impact.Root),
            ["impactedNodeCount"] = impact.ImpactedNodes.Count,
            ["pathCount"] = impact.Paths.Count,
            ["nodesOmitted"] = Math.Max(0, impact.ImpactedNodes.Count - ResultItemLimit),
            ["pathsOmitted"] = Math.Max(0, impact.Paths.Count - ResultItemLimit),
            ["impactedNodes"] = new JArray(impact.ImpactedNodes.Take(ResultItemLimit).Select(Project)),
            ["paths"] = new JArray(impact.Paths.Take(ResultItemLimit).Select(Project))
        };

        private static JObject Project(DependencyNode node) => new JObject
        {
            ["key"] = CliSummary.LimitText(node.Key),
            ["kind"] = node.Kind.ToString(),
            ["name"] = CliSummary.LimitText(node.Name),
            ["nativeId"] = CliSummary.LimitText(node.NativeId),
            ["parentId"] = CliSummary.LimitText(node.ParentId)
        };

        private static JObject Project(DependencyEdge edge) => new JObject
        {
            ["from"] = CliSummary.LimitText(edge.From),
            ["to"] = CliSummary.LimitText(edge.To),
            ["kind"] = edge.Kind.ToString()
        };

        private static int WriteError(TextWriter output, string? tool, int exitCode, string code, string message) =>
            Write(output, tool, exitCode, null, Array.Empty<UnsupportedItem>(), code, message);

        private static int Write(TextWriter output, string? tool, int exitCode, JObject? results,
            IEnumerable<UnsupportedItem> unsupportedItems, string errorCode = "", string errorMessage = "")
        {
            var unsupported = unsupportedItems.ToArray();
            var diagnostics = errorCode.Length == 0
                ? new JArray()
                : new JArray(new JObject { ["code"] = errorCode, ["severity"] = "Error", ["message"] = errorMessage });
            var envelope = new JObject
            {
                ["schemaVersion"] = "1.0",
                ["command"] = "ai",
                ["tool"] = tool,
                ["succeeded"] = exitCode == 0 || exitCode == 5,
                ["isComplete"] = exitCode == 0,
                ["exitCode"] = exitCode,
                ["outputMode"] = "summary",
                ["results"] = results == null ? JValue.CreateNull() : results,
                ["diagnostics"] = diagnostics,
                ["unsupportedItems"] = new JArray(unsupported.Take(UnsupportedExampleLimit).Select(item => new JObject
                {
                    ["id"] = CliSummary.LimitText(item.Id),
                    ["name"] = CliSummary.LimitText(item.Name),
                    ["creationName"] = CliSummary.LimitText(item.CreationName),
                    ["reasonCode"] = item.ReasonCode,
                    ["reason"] = "Details omitted under the metadata-only redaction policy."
                })),
                ["coverage"] = new JObject
                {
                    ["unsupportedCount"] = unsupported.Length,
                    ["examplesOmitted"] = Math.Max(0, unsupported.Length - UnsupportedExampleLimit),
                    ["diagnosticCount"] = diagnostics.Count
                },
                ["redaction"] = new JObject
                {
                    ["policy"] = "metadata-only",
                    ["applied"] = true,
                    ["connectionStringsOmitted"] = true,
                    ["variableValuesOmitted"] = true,
                    ["parameterValuesOmitted"] = true,
                    ["expressionTextOmitted"] = true,
                    ["sqlTextOmitted"] = true,
                    ["settingValuesOmitted"] = true,
                    ["descriptionsOmitted"] = true,
                    ["diagnosticDetailsOmitted"] = true
                }
            };
            output.WriteLine(envelope.ToString(Newtonsoft.Json.Formatting.None));
            return exitCode;
        }

        private const string UsageMessage = "Usage: SsisAiRuntime.Cli.exe ai <package.summary|dependency.graph> <package.dtsx>; " +
            "ai dependency.query <package.dtsx> --node <node-key> [--recursive]; " +
            "ai impact.analysis <package.dtsx> --node <node-key>; ai question.plan <question>.";
    }
}