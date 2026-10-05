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
                    return Emit(output, new PackageAnalysisEngine().Plan(
                        new AiToolRequest(AiToolNames.QuestionPlan, question: args[2])));
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
                    AiToolRequest request;
                    switch (tool)
                    {
                        case "package.summary":
                            request = new AiToolRequest(AiToolNames.PackageSummary);
                            break;
                        case "dependency.graph":
                            request = new AiToolRequest(AiToolNames.DependencyGraph);
                            break;
                        case "dependency.query":
                            TryGetNode(args, true, out var queryNode, out var recursive);
                            request = new AiToolRequest(AiToolNames.DependencyQuery, nodeKey: queryNode,
                                incoming: true, recursive: recursive);
                            break;
                        case "impact.analysis":
                            TryGetNode(args, false, out var impactNode, out _);
                            request = new AiToolRequest(AiToolNames.ImpactAnalysis, nodeKey: impactNode);
                            break;
                        case "selector.resolve":
                            TryGetSelector(args, out var selector, out var kind);
                            request = new AiToolRequest(AiToolNames.ResolveSelector, selector: selector, kind: kind);
                            break;
                        case "impact.classified":
                            TryGetNode(args, false, out var classifiedNode, out _);
                            request = new AiToolRequest(AiToolNames.RichImpactAnalysis, nodeKey: classifiedNode);
                            break;
                        default:
                            return WriteError(output, null, 2, "ai.usage", UsageMessage);
                    }

                    return Emit(output, new PackageAnalysisEngine().Execute(snapshot, request));
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
            tool == "dependency.query" || tool == "impact.analysis" || tool == "selector.resolve" ||
            tool == "impact.classified" || tool == "question.plan";

        private static bool IsPackageTool(string tool) => tool == "package.summary" || tool == "dependency.graph" ||
            tool == "dependency.query" || tool == "impact.analysis" || tool == "selector.resolve" ||
            tool == "impact.classified";

        private static bool HasValidOptions(string[] args, string tool)
        {
            if (tool == "package.summary" || tool == "dependency.graph") { return args.Length == 3; }
            if (tool == "selector.resolve") { return TryGetSelector(args, out _, out _); }
            return TryGetNode(args, tool == "dependency.query", out _, out _);
        }

        private static bool TryGetSelector(string[] args, out string selector, out SemanticObjectKind? kind)
        {
            selector = string.Empty;
            kind = null;
            var selectorSpecified = false;
            var kindSpecified = false;
            for (var index = 3; index < args.Length; index++)
            {
                if (args[index] == "--selector")
                {
                    if (selectorSpecified || index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]) ||
                        args[index + 1].StartsWith("--")) { return false; }
                    selector = args[++index];
                    selectorSpecified = true;
                }
                else if (args[index] == "--kind")
                {
                    if (kindSpecified || index + 1 >= args.Length ||
                        !Enum.GetNames(typeof(SemanticObjectKind)).Any(name =>
                            string.Equals(name, args[index + 1], StringComparison.OrdinalIgnoreCase))) { return false; }
                    kind = (SemanticObjectKind)Enum.Parse(typeof(SemanticObjectKind), args[++index], true);
                    kindSpecified = true;
                }
                else
                {
                    return false;
                }
            }
            return selectorSpecified;
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

        private static int Emit(TextWriter output, AiToolResult result)
        {
            if (!result.Succeeded)
            {
                return WriteError(output, result.ToolName, 4, result.ErrorCode, result.ErrorMessage);
            }

            var projected = result.Result is PackageIntelligenceSummary
                ? Project(new AiContextBuilder().Build(result, ResultItemLimit))
                : Project(result.Result);
            return Write(output, result.ToolName, result.IsComplete ? 0 : 5, projected, result.UnsupportedItems);
        }

        private static JObject Project(object result)
        {
            if (result is AiContext context) { return Project(context); }
            if (result is PackageDependencyGraph graph) { return Project(graph); }
            if (result is ImpactAnalysisResult impact) { return Project(impact); }
            if (result is DependencySelectorResolution resolution) { return Project(resolution); }
            if (result is RichImpactAnalysisResult richImpact) { return Project(richImpact); }
            if (result is QuestionPlan plan) { return Project(plan); }
            throw new ArgumentException("The AI tool result type is not supported.", nameof(result));
        }

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

        private static JObject Project(PackageDependencyGraph graph)
        {
            var nodes = graph.Nodes.Take(ResultItemLimit).ToList();
            var eligibleEdges = BoundedEdges(graph.Edges, nodes.Select(node => node.Key));
            var returnedEdges = eligibleEdges.Take(ResultItemLimit).ToList();
            return new JObject
            {
                ["isComplete"] = graph.IsComplete,
                ["nodeCount"] = graph.Nodes.Count,
                ["edgeCount"] = graph.Edges.Count,
                ["returnedNodeCount"] = nodes.Count,
                ["returnedEdgeCount"] = returnedEdges.Count,
                ["nodesOmitted"] = Math.Max(0, graph.Nodes.Count - nodes.Count),
                ["edgesOmitted"] = graph.Edges.Count - returnedEdges.Count,
                ["edgesOmittedForMissingEndpoint"] = graph.Edges.Count - eligibleEdges.Count,
                ["nodes"] = new JArray(nodes.Select(Project)),
                ["edges"] = new JArray(returnedEdges.Select(Project))
            };
        }

        private static JObject Project(ImpactAnalysisResult impact)
        {
            var nodes = impact.ImpactedNodes.Take(ResultItemLimit).ToList();
            var keys = new HashSet<string>(nodes.Select(node => node.Key), StringComparer.Ordinal) { impact.Root.Key };
            var eligiblePaths = impact.Paths.Where(edge => keys.Contains(edge.From) && keys.Contains(edge.To)).ToList();
            var returnedPaths = eligiblePaths.Take(ResultItemLimit).ToList();
            return new JObject
            {
                ["root"] = Project(impact.Root),
                ["impactedNodeCount"] = impact.ImpactedNodes.Count,
                ["pathCount"] = impact.Paths.Count,
                ["returnedNodeCount"] = nodes.Count,
                ["returnedPathCount"] = returnedPaths.Count,
                ["nodesOmitted"] = Math.Max(0, impact.ImpactedNodes.Count - nodes.Count),
                ["pathsOmitted"] = impact.Paths.Count - returnedPaths.Count,
                ["pathsOmittedForMissingEndpoint"] = impact.Paths.Count - eligiblePaths.Count,
                ["impactedNodes"] = new JArray(nodes.Select(Project)),
                ["paths"] = new JArray(returnedPaths.Select(Project))
            };
        }

        private static List<DependencyEdge> BoundedEdges(IEnumerable<DependencyEdge> edges, IEnumerable<string> nodeKeys)
        {
            var keys = new HashSet<string>(nodeKeys, StringComparer.Ordinal);
            return edges.Where(edge => keys.Contains(edge.From) && keys.Contains(edge.To)).ToList();
        }

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

        private static JObject Project(DependencySelectorResolution resolution) => new JObject
        {
            ["status"] = resolution.Status.ToString(),
            ["resolvedNode"] = resolution.ResolvedNode == null ? null : Project(resolution.ResolvedNode),
            ["candidateCount"] = resolution.CandidateCount,
            ["returnedCandidateCount"] = resolution.Candidates.Count,
            ["candidatesOmitted"] = resolution.CandidatesOmitted,
            ["candidates"] = new JArray(resolution.Candidates.Take(ResultItemLimit).Select(Project))
        };

        private static JObject Project(RichImpactAnalysisResult impact)
        {
            var selectedImpacts = impact.Impacts.Take(ResultItemLimit).ToList();
            var remainingPathEdges = ResultItemLimit;
            var returnedPathEdges = 0;
            var projectedImpacts = new JArray();
            foreach (var item in selectedImpacts)
            {
                var path = item.Path.Take(remainingPathEdges).ToList();
                remainingPathEdges -= path.Count;
                returnedPathEdges += path.Count;
                projectedImpacts.Add(new JObject
                {
                    ["node"] = Project(item.Node),
                    ["category"] = item.Category.ToString(),
                    ["distance"] = item.Distance,
                    ["path"] = new JArray(path.Select(Project)),
                    ["pathEdgesOmitted"] = Math.Max(0, item.Path.Count - path.Count)
                });
            }

            var narrative = new AnalysisNarrativeBuilder().Build(impact);
            var totalPathEdges = impact.Impacts.Sum(item => item.Path.Count);
            return new JObject
            {
                ["root"] = Project(impact.Root),
                ["impactCount"] = impact.Impacts.Count,
                ["returnedImpactCount"] = selectedImpacts.Count,
                ["impactsOmitted"] = Math.Max(0, impact.Impacts.Count - selectedImpacts.Count),
                ["pathEdgeCount"] = totalPathEdges,
                ["returnedPathEdgeCount"] = returnedPathEdges,
                ["pathEdgesOmitted"] = Math.Max(0, totalPathEdges - returnedPathEdges),
                ["isComplete"] = impact.IsComplete,
                ["narrative"] = new JObject
                {
                    ["summary"] = CliSummary.LimitText(narrative.Summary),
                    ["coverage"] = CliSummary.LimitText(narrative.Coverage),
                    ["observations"] = new JArray(narrative.Observations.Take(20).Select(CliSummary.LimitText)),
                    ["observationsOmitted"] = Math.Max(0, narrative.Observations.Count - 20),
                    ["redactionPolicy"] = narrative.RedactionPolicy
                },
                ["impacts"] = projectedImpacts
            };
        }

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
            "ai impact.analysis <package.dtsx> --node <node-key>; " +
            "ai selector.resolve <package.dtsx> --selector <text> [--kind <object-kind>]; " +
            "ai impact.classified <package.dtsx> --node <node-key>; ai question.plan <question>.";
    }
}