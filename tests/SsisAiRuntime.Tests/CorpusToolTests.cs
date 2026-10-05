using SsisAiRuntime.AI;
using SsisAiRuntime.Inspectors;
using SsisAiRuntime.Cli;
using Newtonsoft.Json.Linq;

namespace SsisAiRuntime.Tests;

public sealed class CorpusToolTests
{
    [Fact]
    public void SnapshotBuilderProjectsDependencyEvidenceAndCoverage()
    {
        var package = Package();
        var graph = new PackageDependencyGraph(new[]
        {
            new DependencyNode("Connection:c", SemanticObjectKind.Connection, "Warehouse", "c"),
            new DependencyNode("Executable:t", SemanticObjectKind.Executable, "Load", "t")
        }, new[]
        {
            new DependencyEdge("Executable:t", "Connection:c", DependencyKind.UsesConnection, "ParsedSchemaObject"),
            new DependencyEdge("Executable:t", "Connection:c", DependencyKind.UsesConnection, "LexicalAndScopeResolved")
        }, new[]
        {
            new UnsupportedItem("t", "Load", "Task", "coverage", "sql.dynamic_sql"),
            new UnsupportedItem("t", "Load", "Task", "coverage", "sql.dynamic_sql"),
            new UnsupportedItem("x", "Expr", "Task", "coverage", "expression.reference_not_found")
        });

        var snapshot = AnalysisSnapshot(package, graph);

        var corpus = new CorpusSnapshotBuilder().Build(snapshot);

        Assert.Equal(CorpusSchema.CurrentVersion, corpus.SchemaVersion);
        Assert.Equal(2, corpus.Nodes.Count);
        Assert.Equal(2, corpus.Edges.Count);
        Assert.Contains(corpus.Edges, edge => edge.Evidence == "ParsedSchemaObject");
        Assert.Contains(corpus.Edges, edge => edge.Evidence == "LexicalAndScopeResolved");
        Assert.Equal(2, corpus.CoverageGaps.Count);
        Assert.Contains(corpus.CoverageGaps, gap => gap.ReasonCode == "sql.dynamic_sql" && gap.Count == 2);
        Assert.Contains(corpus.CoverageGaps, gap => gap.ReasonCode == "expression.reference_not_found" && gap.Count == 1);
    }

    [Fact]
    public void DiffEngineReportsNodeEdgeAndCoverageDeltas()
    {
        var baseline = new CorpusSnapshot(CorpusSchema.CurrentVersion, "p", "Demo",
            new[] { new CorpusNode("Package:p", SemanticObjectKind.Package, "Demo", "p", "") },
            new[] { new CorpusEdge("Executable:a", "Connection:c", DependencyKind.UsesConnection, "ParsedSchemaObject") },
            new[] { new CorpusCoverageGap("sql.dynamic_sql", 1) });

        var candidate = new CorpusSnapshot(CorpusSchema.CurrentVersion, "p", "Demo",
            new[]
            {
                new CorpusNode("Package:p", SemanticObjectKind.Package, "Demo", "p", ""),
                new CorpusNode("Executable:a", SemanticObjectKind.Executable, "Load", "a", "")
            },
            new[] { new CorpusEdge("Executable:a", "Connection:c", DependencyKind.UsesConnection, "ParsedExecuteTarget") },
            new[] { new CorpusCoverageGap("sql.dynamic_sql", 3), new CorpusCoverageGap("sql.parse_failed", 1) });

        var diff = new CorpusDiffEngine().Diff(baseline, candidate);

        Assert.False(diff.IsMatch);
        Assert.Equal(new[] { "Executable:a" }, diff.AddedNodes);
        Assert.Empty(diff.RemovedNodes);
        Assert.Single(diff.AddedEdges);
        Assert.Single(diff.RemovedEdges);
        Assert.Contains(diff.AddedCoverageGaps, gap => gap.ReasonCode == "sql.dynamic_sql" && gap.Count == 2);
        Assert.Contains(diff.AddedCoverageGaps, gap => gap.ReasonCode == "sql.parse_failed" && gap.Count == 1);
        Assert.Empty(diff.RemovedCoverageGaps);
    }

    [Fact]
    public void DiffDetectsChangedNodeAndPackageMetadata()
    {
        var baseline = new CorpusSnapshot(CorpusSchema.CurrentVersion, "p", "Demo",
            new[] { new CorpusNode("Package:p", SemanticObjectKind.Package, "Demo", "p", "") },
            Array.Empty<CorpusEdge>(), Array.Empty<CorpusCoverageGap>());
        var candidate = new CorpusSnapshot(CorpusSchema.CurrentVersion, "p", "Renamed",
            new[] { new CorpusNode("Package:p", SemanticObjectKind.Package, "Renamed", "p", "parent") },
            Array.Empty<CorpusEdge>(), Array.Empty<CorpusCoverageGap>());

        var diff = new CorpusDiffEngine().Diff(baseline, candidate);

        Assert.False(diff.IsMatch);
        Assert.True(diff.PackageChanged);
        Assert.Equal(new[] { "Package:p" }, diff.ChangedNodes);
    }

    [Fact]
    public void SystemVariableRuntimeIdsDoNotCauseCorpusDrift()
    {
        var package = Package();
        var first = SystemVariableGraph("{runtime-id-one}");
        var second = SystemVariableGraph("{runtime-id-two}");

        var baseline = new CorpusSnapshotBuilder().Build(AnalysisSnapshot(package, first));
        var candidate = new CorpusSnapshotBuilder().Build(AnalysisSnapshot(package, second));
        var diff = new CorpusDiffEngine().Diff(baseline, candidate);

        Assert.True(diff.IsMatch);
        var variable = Assert.Single(baseline.Nodes, node => node.Name == "System::StartTime");
        Assert.Empty(variable.NativeId);
        Assert.Contains(baseline.Edges, edge => edge.To == variable.Key);
    }

    [Fact]
    public void BaselineReaderRejectsIncompatibleEmbeddedVersionAndUnknownEnums()
    {
        var snapshot = new CorpusSnapshot(CorpusSchema.CurrentVersion, "p", "Demo",
            new[] { new CorpusNode("Package:p", SemanticObjectKind.Package, "Demo", "p", "") },
            Array.Empty<CorpusEdge>(), Array.Empty<CorpusCoverageGap>());

        var incompatible = CorpusBaselineStore.CreateDocument(snapshot, "corpus.approve", "package.dtsx", "", false);
        incompatible["snapshot"]!["schemaVersion"] = "2.0";
        AssertBaselineRejected(incompatible, "corpus.baseline.incompatible");

        var unknownKind = CorpusBaselineStore.CreateDocument(snapshot, "corpus.approve", "package.dtsx", "", false);
        unknownKind["snapshot"]!["nodes"]![0]!["kind"] = "UnknownKind";
        AssertBaselineRejected(unknownKind, "corpus.baseline.invalid");

        var mismatchedVersions = CorpusBaselineStore.CreateDocument(snapshot, "corpus.approve", "package.dtsx", "", false);
        mismatchedVersions["schemaVersion"] = "1.1";
        AssertBaselineRejected(mismatchedVersions, "corpus.baseline.invalid");

        var inconsistentCount = CorpusBaselineStore.CreateDocument(snapshot, "corpus.approve", "package.dtsx", "", false);
        inconsistentCount["snapshot"]!["nodeCount"] = 0;
        AssertBaselineRejected(inconsistentCount, "corpus.baseline.invalid");
    }

    private static void AssertBaselineRejected(JObject document, string expectedCode)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, document.ToString());
            var loaded = CorpusBaselineStore.TryRead(path, out _, out var errorCode, out _, out _);
            Assert.False(loaded);
            Assert.Equal(expectedCode, errorCode);
        }
        finally
        {
            if (File.Exists(path)) { File.Delete(path); }
        }
    }

    private static PackageDependencyGraph SystemVariableGraph(string runtimeId) => new(new[]
    {
        new DependencyNode("Executable:scope", SemanticObjectKind.Executable, "Task", "scope"),
        new DependencyNode("Variable:SystemVariable:" + Uri.EscapeDataString(runtimeId),
            SemanticObjectKind.Variable, "System::StartTime", runtimeId, "scope")
    }, new[]
    {
        new DependencyEdge("Executable:scope", "Variable:SystemVariable:" + Uri.EscapeDataString(runtimeId),
            DependencyKind.UsesVariable, "LexicalAndScopeResolved")
    }, Array.Empty<UnsupportedItem>());

    private static PackageAnalysisSnapshot AnalysisSnapshot(PackageOverview package, PackageDependencyGraph graph) =>
        new(package, Array.Empty<ConnectionOverview>(), Array.Empty<VariableOverview>(),
            Array.Empty<ParameterOverview>(), Array.Empty<ExecutableOverview>(), Array.Empty<SqlStatementOverview>(),
            Array.Empty<DataFlowOverview>(), Array.Empty<ExpressionOverview>(),
            new ControlFlowGraph(Array.Empty<ControlFlowNode>(), Array.Empty<ControlFlowEdge>(), Array.Empty<UnsupportedItem>()),
            graph, new SemanticHandleCatalogBuilder().Build(package, Array.Empty<ConnectionOverview>(),
                Array.Empty<VariableOverview>(), Array.Empty<ParameterOverview>(), Array.Empty<ExecutableOverview>(),
                Array.Empty<DataFlowOverview>()), graph.UnsupportedItems);

    [Theory]
    [InlineData("1.0", true)]
    [InlineData("1.7", true)]
    [InlineData("2.0", false)]
    [InlineData("1.x", false)]
    [InlineData("1.0.1", false)]
    [InlineData("", false)]
    public void SchemaCompatibilityIsMajorVersionBased(string version, bool expected)
    {
        Assert.Equal(expected, CorpusSchema.IsCompatible(version));
    }

    private static PackageOverview Package() => new(Guid.NewGuid(), "Demo", "p", "", DateTime.UnixEpoch,
        1, 0, 0, "DontSaveSensitive", "Default", 0, 0, 0, 0, 0, false);
}
