using SsisAiRuntime.Inspectors;
using SsisAiRuntime.Inspectors.SqlDependencies;
using SsisAiRuntime.AI;

namespace SsisAiRuntime.Tests;

public sealed class SqlDependencyGraphTests
{
    [Fact]
    public void GraphScopesObjectsByConnectionAndPreservesEvidenceForImpact()
    {
        var graph = BaseGraph();
        var statements = new[] { Statement("a", "select * from dbo.Student; select * from dbo.Student;"), Statement("b", "select * from dbo.Student;") };
        var result = new SqlDependencyGraphBuilder().Enrich(graph, statements);
        Assert.Equal(2, result.Nodes.Count(node => node.Kind == SemanticObjectKind.SchemaObject));
        Assert.Equal(2, result.Edges.Count(edge => edge.Kind == DependencyKind.ReadsSchemaObject));
        Assert.All(result.Edges, edge => Assert.Equal("ParsedSchemaObject", edge.Evidence));
        var first = result.Nodes.Single(node => node.Kind == SemanticObjectKind.SchemaObject && node.ParentId == "a");
        Assert.Equal(string.Empty, first.NativeId);
        Assert.Equal(DependencySelectorResolutionStatus.Resolved, new DependencySelectorResolver().Resolve(result, first.Key).Status);
        Assert.Contains(new RichImpactAnalysisQuery().Analyze(result, first.Key).Items.Single().Impacts, item => item.Category == ImpactCategory.Sql);
        var facts = new AiContextBuilder().Build(new AiToolResult(AiToolNames.DependencyGraph, result, result.UnsupportedItems), 100).Facts;
        Assert.Contains(facts, fact => fact.Evidence == "ParsedSchemaObject");
    }

    [Fact]
    public void DynamicMalformedAndMissingConnectionsProduceGapsNotGuessedEdges()
    {
        var result = new SqlDependencyGraphBuilder().Enrich(BaseGraph(), new[] {
            Statement("a", "exec(@sql)"), Statement("a", "select * from dbo.Valid; select from"), Statement("missing", "select * from dbo.Valid") });
        Assert.Empty(result.Edges);
        Assert.DoesNotContain(result.Nodes, node => node.Kind == SemanticObjectKind.SchemaObject);
        Assert.Contains(result.UnsupportedItems, gap => gap.ReasonCode == "sql.dynamic_sql");
        Assert.Contains(result.UnsupportedItems, gap => gap.ReasonCode == "sql.parse_failed");
        Assert.Contains(result.UnsupportedItems, gap => gap.ReasonCode == "sql.connection_unresolved");
    }

    [Fact]
    public void IdentifierPartsCannotCollideOrInventDefaultSchema()
    {
        Assert.NotEqual(SqlDependencyGraphBuilder.ObjectKey("a", new[] { "dbo.Student" }), SqlDependencyGraphBuilder.ObjectKey("a", new[] { "dbo", "Student" }));
        Assert.NotEqual(SqlDependencyGraphBuilder.ObjectKey("a", new[] { "Student" }), SqlDependencyGraphBuilder.ObjectKey("a", new[] { "dbo", "Student" }));
    }

    [Fact]
    public void MergeProjectsConnectionScopedReadAndWriteEdges()
    {
        var sql = "merge dbo.Target as T using dbo.Source as S on T.Id=S.Id " +
            "when matched then update set Value=S.Value " +
            "when not matched then insert (Id, Value) values (S.Id, S.Value);";
        var graph = new SqlDependencyGraphBuilder().Enrich(BaseGraph(), new[] { Statement("a", sql) });
        var target = Assert.Single(graph.Nodes, node => node.Kind == SemanticObjectKind.SchemaObject &&
            node.Name == "[dbo].[Target]");
        var source = Assert.Single(graph.Nodes, node => node.Kind == SemanticObjectKind.SchemaObject &&
            node.Name == "[dbo].[Source]");

        Assert.Equal("a", target.ParentId);
        Assert.Equal("a", source.ParentId);
        Assert.Contains(graph.Edges, edge => edge.To == target.Key && edge.Kind == DependencyKind.WritesSchemaObject &&
            edge.Evidence == "ParsedSchemaObject");
        Assert.Contains(graph.Edges, edge => edge.To == source.Key && edge.Kind == DependencyKind.ReadsSchemaObject &&
            edge.Evidence == "ParsedSchemaObject");
    }

    private static SqlStatementOverview Statement(string connection, string sql) => new("task", "Task", "ExecuteSQLTask", "DirectInput", "", true, connection, "", 0, new SqlDependencyAnalyzer().Analyze(sql));
    private static PackageDependencyGraph BaseGraph() => new(new[] {
        new DependencyNode("Executable:task", SemanticObjectKind.Executable, "Task", "task"),
        new DependencyNode("Connection:a", SemanticObjectKind.Connection, "A", "a"),
        new DependencyNode("Connection:b", SemanticObjectKind.Connection, "B", "b") }, Array.Empty<DependencyEdge>(), Array.Empty<UnsupportedItem>());
}