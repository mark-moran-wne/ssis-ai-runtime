using SsisAiRuntime.Inspectors;

namespace SsisAiRuntime.Tests;

public class ControlFlowGraphBuilderTests
{
    [Fact]
    public void GraphBuildsContainmentAndPrecedenceEdgesUsingSemanticHandles()
    {
        var package = CreatePackageOverview();
        var executables = new[]
        {
            new ExecutableOverview("native-sequence", string.Empty, "Sequence", "Sequence", string.Empty, 0, true, false),
            new ExecutableOverview("native-source", "native-sequence", "Source", "Vendor.Source", string.Empty, 1, false, false),
            new ExecutableOverview("native-target", "native-sequence", "Target", "Vendor.Target", string.Empty, 1, false, false)
        };
        var constraint = new PrecedenceConstraintOverview(
            "native-constraint",
            "On success",
            "native-source",
            "Source",
            "native-target",
            "Target",
            "Constraint",
            "Success",
            true,
            false);
        var catalog = BuildCatalog(package, executables);

        var graph = new ControlFlowGraphBuilder().Build(
            executables,
            InspectionResult<PrecedenceConstraintOverview>.Complete(new[] { constraint }),
            catalog);

        Assert.Equal(3, graph.Nodes.Count);
        Assert.Equal(2, graph.Edges.Count(edge => edge.Kind == ControlFlowEdgeKind.Containment));
        var precedence = Assert.Single(graph.Edges, edge => edge.Kind == ControlFlowEdgeKind.Precedence);
        Assert.Equal("Source", graph.Nodes.Single(node => node.Handle.Equals(precedence.From)).Name);
        Assert.Equal("Target", graph.Nodes.Single(node => node.Handle.Equals(precedence.To)).Name);
        Assert.Equal("Success", precedence.ConstraintValue);
        Assert.True(graph.IsComplete);
        Assert.DoesNotContain("native-source", precedence.From.Value);
    }

    [Fact]
    public void GraphReportsMissingPrecedenceEndpointInsteadOfGuessing()
    {
        var package = CreatePackageOverview();
        var executables = new[]
        {
            new ExecutableOverview("native-source", string.Empty, "Source", "Vendor.Source", string.Empty, 0, false, false)
        };
        var constraint = new PrecedenceConstraintOverview(
            "constraint",
            "Missing target",
            "native-source",
            "Source",
            "unknown-target",
            "Missing",
            "Constraint",
            "Success",
            true,
            false);
        var catalog = BuildCatalog(package, executables);

        var graph = new ControlFlowGraphBuilder().Build(
            executables,
            InspectionResult<PrecedenceConstraintOverview>.Complete(new[] { constraint }),
            catalog);

        Assert.Empty(graph.Edges);
        Assert.False(graph.IsComplete);
        Assert.Contains(graph.UnsupportedItems, item => item.Reason.Contains("could not be resolved"));
    }

    private static SemanticHandleCatalog BuildCatalog(PackageOverview package, IEnumerable<ExecutableOverview> executables)
    {
        return new SemanticHandleCatalogBuilder().Build(
            package,
            Array.Empty<ConnectionOverview>(),
            Array.Empty<VariableOverview>(),
            Array.Empty<ParameterOverview>(),
            executables,
            Array.Empty<DataFlowOverview>());
    }

    private static PackageOverview CreatePackageOverview()
    {
        return new PackageOverview(
            Guid.NewGuid(),
            "Package",
            "native-package",
            string.Empty,
            DateTime.UnixEpoch,
            1,
            0,
            0,
            "EncryptSensitiveWithUserKey",
            "Default",
            0,
            0,
            0,
            0,
            0,
            false);
    }
}