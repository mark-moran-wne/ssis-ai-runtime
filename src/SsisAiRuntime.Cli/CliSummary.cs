#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using SsisAiRuntime.Inspectors;

namespace SsisAiRuntime.Cli
{
    public static class CliSummary
    {
        public static string LimitText(string text)
        {
            return text.Length <= 120 ? text : text.Substring(0, 117) + "...";
        }

        public static JObject Results(string command, object results)
        {
            PackageOverview package;
            JObject counts;
            if (command == "overview" && results is PackageOverview overview)
            {
                package = overview;
                if (new[] { package.ConnectionCount, package.VariableCount, package.ParameterCount,
                    package.ExecutableCount, package.PrecedenceConstraintCount }.Any(count => count < 0))
                {
                    throw new InvalidOperationException("Invalid package counts.");
                }

                counts = new JObject
                {
                    ["connections"] = package.ConnectionCount,
                    ["variables"] = package.VariableCount,
                    ["parameters"] = package.ParameterCount,
                    ["rootExecutables"] = package.ExecutableCount,
                    ["rootPrecedenceConstraints"] = package.PrecedenceConstraintCount
                };
            }
            else if (command == "search" && results is PackageSearchResult search)
            {
                package = search.Package;
                counts = new JObject
                {
                    ["totalMatches"] = search.TotalMatches,
                    ["returnedMatches"] = search.Matches.Count,
                    ["matchesOmitted"] = search.MatchesOmitted
                };
            }
            else if ((command == "control-flow" || command == "predecessors" || command == "successors") && results is PackageControlFlow controlFlow)
            {
                package = controlFlow.Package;
                counts = new JObject
                {
                    ["nodes"] = controlFlow.Graph.Nodes.Count,
                    ["precedenceEdges"] = controlFlow.Graph.Edges.Count(edge => edge.Kind == ControlFlowEdgeKind.Precedence),
                    ["containmentEdges"] = controlFlow.Graph.Edges.Count(edge => edge.Kind == ControlFlowEdgeKind.Containment)
                };
            }
            else if (command == "trace" && results is PackageColumnTrace columnTrace)
            {
                package = columnTrace.Package;
                counts = new JObject
                {
                    ["flowId"] = LimitText(columnTrace.Trace.FlowId),
                    ["direction"] = columnTrace.Trace.Direction,
                    ["columns"] = columnTrace.Trace.Columns.Count,
                    ["links"] = columnTrace.Trace.Links.Count
                };
            }
            else if (results is PackageContext context &&
                string.Equals(context.Kind.ToString(), command, StringComparison.OrdinalIgnoreCase))
            {
                package = context.Package ?? throw new InvalidOperationException("Missing package identity.");
                counts = command switch
                {
                    "sql" => new JObject { ["sqlTasks"] = context.SqlStatements.Count },
                    "lineage" => new JObject
                    {
                        ["dataFlows"] = context.DataFlows.Count,
                        ["components"] = context.DataFlows.Sum(flow => flow.Components.Count),
                        ["paths"] = context.DataFlows.Sum(flow => flow.Paths.Count)
                    },
                    "configuration" => new JObject
                    {
                        ["connections"] = context.Connections.Count,
                        ["variables"] = context.Variables.Count,
                        ["parameters"] = context.Parameters.Count,
                        ["expressionOwners"] = context.Expressions.Count
                    },
                    _ => throw new InvalidOperationException("Unexpected context kind.")
                };
            }
            else
            {
                throw new InvalidOperationException("Unexpected inspection result type.");
            }

            if (string.IsNullOrWhiteSpace(package.PackageId) || string.IsNullOrWhiteSpace(package.PackageName))
            {
                throw new InvalidOperationException("Missing package identity.");
            }

            return new JObject
            {
                ["package"] = new JObject
                {
                    ["sessionId"] = package.SessionId,
                    ["packageId"] = LimitText(package.PackageId),
                    ["packageName"] = LimitText(package.PackageName)
                },
                ["counts"] = counts
            };
        }

        public static JObject Coverage(IReadOnlyList<UnsupportedItem> items)
        {
            var groups = items.GroupBy(item => new { item.Name, item.CreationName, item.ReasonCode })
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key.Name, StringComparer.Ordinal)
                .ThenBy(group => group.Key.CreationName, StringComparer.Ordinal)
                .ThenBy(group => group.Key.ReasonCode, StringComparer.Ordinal)
                .ToList();
            return new JObject
            {
                ["unsupportedCount"] = items.Count,
                ["groupCount"] = groups.Count,
                ["groupsOmitted"] = Math.Max(0, groups.Count - 8),
                ["examplesOmitted"] = Math.Max(0, items.Count - 5),
                ["metadataTextLimit"] = 120,
                ["reasonCounts"] = new JArray(items.GroupBy(item => item.ReasonCode)
                    .OrderBy(group => group.Key, StringComparer.Ordinal).Select(group => new JObject
                    {
                        ["reasonCode"] = group.Key,
                        ["count"] = group.Count()
                    })),
                ["groups"] = new JArray(groups.Take(8).Select(group => new JObject
                {
                    ["name"] = LimitText(group.Key.Name),
                    ["creationName"] = LimitText(group.Key.CreationName),
                    ["reasonCode"] = group.Key.ReasonCode,
                    ["count"] = group.Count()
                }))
            };
        }
    }
}