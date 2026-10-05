using Newtonsoft.Json;
using SsisAiRuntime.AI;
using SsisAiRuntime.Inspectors;

namespace SsisAiRuntime.Tests;

public sealed class LlmContextTests
{
    [Fact]
    public void DefaultContextCannotEmitPrecollectedText()
    {
        var snapshot = Snapshot(new[] { SanitizedTextFactory.Sql("t", "c", "select 'secret' from dbo.Student") });
        var result = new PackageAnalysisEngine().Execute(snapshot, new AiToolRequest(AiToolNames.LlmContext));
        var context = Assert.IsType<LlmContext>(result.Result);
        Assert.False(context.SanitizedTextIncluded);
        Assert.Empty(context.SanitizedTexts);
        Assert.Equal("metadata-only", context.RedactionPolicy);
        Assert.DoesNotContain("sanitized-context-opt-in", JsonConvert.SerializeObject(context));
    }

    [Fact]
    public void OptInRequiresCollectionAndRemainsBoundedAndEvidencePreserving()
    {
        Assert.Throws<ArgumentException>(() => new LlmContextBuilder().Build(Snapshot(), true));
        var snapshot = Snapshot(Enumerable.Range(0, 25).Select(index => SanitizedTextFactory.Sql("t", "c", "select 'secret' from dbo.Student")));
        var context = new LlmContextBuilder().Build(snapshot, true, 2);
        Assert.Equal("sanitized-context-opt-in", context.RedactionPolicy);
        Assert.Equal(20, context.SanitizedTexts.Count);
        Assert.Equal(5, context.TextsOmitted);
        Assert.All(context.DependencyEdges, edge => Assert.Equal("ParsedSchemaObject", edge.Evidence));
        Assert.DoesNotContain("secret", JsonConvert.SerializeObject(context));
        Assert.Contains("untrusted", context.InterpretationBoundary);
        Assert.Contains("do not invent edges", context.InterpretationBoundary);
    }

    [Fact]
    public void OmittedTextIsExplicitCoverageNotAnEmptySuccess()
    {
        var snapshot = Snapshot(new[] { SanitizedTextFactory.Sql("t", "c", "select 'secret' from") });
        var result = new PackageAnalysisEngine().Execute(snapshot, new AiToolRequest(AiToolNames.LlmContext, includeSanitizedText: true));
        Assert.True(result.Succeeded);
        Assert.False(result.IsComplete);
        Assert.Contains(result.UnsupportedItems, gap => gap.ReasonCode == "context.text_unavailable");
    }

    private static PackageAnalysisSnapshot Snapshot(IEnumerable<SanitizedTextSnippet>? texts = null)
    {
        var package = new PackageOverview(Guid.NewGuid(), "Demo", "p", "", DateTime.UnixEpoch, 1, 0, 0, "DontSaveSensitive", "Default", 1, 0, 1, 0, 0, false);
        var connection = new ConnectionOverview("Warehouse", "c", "OLEDB");
        var task = new ExecutableOverview("t", "", "Load", "Task", "", 0, false, false);
        var catalog = new SemanticHandleCatalogBuilder().Build(package, new[] { connection }, Array.Empty<VariableOverview>(), Array.Empty<ParameterOverview>(), new[] { task }, Array.Empty<DataFlowOverview>());
        var control = new ControlFlowGraphBuilder().Build(new[] { task }, InspectionResult<PrecedenceConstraintOverview>.Complete(Array.Empty<PrecedenceConstraintOverview>()), catalog);
        var graph = new PackageDependencyGraph(new[] {
            new DependencyNode("Executable:t", SemanticObjectKind.Executable, "Load", "t"),
            new DependencyNode("SchemaObject:c", SemanticObjectKind.SchemaObject, "[dbo].[Student]", "", "c") },
            new[] { new DependencyEdge("Executable:t", "SchemaObject:c", DependencyKind.ReadsSchemaObject, "ParsedSchemaObject") }, Array.Empty<UnsupportedItem>());
        return new PackageAnalysisSnapshot(package, new[] { connection }, Array.Empty<VariableOverview>(), Array.Empty<ParameterOverview>(), new[] { task }, Array.Empty<SqlStatementOverview>(),
            Array.Empty<DataFlowOverview>(), Array.Empty<ExpressionOverview>(), control, graph, catalog, Array.Empty<UnsupportedItem>(), texts);
    }
}