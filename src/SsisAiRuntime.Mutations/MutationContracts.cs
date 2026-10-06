using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using SsisAiRuntime.AI;
using SsisAiRuntime.Inspectors;

namespace SsisAiRuntime.Mutations
{
    public enum MutationKind
    {
        RenameTask
    }

    public enum MutationRisk
    {
        Low,
        Medium,
        High
    }

    public enum MutationPreviewStatus
    {
        Valid,
        TargetNotFound,
        AmbiguousTarget,
        UnsupportedTarget,
        InvalidValue,
        IncompleteCoverage
    }

    public enum MutationExecutionStatus
    {
        PreviewOnly,
        CheckpointRequired,
        ValidationFailed,
        SaveRequired,
        ReloadRequired,
        Completed
    }

    public sealed class MutationTarget
    {
        public MutationTarget(SemanticObjectKind kind, string nativeId)
        {
            if (!Enum.IsDefined(typeof(SemanticObjectKind), kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }
            if (string.IsNullOrWhiteSpace(nativeId))
            {
                throw new ArgumentException("A native target ID is required.", nameof(nativeId));
            }

            Kind = kind;
            NativeId = nativeId;
        }

        public SemanticObjectKind Kind { get; }
        public string NativeId { get; }
    }

    public sealed class MutationRequest
    {
        public MutationRequest(MutationKind kind, MutationTarget target, string proposedName)
        {
            if (!Enum.IsDefined(typeof(MutationKind), kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }

            Kind = kind;
            Target = target ?? throw new ArgumentNullException(nameof(target));
            ProposedName = proposedName ?? string.Empty;
        }

        public MutationKind Kind { get; }
        public MutationTarget Target { get; }
        public string ProposedName { get; }
    }

    public sealed class MutationImpact
    {
        public MutationImpact(MutationRisk risk, string summary, IEnumerable<string> affectedNodeKeys)
        {
            if (!Enum.IsDefined(typeof(MutationRisk), risk))
            {
                throw new ArgumentOutOfRangeException(nameof(risk));
            }
            Risk = risk;
            Summary = summary ?? string.Empty;
            AffectedNodeKeys = ReadOnly(affectedNodeKeys, nameof(affectedNodeKeys));
        }

        public MutationRisk Risk { get; }
        public string Summary { get; }
        public IReadOnlyList<string> AffectedNodeKeys { get; }

        private static IReadOnlyList<T> ReadOnly<T>(IEnumerable<T> values, string name)
        {
            if (values == null) { throw new ArgumentNullException(name); }
            return new ReadOnlyCollection<T>(new List<T>(values));
        }
    }

    public sealed class MutationExecutionPlan
    {
        public MutationExecutionPlan(MutationRequest request, string expectedCurrentName,
            string proposedName, MutationImpact impact, MutationExecutionRequirements requirements)
        {
            Request = request ?? throw new ArgumentNullException(nameof(request));
            ExpectedCurrentName = expectedCurrentName ?? string.Empty;
            ProposedName = proposedName ?? string.Empty;
            Impact = impact ?? throw new ArgumentNullException(nameof(impact));
            Requirements = requirements ?? throw new ArgumentNullException(nameof(requirements));
        }

        public MutationRequest Request { get; }
        public string ExpectedCurrentName { get; }
        public string ProposedName { get; }
        public MutationImpact Impact { get; }
        public MutationExecutionRequirements Requirements { get; }
    }

    public sealed class MutationPreview
    {
        public MutationPreview(MutationPreviewStatus status, MutationRequest request,
            string currentName, string proposedName, MutationImpact impact,
            MutationExecutionPlan executionPlan, IEnumerable<UnsupportedItem> coverageGaps)
        {
            if (!Enum.IsDefined(typeof(MutationPreviewStatus), status))
            {
                throw new ArgumentOutOfRangeException(nameof(status));
            }
            Status = status;
            Request = request ?? throw new ArgumentNullException(nameof(request));
            CurrentName = currentName ?? string.Empty;
            ProposedName = proposedName ?? string.Empty;
            Impact = impact;
            ExecutionPlan = executionPlan;
            CoverageGaps = ReadOnly(coverageGaps, nameof(coverageGaps));
            if ((status == MutationPreviewStatus.Valid) != (executionPlan != null) ||
                (status == MutationPreviewStatus.Valid && impact == null))
            {
                throw new ArgumentException("A valid preview requires both impact and an execution plan.", nameof(executionPlan));
            }
        }

        public MutationPreviewStatus Status { get; }
        public MutationRequest Request { get; }
        public string CurrentName { get; }
        public string ProposedName { get; }
        public MutationImpact Impact { get; }
        public MutationExecutionPlan ExecutionPlan { get; }
        public IReadOnlyList<UnsupportedItem> CoverageGaps { get; }
        public bool IsValid => Status == MutationPreviewStatus.Valid;

        private static IReadOnlyList<T> ReadOnly<T>(IEnumerable<T> values, string name)
        {
            if (values == null) { throw new ArgumentNullException(name); }
            return new ReadOnlyCollection<T>(new List<T>(values));
        }
    }

    public sealed class MutationExecutionRequest
    {
        public MutationExecutionRequest(MutationExecutionPlan plan)
        {
            Plan = plan ?? throw new ArgumentNullException(nameof(plan));
        }

        public MutationExecutionPlan Plan { get; }
    }

    public sealed class MutationResult
    {
        public MutationResult(MutationExecutionStatus status, string code)
        {
            if (!Enum.IsDefined(typeof(MutationExecutionStatus), status))
            {
                throw new ArgumentOutOfRangeException(nameof(status));
            }

            Status = status;
            Code = code ?? string.Empty;
        }

        public MutationExecutionStatus Status { get; }
        public string Code { get; }
    }

    public interface IMutationPreviewer
    {
        MutationPreview Preview(PackageAnalysisSnapshot snapshot, MutationRequest request);
    }

    public interface IMutationExecutor
    {
        Task<MutationResult> ExecuteAsync(
            MutationExecutionRequest request,
            CancellationToken cancellationToken);
    }
}