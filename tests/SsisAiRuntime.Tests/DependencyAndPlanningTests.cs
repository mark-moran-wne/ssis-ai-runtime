using SsisAiRuntime.AI;
using SsisAiRuntime.Inspectors;

namespace SsisAiRuntime.Tests;

public class DependencyAndPlanningTests
{
    [Fact]
    public void ImpactFindsConnectionConsumer()
    {
        var graph = new PackageDependencyGraph(new[]
        {
            new DependencyNode("Connection:c", SemanticObjectKind.Connection, "Warehouse", "c"),
            new DependencyNode("Executable:t", SemanticObjectKind.Executable, "Load", "t")
        }, new[]
        {
            new DependencyEdge("Executable:t", "Connection:c", DependencyKind.UsesConnection)
        }, Array.Empty<UnsupportedItem>());

        var result = new ImpactAnalysisQuery().Analyze(graph, "Connection:c");

        Assert.Equal("Load", result.Items.Single().ImpactedNodes.Single().Name);
    }

    [Fact]
    public void PlannerRoutesDependencyQuestionWithoutResolvingItsSelector()
    {
        var plan = new QuestionPlanner().Plan("What uses WarehouseDB?");

        Assert.Equal(QuestionIntent.DependencyQuery, plan.Intent);
        Assert.Equal(new[] { "nodeKey" }, plan.RequiredSelectors);
    }
}