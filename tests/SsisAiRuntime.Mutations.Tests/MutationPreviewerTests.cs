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
    public void KnownTaskPropertyOmissionsDoNotBlockRenameAndRemainVisible()
    {
        var gap = new UnsupportedItem("task-1", "Load", "STOCK:Task", "Task properties omitted",
            UnsupportedItem.TaskPropertiesNotInspectedCode);
        var snapshot = Snapshot(new[] { gap });
        var preview = new MutationPreviewer().Preview(snapshot,
            Rename(SemanticObjectKind.Executable, "task-1", "Renamed"));

        Assert.True(preview.IsValid);
        Assert.False(snapshot.IsComplete);
        Assert.Contains(gap, preview.CoverageGaps);
        var unknownOwner = Snapshot(new[] { new UnsupportedItem("missing", "Load", "STOCK:Task", "omitted",
            UnsupportedItem.TaskPropertiesNotInspectedCode) });
        Assert.False(RenameCoveragePolicy.CanVerifyRename(unknownOwner));
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
        var envelope = CreateEnvelope(preview, new string('a', 64));
        var json = MutationContractSerializer.Serialize(envelope);
        var document = JObject.Parse(json);

        Assert.Equal(json, MutationContractSerializer.Serialize(envelope));
        Assert.Equal("2.0", (string)document["schemaVersion"]!);
        Assert.Equal("rename_task", (string)document["operation"]!);
        Assert.Equal("Executable", (string)document["target"]!["kind"]!);
        Assert.Equal("task-1", (string)document["target"]!["nativeId"]!);
        Assert.Equal(new string('a', 64), (string)document["sourceArtifactHash"]!);
        Assert.Equal(16, (int)document["result"]!["runtime"]!["ssisMajorVersion"]!);
        Assert.Equal("TaskHostRename", (string)document["result"]!["adapter"]!["kind"]!);
        Assert.Equal("AppliedAndVerified", (string)document["result"]!["mutation"]!["outcome"]!);
        Assert.Equal("Succeeded", (string)document["result"]!["validation"]!["status"]!);
        Assert.True(MutationContractSchema.IsCompatible("2.0"));
        Assert.False(MutationContractSchema.IsCompatible("1.0"));
        Assert.False(MutationContractSchema.IsCompatible("1.1"));
        Assert.All(typeof(MutationEnvelope<MutationTarget, MutationRequest, MutationPreview, string>)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public), property =>
                Assert.Null(property.SetMethod?.IsPublic == true ? property.SetMethod : null));
        Assert.Throws<NotSupportedException>(() =>
            ((IList<string>)envelope.Diagnostics).Add("mutation.extra"));
        Assert.Throws<NotSupportedException>(() =>
            ((IList<string>)preview.Impact.AffectedNodeKeys).Add("Executable:mutated"));
        Assert.Throws<NotSupportedException>(() =>
            ((IList<UnsupportedItem>)preview.CoverageGaps).Add(
                new UnsupportedItem("id", "name", "task", "coverage")));

        var creationRequirements = new MutationExecutionRequirements(false, false, false, false, false,
            Array.Empty<string>(), requiresExistingSource: false);
        var creation = new MutationEnvelope<string, string, string, string>(
            "create_package", null!, "bootstrap", null!, creationRequirements, null!, null!,
            Array.Empty<string>(), Array.Empty<string>(),
            new MutationRedactionStatus("mutation", true, true, true));
        Assert.Equal(string.Empty, (string)JObject.Parse(MutationContractSerializer.Serialize(creation))["sourceArtifactHash"]!);
        Assert.Throws<ArgumentException>(() => new MutationEnvelope<MutationTarget, string, string, string>(
            "rename_task", preview.Request.Target, "Renamed", null!,
            preview.ExecutionPlan.Requirements, null!, null!, Array.Empty<string>(), Array.Empty<string>(),
            new MutationRedactionStatus("mutation", true, true, true)));
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

    [Fact]
    public void RenamePlanSerializesAllLifecycleRequirements()
    {
        var preview = new MutationPreviewer().Preview(Snapshot(),
            Rename(SemanticObjectKind.Executable, "task-1", "Renamed"));
        var plan = preview.ExecutionPlan;
        var requirements = plan.Requirements;

        Assert.True(requirements.RequiresLifecycle);
        Assert.True(requirements.CheckpointRequired);
        Assert.True(requirements.SaveAsRequired);
        Assert.True(requirements.ReloadRequired);
        Assert.True(requirements.ValidationRequired);
        Assert.True(requirements.SemanticDiffRequired);
        Assert.Equal(new[]
        {
            "checkpoint.required", "reload.required", "saveas.required",
            "semanticdiff.required", "validation.required"
        }, requirements.Requirements);

        var document = JObject.Parse(MutationContractSerializer.Serialize(
            CreateEnvelope(preview, new string('b', 64))));
        Assert.Equal("2.0", (string)document["schemaVersion"]!);
        var serialized = (JObject)document["requirements"]!;
        Assert.Equal(7, serialized.Count);
        foreach (var flag in new[]
        {
            "checkpointRequired", "saveAsRequired", "reloadRequired",
            "validationRequired", "semanticDiffRequired"
        })
        {
            Assert.True((bool)serialized[flag]!);
        }
            Assert.True((bool)serialized["requiresExistingSource"]!);
        Assert.Equal(requirements.Requirements, serialized["requirements"]!.Values<string>());
        Assert.Throws<ArgumentNullException>(() => new MutationExecutionPlan(
            plan.Request, plan.ExpectedCurrentName, plan.ProposedName, plan.Impact, null!));
    }

    [Fact]
    public void RequirementsAreCopiedDeduplicatedSortedAndImmutable()
    {
        var preview = new MutationPreviewer().Preview(Snapshot(),
            Rename(SemanticObjectKind.Executable, "task-1", "Renamed"));
        var source = new List<string> { "validation.required", "checkpoint.required", "validation.required" };
        var requirements = new MutationExecutionRequirements(false, false, false, false, false, source);
        source.Clear();

        Assert.False(requirements.RequiresLifecycle);
        Assert.Equal(new[] { "checkpoint.required", "validation.required" }, requirements.Requirements);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)requirements.Requirements).Clear());
        Assert.All(typeof(MutationExecutionRequirements).GetProperties(), property => Assert.Null(property.SetMethod));
        Assert.Throws<ArgumentNullException>(() =>
            new MutationExecutionRequirements(false, false, false, false, false, null!));

        var reordered = new MutationExecutionRequirements(false, false, false, false, false,
            new[] { "checkpoint.required", "validation.required" });
        Assert.Equal(
            MutationContractSerializer.Serialize(CreateEnvelope(preview, new string('d', 64), requirements)),
            MutationContractSerializer.Serialize(CreateEnvelope(preview, new string('d', 64), reordered)));
    }

    private static MutationEnvelope<MutationTarget, MutationRequest, MutationPreview, string> CreateEnvelope(
        MutationPreview preview, string sourceArtifactHash, MutationExecutionRequirements? requirements = null)
    {
        var success = new MutationStageResult(MutationStageStatus.Succeeded, Array.Empty<string>());
        var result = new MutationEnvelopeResult<string>(
            new MutationRuntimeIdentity(16, "x64"), new MutationAdapterIdentity("TaskHostRename", "1"),
            new MutationOperationResult<string>(MutationOutcome.AppliedAndVerified, "mutation.completed", "Renamed"),
            new MutationArtifactResult(MutationStageStatus.Succeeded, sourceArtifactHash, new string('c', 64), "checkpoint-1"),
            success, success, success, new MutationStageResult(MutationStageStatus.NotApplicable, Array.Empty<string>()),
            new MutationRollbackResult(MutationRollbackStatus.NotRequired, null));
        return new MutationEnvelope<MutationTarget, MutationRequest, MutationPreview, string>(
            "rename_task", preview.Request.Target, preview.Request, sourceArtifactHash,
            requirements ?? preview.ExecutionPlan.Requirements, preview, result, Array.Empty<string>(), Array.Empty<string>(),
            new MutationRedactionStatus("mutation", true, true, true));
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