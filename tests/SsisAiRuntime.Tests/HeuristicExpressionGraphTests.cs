using SsisAiRuntime.Inspectors;
using SsisAiRuntime.Inspectors.Expressions;
using SsisAiRuntime.AI;

namespace SsisAiRuntime.Tests;

public sealed class HeuristicExpressionGraphTests
{
    [Fact]
    public void OnlyUniqueScopeResolvedReferencesCreateHeuristicEdges()
    {
        var catalog = Catalog();
        var analyses = new[] { Analyze(catalog, "@[User::Value] + @[User::Value] + @[$Package::Size]"),
            Analyze(catalog, "@Ambiguous"), Analyze(catalog, "@Missing"), Analyze(catalog, "@[$Project::Size]"), Analyze(catalog, "@Secret +") };
        var nativeEdge = new DependencyEdge("Executable:task", "OutputColumn:column", DependencyKind.WritesColumn, "NativeParser");
        var graph = new PackageDependencyGraph(new[] { new DependencyNode("Executable:task", SemanticObjectKind.Executable, "Task", "task"),
            new DependencyNode("OutputColumn:column", SemanticObjectKind.OutputColumn, "Column", "column") }, new[] { nativeEdge }, Array.Empty<UnsupportedItem>());
        var enriched = new ExpressionDependencyGraphBuilder().Enrich(graph, catalog, analyses, Array.Empty<UnsupportedItem>());
        var variableEdge = Assert.Single(enriched.Edges, edge => edge.Kind == DependencyKind.UsesVariable);
        Assert.Equal("Executable:task", variableEdge.From);
        Assert.Equal("local", enriched.Nodes.Single(node => node.Key == variableEdge.To).NativeId);
        Assert.Equal("LexicalAndScopeResolved", variableEdge.Evidence);
        Assert.Single(enriched.Edges, edge => edge.Kind == DependencyKind.UsesParameter);
        Assert.Contains(nativeEdge, enriched.Edges);
        Assert.Equal(4, enriched.UnsupportedItems.Count);
        Assert.Contains(enriched.UnsupportedItems, gap => gap.ReasonCode == "expression.project_context_unavailable");
        Assert.Contains(enriched.UnsupportedItems, gap => gap.ReasonCode == "expression.reference_ambiguous");
        Assert.Contains(enriched.UnsupportedItems, gap => gap.ReasonCode == "expression.reference_not_found");
        Assert.Contains(enriched.UnsupportedItems, gap => gap.ReasonCode == "expression.parse_failed");
        Assert.DoesNotContain(enriched.UnsupportedItems, gap => gap.Name.Contains("Secret"));
        var impact = new DependencyQuery().Find(enriched, variableEdge.To, true, true);
        Assert.Contains(impact.Items.Single().Nodes, node => node.NativeId == "task");
        Assert.NotEqual(variableEdge.To, enriched.Nodes.Single(node => node.NativeId == "parent").Key);
        var selectors = new DependencySelectorResolver();
        Assert.Equal("local", selectors.Resolve(enriched, variableEdge.To, SemanticObjectKind.Variable).ResolvedNode.NativeId);
        Assert.Equal("size", selectors.Resolve(enriched, "size", SemanticObjectKind.Parameter).ResolvedNode.NativeId);
        Assert.Equal(DependencySelectorResolutionStatus.Ambiguous, selectors.Resolve(enriched, "User::Value", SemanticObjectKind.Variable).Status);
        var classified = new RichImpactAnalysisQuery().Analyze(enriched, variableEdge.To).Items.Single();
        Assert.Contains(classified.Impacts, item => item.Node.NativeId == "task" && item.Category == ImpactCategory.Variable);
        var facts = new AiContextBuilder().Build(new AiToolResult(AiToolNames.DependencyGraph, enriched, enriched.UnsupportedItems), 200).Facts;
        Assert.Contains(facts, fact => fact.Kind == "dependencyEdge.UsesVariable" && fact.Evidence == "LexicalAndScopeResolved");
    }

    private static ExpressionDependencyAnalysis Analyze(ExpressionScopeCatalog catalog, string text) =>
        new ExpressionDependencyAnalyzer(new ExpressionReferenceParser(), new ExpressionScopeResolver())
            .Analyze(new ExpressionDependencyInput("task", "task", "user-controlled-property", text), catalog);

    private static ExpressionScopeCatalog Catalog() => new(new[] {
        new ExpressionScope("package", "", "Package", ExpressionScopeKind.Package, new[] {
            new ExpressionSymbol("parent", "package", ExpressionSymbolKind.Variable, "User", "Value"),
            new ExpressionSymbol("size", "package", ExpressionSymbolKind.PackageParameter, "$Package", "Size") }),
        new ExpressionScope("task", "package", "Task", ExpressionScopeKind.Task, new[] {
            new ExpressionSymbol("local", "task", ExpressionSymbolKind.Variable, "User", "Value"),
            new ExpressionSymbol("first", "task", ExpressionSymbolKind.Variable, "User", "Ambiguous"),
            new ExpressionSymbol("second", "task", ExpressionSymbolKind.Variable, "Other", "Ambiguous") }) });
}