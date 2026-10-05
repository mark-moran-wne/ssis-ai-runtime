using SsisAiRuntime.AI;
using SsisAiRuntime.Inspectors;

namespace SsisAiRuntime.Tests;

public class SelectorAndRichImpactTests
{
    [Fact]
    public void ResolverNeverChoosesAnAmbiguousName()
    {
        var graph = BuildGraph(duplicateConnections: true);
        var result = new DependencySelectorResolver().Resolve(graph, "Warehouse", SemanticObjectKind.Connection);

        Assert.Equal(DependencySelectorResolutionStatus.Ambiguous, result.Status);
        Assert.Null(result.ResolvedNode);
        Assert.Equal(2, result.CandidateCount);
        Assert.Equal(2, result.Candidates.Count);
    }

    [Fact]
    public void ResolverPrefersExactKeyAndAcceptsNativeId()
    {
        var graph = BuildGraph();

        Assert.Equal("Connection:c", new DependencySelectorResolver().Resolve(graph, "Connection:c").ResolvedNode.Key);
        Assert.Equal("Connection:c", new DependencySelectorResolver().Resolve(graph, "c", SemanticObjectKind.Connection).ResolvedNode.Key);
    }

    [Fact]
    public void RichImpactClassifiesDirectAndIndirectConsumers()
    {
        var result = new RichImpactAnalysisQuery().Analyze(BuildGraph(), "Connection:c").Items.Single();

        Assert.Contains(result.Impacts, impact => impact.Node.Key == "Executable:t" &&
            impact.Category == ImpactCategory.Configuration && impact.Distance == 1);
        Assert.Contains(result.Impacts, impact => impact.Node.Key == "Executable:u" &&
            impact.Category == ImpactCategory.Indirect && impact.Distance == 2);
        Assert.Contains("1 direct", new AnalysisNarrativeBuilder().Build(result).Summary);
        Assert.Equal("metadata-only", new AnalysisNarrativeBuilder().Build(result).RedactionPolicy);
    }

    [Fact]
    public void EngineRoutesSelectorAndClassifiedImpactTools()
    {
        var graph = BuildGraph();
        var snapshot = BuildSnapshot(graph);
        var engine = new PackageAnalysisEngine();

        var resolved = engine.Execute(snapshot, new AiToolRequest(AiToolNames.ResolveSelector,
            selector: "Warehouse", kind: SemanticObjectKind.Connection));
        var impact = engine.Execute(snapshot, new AiToolRequest(AiToolNames.RichImpactAnalysis, nodeKey: "Connection:c"));

        Assert.Equal(DependencySelectorResolutionStatus.Resolved, ((DependencySelectorResolution)resolved.Result).Status);
        Assert.IsType<RichImpactAnalysisResult>(impact.Result);
    }

    private static PackageDependencyGraph BuildGraph(bool duplicateConnections = false)
    {
        var nodes = new List<DependencyNode>
        {
            new("Connection:c", SemanticObjectKind.Connection, "Warehouse", "c"),
            new("Executable:t", SemanticObjectKind.Executable, "Load", "t"),
            new("Executable:u", SemanticObjectKind.Executable, "Publish", "u")
        };
        if (duplicateConnections)
        {
            nodes.Add(new DependencyNode("Connection:c2", SemanticObjectKind.Connection, "Warehouse", "c2"));
        }
        return new PackageDependencyGraph(nodes, new[]
        {
            new DependencyEdge("Executable:t", "Connection:c", DependencyKind.UsesConnection),
            new DependencyEdge("Executable:u", "Executable:t", DependencyKind.DependsOnTask)
        }, Array.Empty<UnsupportedItem>());
    }

    private static PackageAnalysisSnapshot BuildSnapshot(PackageDependencyGraph dependencies)
    {
        var package = new PackageOverview(Guid.NewGuid(), "Demo", "p", "", DateTime.UnixEpoch,
            1, 0, 0, "DontSaveSensitive", "Default", 1, 0, 2, 0, 0, false);
        var connection = new ConnectionOverview("Warehouse", "c", "OLEDB");
        var firstTask = new ExecutableOverview("t", "", "Load", "Task", "", 0, false, false);
        var secondTask = new ExecutableOverview("u", "", "Publish", "Task", "", 0, false, false);
        var catalog = new SemanticHandleCatalogBuilder().Build(package, new[] { connection },
            Array.Empty<VariableOverview>(), Array.Empty<ParameterOverview>(), new[] { firstTask, secondTask },
            Array.Empty<DataFlowOverview>());
        var controlFlow = new ControlFlowGraphBuilder().Build(new[] { firstTask, secondTask },
            InspectionResult<PrecedenceConstraintOverview>.Complete(Array.Empty<PrecedenceConstraintOverview>()), catalog);
        return new PackageAnalysisSnapshot(package, new[] { connection }, Array.Empty<VariableOverview>(),
            Array.Empty<ParameterOverview>(), new[] { firstTask, secondTask }, Array.Empty<SqlStatementOverview>(),
            Array.Empty<DataFlowOverview>(), Array.Empty<ExpressionOverview>(), controlFlow, dependencies, catalog,
            Array.Empty<UnsupportedItem>());
    }
}