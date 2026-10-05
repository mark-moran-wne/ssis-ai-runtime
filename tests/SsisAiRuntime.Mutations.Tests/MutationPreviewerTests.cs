using SsisAiRuntime.AI;
using SsisAiRuntime.Inspectors;
using SsisAiRuntime.Mutations;
using Newtonsoft.Json.Linq;
using System.Reflection;

namespace SsisAiRuntime.Mutations.Tests;

public sealed class MutationPreviewerTests
{
    [Fact]
    public void RenamePreviewRequiresExecutableKindAndNativeId()
    {
        var snapshot = Snapshot();
        var previewer = new MutationPreviewer();

        var valid = previewer.Preview(snapshot, Rename(SemanticObjectKind.Executable, "task-1", "Renamed"));
        var wrongKind = previewer.Preview(snapshot, Rename(SemanticObjectKind.Connection, "task-1", "Renamed"));
        var missing = previewer.Preview(snapshot, Rename(SemanticObjectKind.Executable, "missing", "Renamed"));

        Assert.Equal(MutationPreviewStatus.Valid, valid.Status);
        Assert.Equal("Load", valid.CurrentName);
        Assert.NotNull(valid.ExecutionPlan);
        Assert.Equal(MutationPreviewStatus.UnsupportedTarget, wrongKind.Status);
        Assert.Equal(MutationPreviewStatus.TargetNotFound, missing.Status);
        Assert.Throws<ArgumentException>(() => new MutationTarget(SemanticObjectKind.Executable, " "));
    }

    [Fact]
    public void RenameImpactIncludesActualDirectAndIndirectConsumers()
    {
        var preview = new MutationPreviewer().Preview(Snapshot(),
            Rename(SemanticObjectKind.Executable, "task-1", "Renamed"));

        Assert.Equal(MutationPreviewStatus.Valid, preview.Status);
        Assert.Equal(new[] { "Executable:task-2", "Executable:task-3" }, preview.Impact.AffectedNodeKeys);
        Assert.DoesNotContain("Executable:unrelated", preview.Impact.AffectedNodeKeys);
        Assert.Equal(MutationRisk.Low, preview.Impact.Risk);
    }

    [Fact]
    public void IncompleteDependencyCoveragePreventsExecutionPlan()
    {
        var snapshot = Snapshot(new[]
        {
            new UnsupportedItem("task-2", "LoadConsumer", "Task", "coverage", "expression.reference_not_found")
        });

        var preview = new MutationPreviewer().Preview(snapshot,
            Rename(SemanticObjectKind.Executable, "task-1", "Renamed"));

        Assert.Equal(MutationPreviewStatus.IncompleteCoverage, preview.Status);
        Assert.False(preview.IsValid);
        Assert.Null(preview.ExecutionPlan);
        Assert.Contains(preview.CoverageGaps, gap => gap.ReasonCode == "expression.reference_not_found");
    }

    [Fact]
    public void SnapshotCoverageAlsoPreventsExecutionPlan()
    {
        var gap = new UnsupportedItem("task-1", "Load", "Task", "coverage", "sql.parse_failed");
        var snapshot = Snapshot(snapshotOnlyCoverage: new[] { gap });

        var preview = new MutationPreviewer().Preview(snapshot,
            Rename(SemanticObjectKind.Executable, "task-1", "Renamed"));

        Assert.Equal(MutationPreviewStatus.IncompleteCoverage, preview.Status);
        Assert.Contains(preview.CoverageGaps, item => item.ReasonCode == "sql.parse_failed");
    }

    [Fact]
    public void RenamePreviewDoesNotAcceptContainerAsTask()
    {
        var snapshot = Snapshot(containerTask: true);

        var preview = new MutationPreviewer().Preview(snapshot,
            Rename(SemanticObjectKind.Executable, "task-1", "Renamed"));

        Assert.Equal(MutationPreviewStatus.UnsupportedTarget, preview.Status);
    }

    [Fact]
    public void DuplicateNamesResolveOnlyByNativeId()
    {
        var snapshot = Snapshot(duplicateTaskName: true);

        var preview = new MutationPreviewer().Preview(snapshot,
            Rename(SemanticObjectKind.Executable, "task-1", "Renamed"));

        Assert.Equal(MutationPreviewStatus.Valid, preview.Status);
        Assert.Equal("Load", preview.CurrentName);
        Assert.Equal("task-1", preview.ExecutionPlan.Request.Target.NativeId);
    }

    [Fact]
    public void MismatchedNativeIdDoesNotFallBackToMatchingName()
    {
        var snapshot = Snapshot(duplicateTaskName: true);

        var preview = new MutationPreviewer().Preview(snapshot,
            Rename(SemanticObjectKind.Executable, "not-task-1", "Renamed"));

        Assert.Equal(MutationPreviewStatus.TargetNotFound, preview.Status);
    }

    [Fact]
    public void PreviewIsReadOnly()
    {
        var snapshot = Snapshot();

        var preview = new MutationPreviewer().Preview(snapshot,
            Rename(SemanticObjectKind.Executable, "task-1", "Renamed"));

        Assert.Equal(MutationPreviewStatus.Valid, preview.Status);
        Assert.Equal("Load", Assert.Single(snapshot.Executables, item => item.Id == "task-1").Name);
    }

    [Fact]
    public void DurableContractsAreImmutableDeterministicAndVersioned()
    {
        var preview = new MutationPreviewer().Preview(Snapshot(),
            Rename(SemanticObjectKind.Executable, "task-1", "Renamed"));
        var request = preview.Request;
        var plan = preview.ExecutionPlan;
        var result = new MutationResult(MutationExecutionStatus.PreviewOnly, "mutation.preview_only");
        var contracts = new (Type Type, string Json, string Name)[]
        {
            (typeof(MutationRequest), MutationContractSerializer.Serialize(request), "MutationRequest"),
            (typeof(MutationPreview), MutationContractSerializer.Serialize(preview), "MutationPreview"),
            (typeof(MutationExecutionPlan), MutationContractSerializer.Serialize(plan), "MutationExecutionPlan"),
            (typeof(MutationResult), MutationContractSerializer.Serialize(result), "MutationResult")
        };

        foreach (var contract in contracts)
        {
            var serializedAgain = contract.Type == typeof(MutationRequest)
                ? MutationContractSerializer.Serialize(request)
                : contract.Type == typeof(MutationPreview)
                    ? MutationContractSerializer.Serialize(preview)
                    : contract.Type == typeof(MutationExecutionPlan)
                        ? MutationContractSerializer.Serialize(plan)
                        : MutationContractSerializer.Serialize(result);
            var document = JObject.Parse(contract.Json);
            Assert.Equal(contract.Json, serializedAgain);
            Assert.Equal(MutationContractSchema.CurrentVersion, (string)document["schemaVersion"]!);
            Assert.Equal(contract.Name, (string)document["contractType"]!);
            Assert.All(contract.Type.GetProperties(BindingFlags.Instance | BindingFlags.Public), property =>
                Assert.Null(property.SetMethod?.IsPublic == true ? property.SetMethod : null));
        }

        Assert.True(MutationContractSchema.IsCompatible("1.4"));
        Assert.False(MutationContractSchema.IsCompatible("2.0"));
        Assert.Equal("PreviewOnly", (string)JObject.Parse(contracts[3].Json)["payload"]!["status"]!);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<string>)preview.Impact.AffectedNodeKeys).Add("Executable:mutated"));
        Assert.Throws<NotSupportedException>(() =>
            ((IList<UnsupportedItem>)preview.CoverageGaps).Add(
                new UnsupportedItem("id", "name", "task", "coverage")));
    }

    [Fact]
    public void InvalidNameAndAmbiguousNativeIdentityAreRejected()
    {
        var snapshot = Snapshot(duplicateTaskId: true);
        var previewer = new MutationPreviewer();

        var invalidName = previewer.Preview(Snapshot(),
            Rename(SemanticObjectKind.Executable, "task-1", "  "));
        var ambiguous = previewer.Preview(snapshot,
            Rename(SemanticObjectKind.Executable, "task-1", "Renamed"));

        Assert.Equal(MutationPreviewStatus.InvalidValue, invalidName.Status);
        Assert.Equal(MutationPreviewStatus.AmbiguousTarget, ambiguous.Status);
        Assert.Throws<ArgumentOutOfRangeException>(() => new MutationRequest((MutationKind)99,
            new MutationTarget(SemanticObjectKind.Executable, "task-1"), "Renamed"));
    }

    private static MutationRequest Rename(SemanticObjectKind kind, string nativeId, string proposedName) =>
        new(MutationKind.RenameTask, new MutationTarget(kind, nativeId), proposedName);

    private static PackageAnalysisSnapshot Snapshot(IEnumerable<UnsupportedItem>? unsupported = null,
        bool duplicateTaskId = false, IEnumerable<UnsupportedItem>? snapshotOnlyCoverage = null,
        bool containerTask = false, bool duplicateTaskName = false)
    {
        var sessionId = Guid.NewGuid();
        var package = new PackageOverview(sessionId, "Demo", "package-id", "", DateTime.UnixEpoch,
            1, 0, 0, "DontSaveSensitive", "Default", 0, 0, 4, 0, 0, false);
        var executables = new[]
        {
            new ExecutableOverview("task-1", "", "Load", "STOCK:Task", "", 0, containerTask, false),
            new ExecutableOverview("task-2", "", "Consumer", "STOCK:Task", "", 0, false, false),
            new ExecutableOverview("task-3", "", "Transitive", "STOCK:Task", "", 0, false, false),
            new ExecutableOverview("unrelated", "", duplicateTaskName ? "Load" : "Unrelated", "STOCK:Task", "", 0, false, false)
        };
        if (duplicateTaskId)
        {
            executables = executables.Append(
                new ExecutableOverview("task-1", "", "Duplicate", "STOCK:Task", "", 0, false, false)).ToArray();
        }

        var nodes = executables.Select(item => new DependencyNode(
            "Executable:" + item.Id, SemanticObjectKind.Executable, item.Name, item.Id, item.ParentId)).ToArray();
        var edges = new[]
        {
            new DependencyEdge("Executable:task-2", "Executable:task-1", DependencyKind.DependsOnTask),
            new DependencyEdge("Executable:task-3", "Executable:task-2", DependencyKind.DependsOnTask)
        };
        var gaps = unsupported ?? Array.Empty<UnsupportedItem>();
        var dependencies = new PackageDependencyGraph(nodes, edges, gaps);
        var catalog = new SemanticHandleCatalogBuilder().Build(package, Array.Empty<ConnectionOverview>(),
            Array.Empty<VariableOverview>(), Array.Empty<ParameterOverview>(), executables, Array.Empty<DataFlowOverview>());
        var controlFlow = new ControlFlowGraph(Array.Empty<ControlFlowNode>(), Array.Empty<ControlFlowEdge>(),
            Array.Empty<UnsupportedItem>());
        return new PackageAnalysisSnapshot(package, Array.Empty<ConnectionOverview>(),
            Array.Empty<VariableOverview>(), Array.Empty<ParameterOverview>(), executables,
            Array.Empty<SqlStatementOverview>(), Array.Empty<DataFlowOverview>(), Array.Empty<ExpressionOverview>(),
            controlFlow, dependencies, catalog, gaps.Concat(snapshotOnlyCoverage ?? Array.Empty<UnsupportedItem>()));
    }
}