using SsisAiRuntime.Inspectors;

namespace SsisAiRuntime.Tests;

public class ExpressionDependencyGraphTests
{
    [Fact]
    public void ContractOmitsExpressionTextAndValues()
    {
        var dependency = new ExpressionDependencyOverview("Task", "task-id", "Load", "Task", "Connection",
            new[] { new ExpressionReferenceOverview(ExpressionReferenceKind.Variable, "variable-id", "User", "CampusCode") });

        Assert.True(dependency.ExpressionTextOmitted);
        Assert.True(dependency.ValuesOmitted);
        Assert.Null(typeof(ExpressionDependencyOverview).GetProperty("Expression"));
        Assert.Null(typeof(ExpressionReferenceOverview).GetProperty("Value"));
    }

    [Fact]
    public void EnrichmentAddsVariableParameterOwnerAndProvenReferenceEdges()
    {
        var result = new ExpressionDependencyGraphBuilder().Enrich(BaseGraph(),
            new[] { new VariableOverview("CampusCode", "User", "String", false, false, false, false) },
            new[] { new ParameterOverview("parameter-id", "EnvironmentName", "PackageParameter", "", "String", false, false) },
            InspectionResult<ExpressionDependencyOverview>.Complete(new[]
            {
                new ExpressionDependencyOverview("Task", "task-id", "Load", "Task", "Connection", new[]
                {
                    new ExpressionReferenceOverview(ExpressionReferenceKind.Variable, "", "User", "CampusCode"),
                    new ExpressionReferenceOverview(ExpressionReferenceKind.Parameter, "parameter-id", "", "EnvironmentName")
                })
            }));

        Assert.Contains(result.Nodes, item => item.Kind == SemanticObjectKind.Variable);
        Assert.Contains(result.Nodes, item => item.Kind == SemanticObjectKind.Parameter);
        Assert.Contains(result.Nodes, item => item.Kind == SemanticObjectKind.ExpressionOwner);
        Assert.Contains(result.Edges, item => item.Kind == DependencyKind.HasExpression);
        Assert.Contains(result.Edges, item => item.Kind == DependencyKind.UsesVariable);
        Assert.Contains(result.Edges, item => item.Kind == DependencyKind.UsesParameter);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void MissingReferenceBecomesCoverageGapWithoutInventingEdge()
    {
        var result = new ExpressionDependencyGraphBuilder().Enrich(BaseGraph(), Array.Empty<VariableOverview>(),
            Array.Empty<ParameterOverview>(), InspectionResult<ExpressionDependencyOverview>.Complete(new[]
            {
                new ExpressionDependencyOverview("Task", "task-id", "Load", "Task", "Connection", new[]
                {
                    new ExpressionReferenceOverview(ExpressionReferenceKind.Variable, "", "User", "Missing")
                })
            }));

        Assert.DoesNotContain(result.Edges, item => item.Kind == DependencyKind.UsesVariable);
        Assert.Contains(result.UnsupportedItems, item => item.ReasonCode == UnsupportedItem.UnsupportedMetadataCode);
    }

    [Fact]
    public void InspectorCoverageFlowsIntoEnrichedGraph()
    {
        var gap = new UnsupportedItem("task-id", "Load", "Task", "Native references could not be proven.",
            UnsupportedItem.UnsupportedMetadataCode);
        var result = new ExpressionDependencyGraphBuilder().Enrich(BaseGraph(), Array.Empty<VariableOverview>(),
            Array.Empty<ParameterOverview>(), new InspectionResult<ExpressionDependencyOverview>(
                Array.Empty<ExpressionDependencyOverview>(), new[] { gap }));

        Assert.Contains(result.UnsupportedItems, item => item.Id == "task-id");
    }

    [Fact]
    public void VariableKeysPreserveNamespaceAndAvoidNameCollision()
    {
        Assert.NotEqual(ExpressionDependencyGraphBuilder.VariableKey("User", "Mode"),
            ExpressionDependencyGraphBuilder.VariableKey("System", "Mode"));
    }

    private static PackageDependencyGraph BaseGraph()
    {
        var node = new DependencyNode("Executable:task-id", SemanticObjectKind.Executable, "Load", "task-id");
        return new PackageDependencyGraph(new[] { node }, Array.Empty<DependencyEdge>(), Array.Empty<UnsupportedItem>());
    }
}