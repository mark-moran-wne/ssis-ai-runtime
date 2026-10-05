using SsisAiRuntime.AI;
using SsisAiRuntime.Inspectors;

namespace SsisAiRuntime.Tests;

public class AiToolUnificationTests
{
    [Fact]
    public void PlannerRunsWithoutPackageSnapshot()
    {
        var result = new PackageAnalysisEngine().Plan(new AiToolRequest(
            AiToolNames.QuestionPlan, question: "What uses WarehouseDB?"));

        Assert.True(result.Succeeded);
        Assert.Equal(QuestionIntent.DependencyQuery, ((QuestionPlan)result.Result).Intent);
    }

    [Fact]
    public void EngineRunsDependencyAndImpactTools()
    {
        var snapshot = Snapshot();
        var engine = new PackageAnalysisEngine();

        Assert.IsType<PackageDependencyGraph>(engine.Execute(snapshot,
            new AiToolRequest(AiToolNames.DependencyGraph)).Result);
        Assert.IsType<PackageDependencyGraph>(engine.Execute(snapshot,
            new AiToolRequest(AiToolNames.DependencyQuery, nodeKey: "Connection:c", recursive: true)).Result);
        Assert.IsType<ImpactAnalysisResult>(engine.Execute(snapshot,
            new AiToolRequest(AiToolNames.ImpactAnalysis, nodeKey: "Connection:c")).Result);
    }

    [Fact]
    public void BuilderRejectsDanglingConnectionEdges()
    {
        var package = Package();
        var task = new ExecutableOverview("t", "", "Load", "Task", "", 0, false, false);
        var catalog = new SemanticHandleCatalogBuilder().Build(package, Array.Empty<ConnectionOverview>(),
            Array.Empty<VariableOverview>(), Array.Empty<ParameterOverview>(), new[] { task }, Array.Empty<DataFlowOverview>());
        var controlFlow = new ControlFlowGraphBuilder().Build(new[] { task },
            InspectionResult<PrecedenceConstraintOverview>.Complete(Array.Empty<PrecedenceConstraintOverview>()), catalog);
        var sql = new SqlStatementOverview("t", "Load", "SQL", "DirectInput", "", true, "missing", "Missing", 0);

        var graph = new PackageDependencyGraphBuilder().Build(package, Array.Empty<ConnectionOverview>(),
            new[] { task }, new[] { sql }, Array.Empty<DataFlowOverview>(), controlFlow, Array.Empty<UnsupportedItem>());

        Assert.DoesNotContain(graph.Edges, edge => edge.To == "Connection:missing");
        Assert.Contains(graph.UnsupportedItems, item => item.ReasonCode == UnsupportedItem.UnsupportedMetadataCode);
    }

    private static PackageAnalysisSnapshot Snapshot()
    {
        var package = Package();
        var connection = new ConnectionOverview("Warehouse", "c", "OLEDB");
        var task = new ExecutableOverview("t", "", "Load", "Task", "", 0, false, false);
        var catalog = new SemanticHandleCatalogBuilder().Build(package, new[] { connection },
            Array.Empty<VariableOverview>(), Array.Empty<ParameterOverview>(), new[] { task }, Array.Empty<DataFlowOverview>());
        var controlFlow = new ControlFlowGraphBuilder().Build(new[] { task },
            InspectionResult<PrecedenceConstraintOverview>.Complete(Array.Empty<PrecedenceConstraintOverview>()), catalog);
        var sql = new SqlStatementOverview("t", "Load", "SQL", "DirectInput", "", true, "c", "Warehouse", 0);
        var dependencies = new PackageDependencyGraphBuilder().Build(package, new[] { connection }, new[] { task },
            new[] { sql }, Array.Empty<DataFlowOverview>(), controlFlow, Array.Empty<UnsupportedItem>());
        return new PackageAnalysisSnapshot(package, new[] { connection }, Array.Empty<VariableOverview>(),
            Array.Empty<ParameterOverview>(), new[] { task }, new[] { sql }, Array.Empty<DataFlowOverview>(),
            Array.Empty<ExpressionOverview>(), controlFlow, dependencies, catalog, Array.Empty<UnsupportedItem>());
    }

    private static PackageOverview Package() => new(Guid.NewGuid(), "Demo", "p", "", DateTime.UnixEpoch,
        1, 0, 0, "DontSaveSensitive", "Default", 1, 0, 1, 0, 0, false);
}