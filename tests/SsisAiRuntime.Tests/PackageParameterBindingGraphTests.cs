using SsisAiRuntime.Inspectors;
using SsisAiRuntime.Inspectors.Expressions;

namespace SsisAiRuntime.Tests;

public sealed class PackageParameterBindingGraphTests
{
    [Fact]
    public void ParentVariableBindsToChildPackageParameterSeparatelyFromInvocation()
    {
        Assert.Equal("DeclaredPackageParameterBinding", DependencyEvidence.Safe("DeclaredPackageParameterBinding"));
        var parentVariable = new ExpressionSymbol("parent-variable", "parent", ExpressionSymbolKind.Variable,
            "User", "ParentInput");
        var graph = Build("User::ParentInput", "ChildInput", new[] { parentVariable });
        var task = Assert.Single(graph.Nodes, node => node.Kind == SemanticObjectKind.Executable);
        var source = Assert.Single(graph.Nodes, node => node.Kind == SemanticObjectKind.Variable &&
            node.NativeId == parentVariable.NativeId);
        var childParameter = Assert.Single(graph.Nodes, node => node.Kind == SemanticObjectKind.Parameter &&
            node.NativeId == "child-parameter");
        var childPackage = Assert.Single(graph.Nodes, node => node.Kind == SemanticObjectKind.Package &&
            node.NativeId == "child");
        var binding = Assert.Single(graph.Nodes, node => node.Kind == SemanticObjectKind.PackageParameterBinding);

        Assert.Contains(graph.Edges, edge => edge.From == task.Key && edge.To == binding.Key &&
            edge.Kind == DependencyKind.HasPackageParameterBinding && edge.Evidence == "DeclaredPackageParameterBinding");
        Assert.Contains(graph.Edges, edge => edge.From == binding.Key && edge.To == source.Key &&
            edge.Kind == DependencyKind.UsesVariable && edge.Evidence == "LexicalAndScopeResolved");
        Assert.Contains(graph.Edges, edge => edge.From == binding.Key && edge.To == childParameter.Key &&
            edge.Kind == DependencyKind.BindsPackageParameter && edge.Evidence == "DeclaredPackageParameterBinding");
        Assert.Contains(graph.Edges, edge => edge.From == task.Key && edge.To == childPackage.Key &&
            edge.Kind == DependencyKind.InvokesPackage && edge.Evidence == "ProjectInventoryResolved");
        Assert.DoesNotContain(graph.Edges, edge => edge.To == childParameter.Key &&
            edge.Kind == DependencyKind.UsesParameter);
    }

    [Fact]
    public void ParentProjectParameterBindsToChildPackageParameter()
    {
        var projectParameter = new ExpressionSymbol("project-parameter", "parent", ExpressionSymbolKind.ProjectParameter,
            "$Project", "ProjectInput");
        var graph = Build("$Project::ProjectInput", "ChildInput", new[] { projectParameter });
        var task = Assert.Single(graph.Nodes, node => node.Kind == SemanticObjectKind.Executable);
        var source = Assert.Single(graph.Nodes, node => node.Kind == SemanticObjectKind.Parameter &&
            node.NativeId == projectParameter.NativeId);
        var target = Assert.Single(graph.Nodes, node => node.Kind == SemanticObjectKind.Parameter &&
            node.NativeId == "child-parameter");
        var binding = Assert.Single(graph.Nodes, node => node.Kind == SemanticObjectKind.PackageParameterBinding);

        Assert.Contains(graph.Edges, edge => edge.From == task.Key && edge.To == binding.Key &&
            edge.Kind == DependencyKind.HasPackageParameterBinding);
        Assert.Contains(graph.Edges, edge => edge.From == binding.Key && edge.To == source.Key &&
            edge.Kind == DependencyKind.UsesParameter && edge.Evidence == "LexicalAndScopeResolved");
        Assert.Contains(graph.Edges, edge => edge.From == binding.Key && edge.To == target.Key &&
            edge.Kind == DependencyKind.BindsPackageParameter);
        Assert.DoesNotContain(graph.UnsupportedItems, item =>
            item.ReasonCode == UnsupportedItem.PackageParameterBindingUnresolvedCode);
    }

    [Fact]
    public void AmbiguousParentSourceDoesNotCreateBindingEdges()
    {
        var graph = Build("User::ParentInput", "ChildInput", new[]
        {
            new ExpressionSymbol("parent-variable-a", "parent", ExpressionSymbolKind.Variable, "User", "ParentInput"),
            new ExpressionSymbol("parent-variable-b", "parent", ExpressionSymbolKind.Variable, "User", "ParentInput")
        });

        Assert.DoesNotContain(graph.Edges, edge => edge.Kind == DependencyKind.BindsPackageParameter ||
            edge.Evidence == "DeclaredPackageParameterBinding");
        Assert.Contains(graph.UnsupportedItems, item =>
            item.ReasonCode == UnsupportedItem.PackageParameterBindingAmbiguousCode);
    }

    [Fact]
    public void MissingChildParameterDoesNotCreateBindingEdge()
    {
        var graph = Build("User::ParentInput", "MissingChildInput", new[]
        {
            new ExpressionSymbol("parent-variable", "parent", ExpressionSymbolKind.Variable, "User", "ParentInput")
        });

        Assert.DoesNotContain(graph.Edges, edge => edge.Kind == DependencyKind.BindsPackageParameter);
        Assert.Contains(graph.UnsupportedItems, item =>
            item.ReasonCode == UnsupportedItem.PackageParameterBindingUnresolvedCode);
    }

    private static PackageDependencyGraph Build(string sourceReference, string childParameterName,
        IReadOnlyList<ExpressionSymbol> parentSymbols)
    {
        var inventory = new ProjectInventory("project", "ProjectDeployment", new[]
        {
            new ProjectPackageInfo("parent", "Parent.dtsx"),
            new ProjectPackageInfo("child", "ChildPackage", new[]
            {
                new PackageParameterInfo("child-parameter", "ChildInput")
            })
        }, parentSymbols.Where(symbol => symbol.Kind == ExpressionSymbolKind.ProjectParameter)
            .Select(symbol => new ProjectParameterInfo(symbol.NativeId, symbol.Name)));
        var package = new PackageOverview(Guid.NewGuid(), "Parent", "parent", string.Empty, DateTime.UnixEpoch,
            1, 0, 0, "DontSaveSensitive", "Default", 0, 0, 1, 0, 0, false);
        var task = new ExecutableOverview("execute-package", string.Empty, "Run Child Package",
            "Microsoft.SqlServer.Dts.Tasks.ExecutePackageTask.ExecutePackageTask, Microsoft.SqlServer.ExecPkgTask",
            string.Empty, 0, false, false, "ProjectReference", "ChildPackage",
            new[] { new ExecutePackageParameterBindingOverview(childParameterName, sourceReference) });
        var emptyControlFlow = new ControlFlowGraph(Array.Empty<ControlFlowNode>(),
            Array.Empty<ControlFlowEdge>(), Array.Empty<UnsupportedItem>());
        var graph = new PackageDependencyGraphBuilder().Build(package, Array.Empty<ConnectionOverview>(),
            new[] { task }, Array.Empty<SqlStatementOverview>(), Array.Empty<DataFlowOverview>(),
            emptyControlFlow, Array.Empty<UnsupportedItem>(), inventory);
        var scopeCatalog = new ExpressionScopeCatalog(new[]
        {
            new ExpressionScope("parent", string.Empty, "Parent", ExpressionScopeKind.Package, parentSymbols),
            new ExpressionScope("execute-package", "parent", "Run Child Package", ExpressionScopeKind.Task,
                Array.Empty<ExpressionSymbol>())
        }, projectContextAvailable: parentSymbols.Any(symbol => symbol.Kind == ExpressionSymbolKind.ProjectParameter));
        graph = new ExpressionDependencyGraphBuilder().Enrich(graph, scopeCatalog,
            Array.Empty<ExpressionDependencyAnalysis>(), Array.Empty<UnsupportedItem>());
        return new PackageParameterBindingGraphBuilder().Enrich(graph, new[] { task }, inventory, scopeCatalog);
    }
}
