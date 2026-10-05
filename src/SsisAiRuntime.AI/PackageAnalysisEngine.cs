using System;
using System.Collections.Generic;
using System.Linq;
using SsisAiRuntime.Inspectors;

namespace SsisAiRuntime.AI
{
    public sealed class PackageAnalysisEngine
    {
        public AiToolResult Plan(AiToolRequest request)
        {
            if (request == null) { throw new ArgumentNullException(nameof(request)); }
            if (request.ToolName != AiToolNames.QuestionPlan)
            {
                return AiToolResult.Failure(request.ToolName, "ai.tool.unsupported",
                    "Only question planning is available without a package snapshot.");
            }
            return BuildQuestionPlan(request);
        }

        public AiToolResult Execute(PackageAnalysisSnapshot snapshot, AiToolRequest request)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (request == null) throw new ArgumentNullException(nameof(request));

            try
            {
                switch (request.ToolName)
                {
                    case AiToolNames.PackageSummary:
                        return Success(request, new PackageIntelligenceSummary(snapshot), snapshot.UnsupportedItems);
                    case AiToolNames.MetadataSearch:
                        return Search(snapshot, request);
                    case AiToolNames.TaskDependencies:
                        return TaskDependencies(snapshot, request);
                    case AiToolNames.ColumnTrace:
                        return ColumnTrace(snapshot, request);
                    case AiToolNames.DependencyGraph:
                        return Success(request, snapshot.Dependencies, snapshot.Dependencies.UnsupportedItems);
                    case AiToolNames.DependencyQuery:
                        return Dependency(snapshot, request);
                    case AiToolNames.ImpactAnalysis:
                        return Impact(snapshot, request);
                    case AiToolNames.QuestionPlan:
                        return BuildQuestionPlan(request);
                    default:
                        return AiToolResult.Failure(request.ToolName, "ai.tool.unsupported", "The requested read-only tool is not supported.");
                }
            }
            catch (ArgumentException)
            {
                return AiToolResult.Failure(request.ToolName, "ai.request.invalid", "The tool request is invalid. Selector values are omitted.");
            }
            catch (Exception)
            {
                return AiToolResult.Failure(request.ToolName, "ai.tool.failed", "The read-only analysis tool failed. Exception details are omitted.");
            }
        }

        private static AiToolResult Search(PackageAnalysisSnapshot snapshot, AiToolRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Query))
                return AiToolResult.Failure(request.ToolName, "ai.search.query_required", "A metadata query is required.");
            var result = new PackageSearchResult(snapshot.Package, snapshot.Catalog, request.Query, request.Kind);
            return Success(request, result, snapshot.UnsupportedItems.Where(item => item.ReasonCode == UnsupportedItem.ReadFailureCode));
        }

        private static AiToolResult TaskDependencies(PackageAnalysisSnapshot snapshot, AiToolRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.TaskId))
                return AiToolResult.Failure(request.ToolName, "ai.task.id_required", "A task native ID is required.");
            var query = new ControlFlowQuery().FindRelated(snapshot.ControlFlow, request.TaskId, request.Predecessors, request.Recursive);
            if (query.Items.Count != 1)
                return AiToolResult.Failure(request.ToolName, "ai.task.selection_invalid", "The task selection was missing or ambiguous.");
            var unsupported = snapshot.UnsupportedItems.Concat(query.UnsupportedItems).Distinct(new UnsupportedItemComparer()).ToArray();
            return Success(request, new PackageControlFlow(snapshot.Package, query.Items[0]), unsupported);
        }

        private static AiToolResult ColumnTrace(PackageAnalysisSnapshot snapshot, AiToolRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.FlowId) || string.IsNullOrWhiteSpace(request.ComponentId) || string.IsNullOrWhiteSpace(request.ColumnId))
                return AiToolResult.Failure(request.ToolName, "ai.lineage.selector_required", "Flow, component, and column selectors are required.");
            var flows = snapshot.DataFlows.Where(flow => string.Equals(flow.ExecutableId, request.FlowId, StringComparison.Ordinal)).ToList();
            if (flows.Count != 1)
                return AiToolResult.Failure(request.ToolName, "ai.lineage.selection_invalid", "The flow selection was missing or ambiguous.");
            var trace = new ColumnLineageQuery().Trace(flows[0], request.ComponentId, request.ColumnId, request.Upstream);
            if (trace.Items.Count != 1)
                return AiToolResult.Failure(request.ToolName, "ai.lineage.selection_invalid", "The column selection was missing or ambiguous.");
            var unsupported = snapshot.UnsupportedItems.Where(item => item.ReasonCode == UnsupportedItem.ReadFailureCode)
                .Concat(trace.UnsupportedItems).Distinct(new UnsupportedItemComparer()).ToArray();
            return Success(request, new PackageColumnTrace(snapshot.Package, trace.Items[0]), unsupported);
        }

        private static AiToolResult Dependency(PackageAnalysisSnapshot snapshot, AiToolRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.NodeKey))
            {
                return AiToolResult.Failure(request.ToolName, "ai.dependency.node_required", "A dependency node key is required.");
            }
            var query = new DependencyQuery().Find(snapshot.Dependencies, request.NodeKey, request.Incoming, request.Recursive);
            return query.Items.Count == 1
                ? Success(request, query.Items[0], query.UnsupportedItems)
                : AiToolResult.Failure(request.ToolName, "ai.dependency.selection_invalid", "The dependency node was missing or ambiguous.");
        }

        private static AiToolResult Impact(PackageAnalysisSnapshot snapshot, AiToolRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.NodeKey))
            {
                return AiToolResult.Failure(request.ToolName, "ai.impact.node_required", "An impact node key is required.");
            }
            var query = new ImpactAnalysisQuery().Analyze(snapshot.Dependencies, request.NodeKey);
            return query.Items.Count == 1
                ? Success(request, query.Items[0], query.UnsupportedItems)
                : AiToolResult.Failure(request.ToolName, "ai.impact.selection_invalid", "The impact node was missing or ambiguous.");
        }

        private static AiToolResult BuildQuestionPlan(AiToolRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Question))
            {
                return AiToolResult.Failure(request.ToolName, "ai.question.required", "A question is required.");
            }
            return Success(request, new QuestionPlanner().Plan(request.Question), Array.Empty<UnsupportedItem>());
        }

        private static AiToolResult Success(AiToolRequest request, object result, IEnumerable<UnsupportedItem> unsupported) =>
            new AiToolResult(request.ToolName, result, unsupported);

        private sealed class UnsupportedItemComparer : IEqualityComparer<UnsupportedItem>
        {
            public bool Equals(UnsupportedItem x, UnsupportedItem y) => ReferenceEquals(x, y) ||
                (x != null && y != null && x.Id == y.Id && x.Name == y.Name && x.CreationName == y.CreationName && x.ReasonCode == y.ReasonCode);
            public int GetHashCode(UnsupportedItem item)
            {
                if (item == null) return 0;
                unchecked { return (((item.Id.GetHashCode() * 397) ^ item.Name.GetHashCode()) * 397 ^ item.CreationName.GetHashCode()) * 397 ^ item.ReasonCode.GetHashCode(); }
            }
        }
    }
}