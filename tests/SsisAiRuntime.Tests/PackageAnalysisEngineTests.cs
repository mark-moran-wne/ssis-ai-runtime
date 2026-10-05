using SsisAiRuntime.AI;
using SsisAiRuntime.Inspectors;

namespace SsisAiRuntime.Tests;

public class PackageAnalysisEngineTests
{
    [Fact]
    public void SummaryAndSearchProduceBoundedMetadataOnlyContext()
    {
        var snapshot = Snapshot();
        var engine = new PackageAnalysisEngine();
        var summary = engine.Execute(snapshot, new AiToolRequest(AiToolNames.PackageSummary));
        Assert.True(summary.Succeeded);
        var context = new AiContextBuilder().Build(summary, 10);
        Assert.True(context.Succeeded);
        Assert.Equal("metadata-only", context.RedactionPolicy);
        Assert.Contains(context.Facts, fact => fact.Kind == "counts" && fact.Name == "executables");
        Assert.Equal(10, context.Facts.Count);
        Assert.Equal(3, context.FactsOmitted);

        var completeContext = new AiContextBuilder().Build(summary);
        Assert.Contains(completeContext.Facts, fact => fact.Kind == "task" && fact.Reference == "load");

        var tightlyBounded = new AiContextBuilder().Build(summary, 1);
        Assert.Single(tightlyBounded.Facts);
        Assert.Equal(12, tightlyBounded.FactsOmitted);

        var search = engine.Execute(snapshot, new AiToolRequest(AiToolNames.MetadataSearch, query: "Load"));
        Assert.True(search.Succeeded);
        Assert.Single(((PackageSearchResult)search.Result).Matches);
    }

    [Fact]
    public void TaskDependenciesUseNativeIdAndRejectMissingSelection()
    {
        var snapshot = Snapshot();
        var engine = new PackageAnalysisEngine();
        var result = engine.Execute(snapshot, new AiToolRequest(AiToolNames.TaskDependencies, taskId: "load", recursive: true));
        Assert.True(result.Succeeded);
        Assert.Single(((PackageControlFlow)result.Result).Graph.Nodes);
        var invalid = engine.Execute(snapshot, new AiToolRequest(AiToolNames.TaskDependencies));
        Assert.False(invalid.Succeeded);
        Assert.Equal("ai.task.id_required", invalid.ErrorCode);
    }

    private static PackageAnalysisSnapshot Snapshot()
    {
        var package = new PackageOverview(Guid.NewGuid(), "Demo", "package", "", DateTime.UnixEpoch,
            1, 0, 0, "DontSaveSensitive", "Default", 1, 0, 1, 0, 0, false);
        var connection = new ConnectionOverview("Warehouse", "connection", "OLEDB");
        var executable = new ExecutableOverview("load", "", "Load Warehouse", "Task", "", 0, false, false);
        var catalog = new SemanticHandleCatalogBuilder().Build(package, new[] { connection },
            Array.Empty<VariableOverview>(), Array.Empty<ParameterOverview>(), new[] { executable }, Array.Empty<DataFlowOverview>());
        var graph = new ControlFlowGraphBuilder().Build(new[] { executable },
            InspectionResult<PrecedenceConstraintOverview>.Complete(Array.Empty<PrecedenceConstraintOverview>()), catalog);
        return new PackageAnalysisSnapshot(package, new[] { connection }, Array.Empty<VariableOverview>(),
            Array.Empty<ParameterOverview>(), new[] { executable }, Array.Empty<SqlStatementOverview>(),
            Array.Empty<DataFlowOverview>(), Array.Empty<ExpressionOverview>(), graph, catalog, Array.Empty<UnsupportedItem>());
    }
}