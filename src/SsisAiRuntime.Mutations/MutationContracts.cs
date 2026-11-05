using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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

    public sealed class MutationRuntimeIdentity
    {
        public MutationRuntimeIdentity(int ssisMajorVersion, string architecture)
        {
            if (ssisMajorVersion <= 0) { throw new ArgumentOutOfRangeException(nameof(ssisMajorVersion)); }
            if (string.IsNullOrWhiteSpace(architecture)) { throw new ArgumentException("A runtime architecture is required.", nameof(architecture)); }
            SsisMajorVersion = ssisMajorVersion;
            Architecture = architecture;
        }

        public int SsisMajorVersion { get; }
        public string Architecture { get; }
    }

    public sealed class MutationAdapterIdentity
    {
        public MutationAdapterIdentity(string kind, string version)
        {
            if (string.IsNullOrWhiteSpace(kind)) { throw new ArgumentException("An adapter kind is required.", nameof(kind)); }
            if (string.IsNullOrWhiteSpace(version)) { throw new ArgumentException("An adapter version is required.", nameof(version)); }
            Kind = kind;
            Version = version;
        }

        public string Kind { get; }
        public string Version { get; }
    }

    public enum MutationOutcome
    {
        PreviewReady,
        AppliedAndVerified,
        AppliedWithWarnings,
        RejectedBeforeMutation,
        FailedAndRestored,
        FailedAndRestorationUnverified,
        FailedAndRestoreFailed
    }

    public enum MutationStageStatus
    {
        NotApplicable,
        Pending,
        Succeeded,
        Failed,
        Unverified
    }

    public enum MutationRollbackStatus
    {
        NotRequired,
        Restored,
        RestorationUnverified,
        RestoreFailed
    }

    public sealed class MutationStageResult
    {
        public MutationStageResult(MutationStageStatus status, IEnumerable<string> diagnosticCodes)
        {
            if (!Enum.IsDefined(typeof(MutationStageStatus), status))
            { throw new ArgumentOutOfRangeException(nameof(status)); }
            Status = status;
            DiagnosticCodes = MutationEnvelopeCollection.ImmutableStrings(diagnosticCodes, nameof(diagnosticCodes));
        }

        public MutationStageStatus Status { get; }
        public IReadOnlyList<string> DiagnosticCodes { get; }
    }

    public sealed class MutationArtifactResult
    {
        public MutationArtifactResult(MutationStageStatus status, string sourceArtifactHash,
            string destinationArtifactHash, string checkpointId)
        {
            if (!Enum.IsDefined(typeof(MutationStageStatus), status))
            { throw new ArgumentOutOfRangeException(nameof(status)); }
            Status = status;
            SourceArtifactHash = sourceArtifactHash ?? string.Empty;
            DestinationArtifactHash = destinationArtifactHash ?? string.Empty;
            CheckpointId = checkpointId ?? string.Empty;
        }

        public MutationStageStatus Status { get; }
        public string SourceArtifactHash { get; }
        public string DestinationArtifactHash { get; }
        public string CheckpointId { get; }
    }

    public sealed class MutationRollbackResult
    {
        public MutationRollbackResult(MutationRollbackStatus status, bool? sourceRestored)
        {
            if (!Enum.IsDefined(typeof(MutationRollbackStatus), status))
            { throw new ArgumentOutOfRangeException(nameof(status)); }
            Status = status;
            SourceRestored = sourceRestored;
        }

        public MutationRollbackStatus Status { get; }
        public bool? SourceRestored { get; }
    }

    public sealed class MutationOperationResult<TMutation>
    {
        public MutationOperationResult(MutationOutcome outcome, string diagnosticCode, TMutation mutation)
        {
            if (!Enum.IsDefined(typeof(MutationOutcome), outcome))
            { throw new ArgumentOutOfRangeException(nameof(outcome)); }
            Outcome = outcome;
            DiagnosticCode = diagnosticCode ?? string.Empty;
            Mutation = mutation;
        }

        public MutationOutcome Outcome { get; }
        public string DiagnosticCode { get; }
        public TMutation Mutation { get; }
    }

    public sealed class MutationEnvelopeResult<TMutation>
    {
        public MutationEnvelopeResult(MutationRuntimeIdentity runtime, MutationAdapterIdentity adapter,
            MutationOperationResult<TMutation> mutation, MutationArtifactResult artifact,
            MutationStageResult reload, MutationStageResult validation,
            MutationStageResult semanticVerification, MutationStageResult layoutVerification,
            MutationRollbackResult rollback)
        {
            Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            Adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            Mutation = mutation ?? throw new ArgumentNullException(nameof(mutation));
            Artifact = artifact ?? throw new ArgumentNullException(nameof(artifact));
            Reload = reload ?? throw new ArgumentNullException(nameof(reload));
            Validation = validation ?? throw new ArgumentNullException(nameof(validation));
            SemanticVerification = semanticVerification ?? throw new ArgumentNullException(nameof(semanticVerification));
            LayoutVerification = layoutVerification ?? throw new ArgumentNullException(nameof(layoutVerification));
            Rollback = rollback ?? throw new ArgumentNullException(nameof(rollback));
        }

        public MutationRuntimeIdentity Runtime { get; }
        public MutationAdapterIdentity Adapter { get; }
        public MutationOperationResult<TMutation> Mutation { get; }
        public MutationArtifactResult Artifact { get; }
        public MutationStageResult Reload { get; }
        public MutationStageResult Validation { get; }
        public MutationStageResult SemanticVerification { get; }
        public MutationStageResult LayoutVerification { get; }
        public MutationRollbackResult Rollback { get; }
    }

    public sealed class MutationRedactionStatus
    {
        public MutationRedactionStatus(string policy, bool requestValuesOmitted,
            bool expressionTextOmitted, bool secretInputsOmitted, bool packagePathOmitted = true)
        {
            if (string.IsNullOrWhiteSpace(policy)) { throw new ArgumentException("A redaction policy is required.", nameof(policy)); }
            Policy = policy;
            RequestValuesOmitted = requestValuesOmitted;
            ExpressionTextOmitted = expressionTextOmitted;
            SecretInputsOmitted = secretInputsOmitted;
            PackagePathOmitted = packagePathOmitted;
        }

        public string Policy { get; }
        public bool RequestValuesOmitted { get; }
        public bool ExpressionTextOmitted { get; }
        public bool SecretInputsOmitted { get; }
        public bool PackagePathOmitted { get; }
    }

    public sealed class MutationEnvelope<TTarget, TRequest, TPreview, TMutation>
    {
        public MutationEnvelope(string operation, TTarget target, TRequest request,
            string sourceArtifactHash, MutationExecutionRequirements requirements,
            TPreview preview, MutationEnvelopeResult<TMutation> result,
            IEnumerable<string> diagnostics, IEnumerable<string> coverage,
            MutationRedactionStatus redaction)
        {
            if (string.IsNullOrWhiteSpace(operation)) { throw new ArgumentException("A mutation operation is required.", nameof(operation)); }
            if (ReferenceEquals(target, null) && requirements != null && requirements.RequiresExistingSource)
            { throw new ArgumentNullException(nameof(target)); }
            if (ReferenceEquals(request, null)) { throw new ArgumentNullException(nameof(request)); }
            Requirements = requirements ?? throw new ArgumentNullException(nameof(requirements));
            var sourceHash = sourceArtifactHash ?? string.Empty;
            if (Requirements.RequiresExistingSource && !IsSha256(sourceHash))
            { throw new ArgumentException("Existing-artifact mutations require a source SHA-256 hash.", nameof(sourceArtifactHash)); }
            if (sourceHash.Length != 0 && !IsSha256(sourceHash))
            { throw new ArgumentException("A source artifact hash must be SHA-256.", nameof(sourceArtifactHash)); }
            Operation = operation;
            Target = target;
            Request = request;
            SourceArtifactHash = sourceHash;
            Preview = preview;
            Result = result;
            Diagnostics = MutationEnvelopeCollection.ImmutableStrings(diagnostics, nameof(diagnostics));
            Coverage = MutationEnvelopeCollection.ImmutableStrings(coverage, nameof(coverage));
            Redaction = redaction ?? throw new ArgumentNullException(nameof(redaction));
        }

        public string SchemaVersion => MutationContractSchema.CurrentVersion;
        public string Operation { get; }
        public TTarget Target { get; }
        public TRequest Request { get; }
        public string SourceArtifactHash { get; }
        public MutationExecutionRequirements Requirements { get; }
        public TPreview Preview { get; }
        public MutationEnvelopeResult<TMutation> Result { get; }
        public IReadOnlyList<string> Diagnostics { get; }
        public IReadOnlyList<string> Coverage { get; }
        public MutationRedactionStatus Redaction { get; }

        private static bool IsSha256(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);
    }

    internal static class MutationEnvelopeCollection
    {
        public static IReadOnlyList<string> ImmutableStrings(IEnumerable<string> values, string parameterName)
        {
            if (values == null) { throw new ArgumentNullException(parameterName); }
            return new ReadOnlyCollection<string>(values.Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray());
        }
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