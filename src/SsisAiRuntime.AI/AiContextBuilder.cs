using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using SsisAiRuntime.Inspectors;

namespace SsisAiRuntime.AI
{
    public sealed class AiContextBuilder
    {
        public AiContext Build(AiToolResult result, int itemLimit = 50)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (itemLimit < 1 || itemLimit > 200) throw new ArgumentOutOfRangeException(nameof(itemLimit));
            var facts = new List<AiFact>();
            if (!result.Succeeded)
                return new AiContext(result.ToolName, false, false, facts, result.ErrorCode, result.ErrorMessage, 0);

            if (result.Result is PackageIntelligenceSummary summary)
            {
                var summaryFacts = new[]
                {
                    new AiFact("package", summary.Package.PackageName, summary.Package.PackageId),
                    new AiFact("counts", "connections", summary.ConnectionCount.ToString()),
                    new AiFact("counts", "variables", summary.VariableCount.ToString()),
                    new AiFact("counts", "parameters", summary.ParameterCount.ToString()),
                    new AiFact("counts", "executables", summary.ExecutableCount.ToString()),
                    new AiFact("counts", "sqlTasks", summary.SqlTaskCount.ToString()),
                    new AiFact("counts", "dataFlows", summary.DataFlowCount.ToString()),
                    new AiFact("counts", "components", summary.ComponentCount.ToString()),
                    new AiFact("counts", "paths", summary.PathCount.ToString()),
                    new AiFact("counts", "expressionOwners", summary.ExpressionOwnerCount.ToString()),
                    new AiFact("counts", "coverageGaps", summary.CoverageGapCount.ToString())
                };
                facts.AddRange(summaryFacts.Take(itemLimit));
                var remaining = Math.Max(0, itemLimit - facts.Count);
                facts.AddRange(summary.Connections.Take(remaining).Select(item => new AiFact("connection", item.Name, item.NativeId)));
                remaining = Math.Max(0, itemLimit - facts.Count);
                facts.AddRange(summary.Tasks.Take(remaining).Select(item => new AiFact("task", item.Name, item.NativeId)));
            }
            else if (result.Result is PackageSearchResult search)
            {
                facts.AddRange(search.Matches.Take(itemLimit).Select(match => new AiFact(match.Reference.Handle.Kind.ToString(),
                    match.Reference.Name, match.Reference.Handle.Value)));
            }
            else if (result.Result is PackageControlFlow control)
            {
                facts.AddRange(control.Graph.Nodes.Take(itemLimit).Select(node => new AiFact("task", node.Name, node.NativeId)));
                facts.AddRange(control.Graph.Edges.Take(Math.Max(0, itemLimit - facts.Count)).Select(edge =>
                    new AiFact("precedence", edge.From.Value, edge.To.Value)));
            }
            else if (result.Result is PackageColumnTrace trace)
            {
                facts.AddRange(trace.Trace.Columns.Take(itemLimit).Select(column =>
                    new AiFact("column", column.Name, column.ComponentId + ":" + column.Id)));
                facts.AddRange(trace.Trace.Links.Take(Math.Max(0, itemLimit - facts.Count)).Select(link =>
                    new AiFact("lineage." + link.Kind, link.Source.ComponentId + ":" + link.Source.Id,
                        link.Target.ComponentId + ":" + link.Target.Id)));
            }
            else
            {
                throw new ArgumentException("The result type is not supported for AI context.", nameof(result));
            }

            var omitted = Math.Max(0, CountPotentialFacts(result.Result) - facts.Count);
            return new AiContext(result.ToolName, true, result.IsComplete, facts, string.Empty, string.Empty, omitted);
        }

        private static int CountPotentialFacts(object result)
        {
            if (result is PackageIntelligenceSummary summary) return 11 + summary.Connections.Count + summary.Tasks.Count;
            if (result is PackageSearchResult search) return search.TotalMatches;
            if (result is PackageControlFlow control) return control.Graph.Nodes.Count + control.Graph.Edges.Count;
            if (result is PackageColumnTrace trace) return trace.Trace.Columns.Count + trace.Trace.Links.Count;
            return 0;
        }
    }

    public sealed class AiContext
    {
        public AiContext(string toolName, bool succeeded, bool isComplete, IEnumerable<AiFact> facts,
            string errorCode, string errorMessage, int factsOmitted)
        {
            ToolName = toolName ?? string.Empty;
            Succeeded = succeeded;
            IsComplete = isComplete;
            Facts = new ReadOnlyCollection<AiFact>(new List<AiFact>(facts ?? throw new ArgumentNullException(nameof(facts))));
            ErrorCode = errorCode ?? string.Empty;
            ErrorMessage = errorMessage ?? string.Empty;
            FactsOmitted = factsOmitted;
        }
        public string ToolName { get; }
        public bool Succeeded { get; }
        public bool IsComplete { get; }
        public IReadOnlyList<AiFact> Facts { get; }
        public int FactsOmitted { get; }
        public string ErrorCode { get; }
        public string ErrorMessage { get; }
        public string RedactionPolicy => "metadata-only";
    }

    public sealed class AiFact
    {
        public AiFact(string kind, string name, string reference)
        {
            Kind = kind ?? string.Empty;
            Name = name ?? string.Empty;
            Reference = reference ?? string.Empty;
        }
        public string Kind { get; }
        public string Name { get; }
        public string Reference { get; }
    }
}