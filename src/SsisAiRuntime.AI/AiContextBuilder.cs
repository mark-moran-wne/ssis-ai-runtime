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
            var facts = CreateFacts(result, itemLimit, out var potentialFacts);
            return new AiContext(result.ToolName, result.Succeeded, result.IsComplete,
                facts, result.ErrorCode, result.ErrorMessage, Math.Max(0, potentialFacts - facts.Count));
        }

        private static List<AiFact> CreateFacts(AiToolResult result, int itemLimit, out int potentialFacts)
        {
            var facts = new List<AiFact>();
            var totalFacts = 0;
            potentialFacts = 0;
            if (!result.Succeeded) { return facts; }

            void AddFacts(IEnumerable<AiFact> values)
            {
                foreach (var fact in values)
                {
                    totalFacts++;
                    if (facts.Count < itemLimit) { facts.Add(fact); }
                }
            }

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
                AddFacts(summaryFacts);
                AddFacts(summary.Connections.Select(item => new AiFact("connection", item.Name, item.NativeId)));
                AddFacts(summary.Tasks.Select(item => new AiFact("task", item.Name, item.NativeId)));
            }
            else if (result.Result is PackageSearchResult search)
            {
                AddFacts(search.Matches.Select(match => new AiFact(match.Reference.Handle.Kind.ToString(),
                    match.Reference.Name, match.Reference.Handle.Value)));
                totalFacts += search.MatchesOmitted;
            }
            else if (result.Result is PackageControlFlow control)
            {
                AddFacts(control.Graph.Nodes.Select(node => new AiFact("task", node.Name, node.NativeId)));
                AddFacts(control.Graph.Edges.Select(edge =>
                    new AiFact("precedence", edge.From.Value, edge.To.Value)));
            }
            else if (result.Result is PackageColumnTrace trace)
            {
                AddFacts(trace.Trace.Columns.Select(column =>
                    new AiFact("column", column.Name, column.ComponentId + ":" + column.Id)));
                AddFacts(trace.Trace.Links.Select(link =>
                    new AiFact("lineage." + link.Kind, link.Source.ComponentId + ":" + link.Source.Id,
                        link.Target.ComponentId + ":" + link.Target.Id)));
            }
            else if (result.Result is PackageDependencyGraph graph)
            {
                AddFacts(graph.Nodes.Select(node => new AiFact("dependencyNode." + node.Kind, node.Name, node.Key)));
                AddFacts(graph.Edges.Select(edge => new AiFact("dependencyEdge." + edge.Kind, edge.From, edge.To)));
            }
            else if (result.Result is ImpactAnalysisResult impact)
            {
                AddFacts(new[] { new AiFact("impactRoot." + impact.Root.Kind, impact.Root.Name, impact.Root.Key) });
                AddFacts(impact.ImpactedNodes.Select(node => new AiFact("impacted." + node.Kind, node.Name, node.Key)));
                AddFacts(impact.Paths.Select(edge => new AiFact("impactPath." + edge.Kind, edge.From, edge.To)));
            }
            else if (result.Result is QuestionPlan plan)
            {
                AddFacts(new[] { new AiFact("questionIntent", plan.Intent.ToString(), plan.ToolName) });
                AddFacts(plan.RequiredSelectors.Select(selector => new AiFact("requiredSelector", selector, string.Empty)));
            }
            else
            {
                throw new ArgumentException("The result type is not supported for AI context.", nameof(result));
            }

            potentialFacts = totalFacts;
            return facts;
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