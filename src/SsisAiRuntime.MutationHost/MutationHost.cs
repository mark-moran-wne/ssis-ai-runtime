using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SqlServer.Dts.Runtime;
using SsisAiRuntime.AI;
using SsisAiRuntime.Core;
using SsisAiRuntime.Corpus;
using SsisAiRuntime.Inspectors;
using SsisAiRuntime.Mutations;
using SsisAiRuntime.Ssis16;

namespace SsisAiRuntime.MutationHost
{
    public interface IMutationCheckpointService
    {
        Task<MutationCheckpointResult> CreateAsync(
            MutationCheckpointRequest request,
            CancellationToken cancellationToken);
    }

    public sealed class MutationCheckpointRequest
    {
        public MutationCheckpointRequest(
            string sourcePackagePath,
            string sourceArtifactHash,
            CorpusFingerprint sourceFingerprint,
            string operationId,
            string reasonCode)
        {
            if (string.IsNullOrWhiteSpace(sourcePackagePath))
            {
                throw new ArgumentException(
                    "A source package path is required.",
                    nameof(sourcePackagePath));
            }

            if (!MutationHash.IsSha256(sourceArtifactHash))
            {
                throw new ArgumentException(
                    "A SHA-256 source artifact hash is required.",
                    nameof(sourceArtifactHash));
            }

            if (string.IsNullOrWhiteSpace(operationId))
            {
                throw new ArgumentException(
                    "An operation ID is required.",
                    nameof(operationId));
            }

            SourcePackagePath = Path.GetFullPath(sourcePackagePath);
            SourceArtifactHash = sourceArtifactHash;
            SourceFingerprint = sourceFingerprint ??
                throw new ArgumentNullException(nameof(sourceFingerprint));
            OperationId = operationId;
            ReasonCode = reasonCode ?? string.Empty;
        }

        public string SourcePackagePath { get; }

        public string SourceArtifactHash { get; }

        public CorpusFingerprint SourceFingerprint { get; }

        public string OperationId { get; }

        public string ReasonCode { get; }
    }

    public sealed class MutationCheckpointResult
    {
        public MutationCheckpointResult(
            bool succeeded,
            string checkpointId,
            string artifactHash,
            string code)
        {
            Succeeded = succeeded;
            CheckpointId = checkpointId ?? string.Empty;
            ArtifactHash = artifactHash ?? string.Empty;
            Code = code ?? string.Empty;

            if (Succeeded)
            {
                if (CheckpointId.Length == 0)
                {
                    throw new ArgumentException(
                        "A successful checkpoint requires an ID.",
                        nameof(checkpointId));
                }

                if (!MutationHash.IsSha256(ArtifactHash))
                {
                    throw new ArgumentException(
                        "A successful checkpoint requires an artifact hash.",
                        nameof(artifactHash));
                }
            }
        }

        public bool Succeeded { get; }

        public string CheckpointId { get; }

        public string ArtifactHash { get; }

        public string Code { get; }
    }

    public interface INativeMutationValidator
    {
        NativeMutationValidationResult Validate(
            PackageSession<Package> session);
    }

    public sealed class NativeMutationValidationResult
    {
        public NativeMutationValidationResult(
            bool succeeded,
            bool hasWarnings,
            IEnumerable<string> diagnosticCodes)
        {
            Succeeded = succeeded;
            HasWarnings = hasWarnings;

            DiagnosticCodes = new ReadOnlyCollection<string>(
                (diagnosticCodes ??
                 throw new ArgumentNullException(nameof(diagnosticCodes)))
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(code => code, StringComparer.Ordinal)
                .ToArray());
        }

        public bool Succeeded { get; }

        public bool HasWarnings { get; }

        public IReadOnlyList<string> DiagnosticCodes { get; }
    }

    public sealed class MutationHostRequest
    {
        public MutationHostRequest(
            string sourcePackagePath,
            string destinationPackagePath,
            MutationExecutionPlan plan,
            string expectedSourceHash)
            : this(sourcePackagePath, destinationPackagePath, expectedSourceHash)
        {
            Plan = plan ?? throw new ArgumentNullException(nameof(plan));
        }

        public MutationHostRequest(string sourcePackagePath, string destinationPackagePath,
            ColumnResizePlan plan, string expectedSourceHash)
            : this(sourcePackagePath, destinationPackagePath, expectedSourceHash)
        {
            ResizePlan = plan ?? throw new ArgumentNullException(nameof(plan));
            if (plan.SourceArtifactHash != expectedSourceHash)
            { throw new ArgumentException("The resize plan must bind the expected source artifact.", nameof(expectedSourceHash)); }
        }

        private MutationHostRequest(string sourcePackagePath, string destinationPackagePath, string expectedSourceHash)
        {
            if (string.IsNullOrWhiteSpace(sourcePackagePath))
            {
                throw new ArgumentException(
                    "A source package path is required.",
                    nameof(sourcePackagePath));
            }

            if (string.IsNullOrWhiteSpace(destinationPackagePath))
            {
                throw new ArgumentException(
                    "A destination package path is required.",
                    nameof(destinationPackagePath));
            }

            if (!MutationHash.IsSha256(expectedSourceHash))
            {
                throw new ArgumentException(
                    "An expected source SHA-256 hash is required.",
                    nameof(expectedSourceHash));
            }

            SourcePackagePath = Path.GetFullPath(sourcePackagePath);
            DestinationPackagePath =
                Path.GetFullPath(destinationPackagePath);

            ExpectedSourceHash = expectedSourceHash;
        }

        public string SourcePackagePath { get; }

        public string DestinationPackagePath { get; }

        public MutationExecutionPlan Plan { get; }
        public ColumnResizePlan ResizePlan { get; }

        public string ExpectedSourceHash { get; }
    }

    public sealed class MutationHostResult
    {
        public MutationHostResult(
            MutationExecutionStatus status,
            string code,
            string operationId,
            string checkpointId,
            string sourceArtifactHash,
            string destinationArtifactHash,
            CorpusFingerprint beforeFingerprint,
            CorpusFingerprint afterFingerprint,
            bool checkpointCreated,
            bool saveAsCompleted,
            bool reloadSucceeded,
            bool validationSucceeded,
            bool semanticDiffMatched,
            IEnumerable<string> diagnosticCodes)
        {
            if (!Enum.IsDefined(
                typeof(MutationExecutionStatus),
                status))
            {
                throw new ArgumentOutOfRangeException(nameof(status));
            }

            Status = status;
            Code = code ?? string.Empty;
            OperationId = operationId ?? string.Empty;
            CheckpointId = checkpointId ?? string.Empty;
            SourceArtifactHash = sourceArtifactHash ?? string.Empty;
            DestinationArtifactHash =
                destinationArtifactHash ?? string.Empty;
            BeforeFingerprint = beforeFingerprint;
            AfterFingerprint = afterFingerprint;
            CheckpointCreated = checkpointCreated;
            SaveAsCompleted = saveAsCompleted;
            ReloadSucceeded = reloadSucceeded;
            ValidationSucceeded = validationSucceeded;
            SemanticDiffMatched = semanticDiffMatched;

            DiagnosticCodes = new ReadOnlyCollection<string>(
                (diagnosticCodes ??
                 throw new ArgumentNullException(nameof(diagnosticCodes)))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray());
        }

        public MutationExecutionStatus Status { get; }

        public string Code { get; }

        public string OperationId { get; }

        public string CheckpointId { get; }

        public string SourceArtifactHash { get; }

        public string DestinationArtifactHash { get; }

        public CorpusFingerprint BeforeFingerprint { get; }

        public CorpusFingerprint AfterFingerprint { get; }

        public bool CheckpointCreated { get; }

        public bool SaveAsCompleted { get; }

        public bool ReloadSucceeded { get; }

        public bool ValidationSucceeded { get; }

        public bool SemanticDiffMatched { get; }

        public IReadOnlyList<string> DiagnosticCodes { get; }

        public bool Completed =>
            Status == MutationExecutionStatus.Completed &&
            CheckpointCreated &&
            SaveAsCompleted &&
            ReloadSucceeded &&
            ValidationSucceeded &&
            SemanticDiffMatched;
    }

    public class MutationHost
    {
        private readonly PackageLoader packageLoader;
        private readonly PackageAnalysisSnapshotFactory snapshotFactory;
        private readonly CorpusSnapshotBuilder corpusBuilder;
        private readonly ICorpusFingerprintProvider fingerprintProvider;
        private readonly IMutationPreviewer previewer;
        private readonly IMutationCheckpointService checkpointService;
        private readonly INativeMutationValidator validator;
        private readonly MutationArtifactStager artifactStager;

        public MutationHost(
            PackageLoader packageLoader,
            PackageAnalysisSnapshotFactory snapshotFactory,
            CorpusSnapshotBuilder corpusBuilder,
            ICorpusFingerprintProvider fingerprintProvider,
            IMutationPreviewer previewer,
            IMutationCheckpointService checkpointService,
            INativeMutationValidator validator,
            MutationArtifactStager artifactStager)
        {
            this.packageLoader = packageLoader ??
                throw new ArgumentNullException(nameof(packageLoader));

            this.snapshotFactory = snapshotFactory ??
                throw new ArgumentNullException(nameof(snapshotFactory));

            this.corpusBuilder = corpusBuilder ??
                throw new ArgumentNullException(nameof(corpusBuilder));

            this.fingerprintProvider = fingerprintProvider ??
                throw new ArgumentNullException(nameof(fingerprintProvider));

            this.previewer = previewer ??
                throw new ArgumentNullException(nameof(previewer));

            this.checkpointService = checkpointService ??
                throw new ArgumentNullException(nameof(checkpointService));

            this.validator = validator ??
                throw new ArgumentNullException(nameof(validator));

            this.artifactStager = artifactStager ??
                throw new ArgumentNullException(nameof(artifactStager));
        }

        public ColumnResizeQuickAnalysis AnalyzeColumnResize(string sourcePackagePath, string taskId,
            int sourceComponentId, int outputColumnId, int destinationComponentId, int proposedWidth,
            int sampleRowLimit = 1000)
        {
            using (var guard = new FileStream(sourcePackagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var hash = MutationHash.FileSha256(sourcePackagePath);
                var loaded = packageLoader.Load(sourcePackagePath);
                if (!loaded.Succeeded) { throw new InvalidOperationException("mutation.source.load_failed"); }
                try
                {
                    return ColumnResizePlan.Analyze(loaded.Session.Package, hash, taskId, sourceComponentId,
                        outputColumnId, destinationComponentId, proposedWidth, sampleRowLimit);
                }
                finally { loaded.Session.Package.Dispose(); }
            }
        }

        public ColumnResizePlan PreviewColumnResize(string sourcePackagePath, string taskId,
            int sourceComponentId, int outputColumnId, int destinationComponentId, int proposedWidth,
            ColumnResizeQuickAnalysis quickAnalysis = null, bool acknowledgeDataLoss = false)
        {
            using (var guard = new FileStream(sourcePackagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var hash = MutationHash.FileSha256(sourcePackagePath);
                var loaded = packageLoader.Load(sourcePackagePath);
                if (!loaded.Succeeded) { throw new InvalidOperationException("mutation.source.load_failed"); }
                try
                {
                    return ColumnResizePlan.Create(loaded.Session.Package, hash, taskId, sourceComponentId,
                        outputColumnId, destinationComponentId, proposedWidth, quickAnalysis, acknowledgeDataLoss);
                }
                finally { loaded.Session.Package.Dispose(); }
            }
        }

        public async Task<MutationHostResult> ExecuteAsync(
            MutationHostRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var operationId = Guid.NewGuid().ToString("N");
            var diagnosticCodes = new List<string>();

            string sourceHash = string.Empty;
            string destinationHash = string.Empty;
            string checkpointId = string.Empty;

            CorpusFingerprint beforeFingerprint = null;
            CorpusFingerprint afterFingerprint = null;

            var checkpointCreated = false;
            var saveAsCompleted = false;
            var reloadSucceeded = false;
            var validationSucceeded = false;
            var semanticDiffMatched = false;

            PackageSession<Package> originalSession = null;
            FileStream sourceGuard = null;

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                ValidateHostRequest(request);

                sourceGuard = new FileStream(request.SourcePackagePath, FileMode.Open, FileAccess.Read, FileShare.Read);

                sourceHash = MutationHash.FileSha256(
                    request.SourcePackagePath);

                if (!string.Equals(
                    sourceHash,
                    request.ExpectedSourceHash,
                    StringComparison.Ordinal))
                {
                    return Failure(
                        MutationExecutionStatus.ValidationFailed,
                        "mutation.source.hash_changed",
                        operationId,
                        sourceHash,
                        diagnosticCodes);
                }

                var load = packageLoader.Load(
                    request.SourcePackagePath);

                if (!load.Succeeded ||
                    load.Session == null)
                {
                    return Failure(
                        MutationExecutionStatus.ValidationFailed,
                        "mutation.source.load_failed",
                        operationId,
                        sourceHash,
                        diagnosticCodes);
                }

                originalSession = load.Session;

                var beforeAnalysisResult =
                    snapshotFactory.Create(originalSession);

                if (beforeAnalysisResult.Items.Count != 1)
                {
                    return Failure(
                        MutationExecutionStatus.ValidationFailed,
                        "mutation.source.analysis_failed",
                        operationId,
                        sourceHash,
                        diagnosticCodes);
                }

                var beforeAnalysis =
                    beforeAnalysisResult.Items[0];

                if (request.ResizePlan != null)
                {
                    request.ResizePlan.CheckCurrent(originalSession.Package, sourceHash);
                }
                else
                {
                var rebuiltPreview = previewer.Preview(
                    beforeAnalysis,
                    request.Plan.Request);

                if (!rebuiltPreview.IsValid ||
                    rebuiltPreview.ExecutionPlan == null)
                {
                    return Failure(
                        MutationExecutionStatus.ValidationFailed,
                        "mutation.preview.no_longer_valid",
                        operationId,
                        sourceHash,
                        diagnosticCodes);
                }

                if (!PlansMatch(
                    request.Plan,
                    rebuiltPreview.ExecutionPlan))
                {
                    return Failure(
                        MutationExecutionStatus.ValidationFailed,
                        "mutation.plan.changed",
                        operationId,
                        sourceHash,
                        diagnosticCodes);
                }

                    }

                    var requirements = request.ResizePlan?.Requirements ?? request.Plan.Requirements;

                if (!AllRenameRequirementsPresent(requirements))
                {
                    return Failure(
                        MutationExecutionStatus.ValidationFailed,
                        "mutation.requirements.incomplete",
                        operationId,
                        sourceHash,
                        diagnosticCodes);
                }

                var beforeCorpus =
                    corpusBuilder.Build(beforeAnalysis);

                beforeFingerprint =
                    fingerprintProvider.Create(beforeCorpus);

                if (!CanVerifyOperation(beforeAnalysis, request.ResizePlan != null))
                {
                    return Failure(
                        MutationExecutionStatus.ValidationFailed,
                        "mutation.source.coverage_incomplete",
                        operationId,
                        sourceHash,
                        diagnosticCodes,
                        beforeFingerprint);
                }

                var checkpoint = await checkpointService
                    .CreateAsync(
                        new MutationCheckpointRequest(
                            request.SourcePackagePath,
                            sourceHash,
                            beforeFingerprint,
                            operationId,
                            request.ResizePlan == null ? "mutation.rename_task.before" : "mutation.resize_column.before"),
                        cancellationToken)
                    .ConfigureAwait(false);

                if (!checkpoint.Succeeded)
                {
                    return Failure(
                        MutationExecutionStatus.CheckpointRequired,
                        checkpoint.Code.Length == 0
                            ? "mutation.checkpoint.failed"
                            : checkpoint.Code,
                        operationId,
                        sourceHash,
                        diagnosticCodes,
                        beforeFingerprint);
                }

                if (!string.Equals(
                    checkpoint.ArtifactHash,
                    sourceHash,
                    StringComparison.Ordinal))
                {
                    return Failure(
                        MutationExecutionStatus.CheckpointRequired,
                        "mutation.checkpoint.hash_mismatch",
                        operationId,
                        sourceHash,
                        diagnosticCodes,
                        beforeFingerprint);
                }

                checkpointCreated = true;
                checkpointId = checkpoint.CheckpointId;

                cancellationToken.ThrowIfCancellationRequested();

                if (request.ResizePlan != null) { request.ResizePlan.Apply(originalSession.Package); }
                else { ApplyRename(
                    originalSession.Package,
                    request.Plan); }

                PackageAnalysisSnapshot stagedAnalysis = null;
                NativeMutationValidationResult stagedValidation = null;

                artifactStager.StageAndPublish(
                    request.SourcePackagePath,
                    request.DestinationPackagePath,
                    stream =>
                    {
                        WriteNativePackage(
                            originalSession.Package,
                            stream);
                    },
                    stream =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        using (var stagedPackage = ReadNativePackage(stream))
                        {

                        var stagedSession =
                            new PackageSession<Package>(
                                request.DestinationPackagePath,
                                stagedPackage.Name,
                                stagedPackage);

                        var analysisResult =
                            snapshotFactory.Create(stagedSession);

                        if (analysisResult.Items.Count != 1)
                        {
                            diagnosticCodes.Add(
                                "mutation.reload.analysis_failed");

                            return false;
                        }

                        reloadSucceeded = true;
                        stagedAnalysis = analysisResult.Items[0];

                        stagedValidation =
                            validator.Validate(stagedSession);

                        if (stagedValidation == null)
                        {
                            diagnosticCodes.Add(
                                "mutation.validation.no_result");

                            return false;
                        }

                        foreach (var code in
                            stagedValidation.DiagnosticCodes)
                        {
                            diagnosticCodes.Add(code);
                        }

                        if (!stagedValidation.Succeeded)
                        {
                            diagnosticCodes.Add(
                                "mutation.validation.failed");

                            return false;
                        }

                        validationSucceeded = true;

                        analysisResult = snapshotFactory.Create(stagedSession);
                        if (analysisResult.Items.Count != 1)
                        {
                            diagnosticCodes.Add("mutation.validation.analysis_failed");
                            return false;
                        }
                        stagedAnalysis = analysisResult.Items[0];

                        var afterCorpus =
                            corpusBuilder.Build(stagedAnalysis);

                        afterFingerprint =
                            fingerprintProvider.Create(afterCorpus);

                        if (!CanVerifyOperation(stagedAnalysis, request.ResizePlan != null))
                        {
                            diagnosticCodes.Add(
                                "mutation.after.coverage_incomplete");

                            return false;
                        }

                        var semanticVerification =
                            request.ResizePlan != null
                                ? (request.ResizePlan.Verify(stagedPackage)
                                    ? RenameSemanticVerification.Success()
                                    : RenameSemanticVerification.Failure("mutation.resize.unexpected_change"))
                                : VerifyRenameOnly(
                                beforeCorpus,
                                afterCorpus,
                                request.Plan);

                        if (!semanticVerification.Succeeded)
                        {
                            diagnosticCodes.Add(
                                semanticVerification.Code);

                            return false;
                        }

                        cancellationToken.ThrowIfCancellationRequested();
                        semanticDiffMatched = true;
                        return true;
                        }
                    });

                saveAsCompleted = true;

                if (!File.Exists(
                    request.DestinationPackagePath))
                {
                    return Failure(
                        MutationExecutionStatus.SaveRequired,
                        "mutation.destination.missing",
                        operationId,
                        sourceHash,
                        diagnosticCodes,
                        beforeFingerprint,
                        afterFingerprint,
                        checkpointCreated,
                        checkpointId,
                        saveAsCompleted,
                        reloadSucceeded,
                        validationSucceeded,
                        semanticDiffMatched);
                }

                destinationHash = MutationHash.FileSha256(
                    request.DestinationPackagePath);

                if (!string.Equals(
                    MutationHash.FileSha256(
                        request.SourcePackagePath),
                    sourceHash,
                    StringComparison.Ordinal))
                {
                    return Failure(
                        MutationExecutionStatus.ValidationFailed,
                        "mutation.source.changed",
                        operationId,
                        sourceHash,
                        diagnosticCodes,
                        beforeFingerprint,
                        afterFingerprint,
                        checkpointCreated,
                        checkpointId,
                        saveAsCompleted,
                        reloadSucceeded,
                        validationSucceeded,
                        semanticDiffMatched,
                        destinationHash);
                }

                if (!reloadSucceeded)
                {
                    return Failure(
                        MutationExecutionStatus.ReloadRequired,
                        "mutation.reload.failed",
                        operationId,
                        sourceHash,
                        diagnosticCodes,
                        beforeFingerprint,
                        afterFingerprint,
                        checkpointCreated,
                        checkpointId,
                        saveAsCompleted,
                        false,
                        validationSucceeded,
                        semanticDiffMatched,
                        destinationHash);
                }

                if (!validationSucceeded)
                {
                    return Failure(
                        MutationExecutionStatus.ValidationFailed,
                        "mutation.validation.failed",
                        operationId,
                        sourceHash,
                        diagnosticCodes,
                        beforeFingerprint,
                        afterFingerprint,
                        checkpointCreated,
                        checkpointId,
                        saveAsCompleted,
                        reloadSucceeded,
                        false,
                        semanticDiffMatched,
                        destinationHash);
                }

                if (!semanticDiffMatched)
                {
                    return Failure(
                        MutationExecutionStatus.ValidationFailed,
                        "mutation.semantic_diff.unexpected",
                        operationId,
                        sourceHash,
                        diagnosticCodes,
                        beforeFingerprint,
                        afterFingerprint,
                        checkpointCreated,
                        checkpointId,
                        saveAsCompleted,
                        reloadSucceeded,
                        validationSucceeded,
                        false,
                        destinationHash);
                }

                return new MutationHostResult(
                    MutationExecutionStatus.Completed,
                    string.Empty,
                    operationId,
                    checkpointId,
                    sourceHash,
                    destinationHash,
                    beforeFingerprint,
                    afterFingerprint,
                    checkpointCreated: true,
                    saveAsCompleted: true,
                    reloadSucceeded: true,
                    validationSucceeded: true,
                    semanticDiffMatched: true,
                    diagnosticCodes: diagnosticCodes);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (InvalidOperationException exception)
            {
                diagnosticCodes.Add(
                    SafeCode(
                        exception.Message,
                        "mutation.execution.invalid_operation"));

                return Failure(
                    MutationExecutionStatus.ValidationFailed,
                    SafeCode(
                        exception.Message,
                        "mutation.execution.invalid_operation"),
                    operationId,
                    sourceHash,
                    diagnosticCodes,
                    beforeFingerprint,
                    afterFingerprint,
                    checkpointCreated,
                    checkpointId,
                    saveAsCompleted,
                    reloadSucceeded,
                    validationSucceeded,
                    semanticDiffMatched,
                    destinationHash);
            }
            catch (Exception)
            {
                diagnosticCodes.Add(
                    "mutation.execution.failed");

                return Failure(
                    MutationExecutionStatus.ValidationFailed,
                    "mutation.execution.failed",
                    operationId,
                    sourceHash,
                    diagnosticCodes,
                    beforeFingerprint,
                    afterFingerprint,
                    checkpointCreated,
                    checkpointId,
                    saveAsCompleted,
                    reloadSucceeded,
                    validationSucceeded,
                    semanticDiffMatched,
                    destinationHash);
            }
            finally
            {
                try { originalSession?.Package.Dispose(); }
                finally { sourceGuard?.Dispose(); }
            }
        }

        private static void ValidateHostRequest(
            MutationHostRequest request)
        {
            if (request.ResizePlan == null && request.Plan.Request.Kind !=
                MutationKind.RenameTask)
            {
                throw new InvalidOperationException(
                    "mutation.kind.unsupported");
            }

            if (request.ResizePlan == null && request.Plan.Request.Target.Kind !=
                SemanticObjectKind.Executable)
            {
                throw new InvalidOperationException(
                    "mutation.target.kind_invalid");
            }

            if (string.Equals(
                request.SourcePackagePath,
                request.DestinationPackagePath,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "mutation.destination.same_as_source");
            }

            if (!File.Exists(request.SourcePackagePath))
            {
                throw new InvalidOperationException(
                    "mutation.source.missing");
            }

            if (File.Exists(request.DestinationPackagePath) ||
                Directory.Exists(request.DestinationPackagePath))
            {
                throw new InvalidOperationException(
                    "mutation.destination.exists");
            }
        }

        private static bool CanVerifyOperation(PackageAnalysisSnapshot snapshot, bool resizing)
        {
            if (!resizing) { return RenameCoveragePolicy.CanVerifyRename(snapshot); }
            return snapshot.UnsupportedItems.Concat(snapshot.Dependencies.UnsupportedItems)
                .Concat(snapshot.ControlFlow.UnsupportedItems).All(gap =>
                    gap.ReasonCode == UnsupportedItem.IntentionalOmissionCode ||
                    (gap.ReasonCode == UnsupportedItem.TaskPropertiesNotInspectedCode &&
                     snapshot.Executables.Any(task => task.Id == gap.Id && task.CreationName == gap.CreationName)));
        }

        private static bool AllRenameRequirementsPresent(
            MutationExecutionRequirements requirements)
        {
            return requirements != null &&
                   requirements.CheckpointRequired &&
                   requirements.SaveAsRequired &&
                   requirements.ReloadRequired &&
                   requirements.ValidationRequired &&
                   requirements.SemanticDiffRequired;
        }

        private static bool PlansMatch(
            MutationExecutionPlan expected,
            MutationExecutionPlan actual)
        {
            return
                expected.Request.Kind == actual.Request.Kind &&
                expected.Request.Target.Kind ==
                    actual.Request.Target.Kind &&
                string.Equals(
                    expected.Request.Target.NativeId,
                    actual.Request.Target.NativeId,
                    StringComparison.Ordinal) &&
                string.Equals(
                    expected.ExpectedCurrentName,
                    actual.ExpectedCurrentName,
                    StringComparison.Ordinal) &&
                string.Equals(
                    expected.ProposedName,
                    actual.ProposedName,
                    StringComparison.Ordinal) &&
                RequirementsMatch(
                    expected.Requirements,
                    actual.Requirements);
        }

        private static bool RequirementsMatch(
            MutationExecutionRequirements expected,
            MutationExecutionRequirements actual)
        {
            if (expected == null || actual == null)
            {
                return false;
            }

            return
                expected.CheckpointRequired ==
                    actual.CheckpointRequired &&
                expected.SaveAsRequired ==
                    actual.SaveAsRequired &&
                expected.ReloadRequired ==
                    actual.ReloadRequired &&
                expected.ValidationRequired ==
                    actual.ValidationRequired &&
                expected.SemanticDiffRequired ==
                    actual.SemanticDiffRequired &&
                expected.Requirements.SequenceEqual(
                    actual.Requirements,
                    StringComparer.Ordinal);
        }

        private static void ApplyRename(
            Package package,
            MutationExecutionPlan plan)
        {
            var matches = FindTaskHosts(package)
                .Where(task =>
                    string.Equals(
                        task.ID,
                        plan.Request.Target.NativeId,
                        StringComparison.Ordinal))
                .ToArray();

            if (matches.Length == 0)
            {
                throw new InvalidOperationException(
                    "mutation.target.not_found");
            }

            if (matches.Length != 1)
            {
                throw new InvalidOperationException(
                    "mutation.target.ambiguous");
            }

            var target = matches[0];

            if (!string.Equals(
                target.Name,
                plan.ExpectedCurrentName,
                StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "mutation.target.current_name_changed");
            }

            if (string.IsNullOrWhiteSpace(plan.ProposedName) ||
                plan.ProposedName.Length > 128 ||
                plan.ProposedName.Any(char.IsControl))
            {
                throw new InvalidOperationException(
                    "mutation.value.invalid");
            }

            target.Name = plan.ProposedName;

            if (!string.Equals(
                target.Name,
                plan.ProposedName,
                StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "mutation.native_apply.failed");
            }
        }

        private static IEnumerable<TaskHost> FindTaskHosts(
            Package package)
        {
            foreach (var task in FindTaskHosts(
                package.Executables))
            {
                yield return task;
            }

            foreach (DtsEventHandler handler in
                package.EventHandlers)
            {
                foreach (var task in FindTaskHosts(
                    handler.Executables))
                {
                    yield return task;
                }
            }
        }

        private static IEnumerable<TaskHost> FindTaskHosts(
            Executables executables)
        {
            foreach (Executable executable in executables)
            {
                if (executable is TaskHost task)
                {
                    yield return task;
                }

                if (executable is IDTSSequence sequence)
                {
                    foreach (var nested in
                        FindTaskHosts(sequence.Executables))
                    {
                        yield return nested;
                    }
                }

                if (executable is EventsProvider eventsProvider)
                {
                    foreach (DtsEventHandler handler in
                        eventsProvider.EventHandlers)
                    {
                        foreach (var nested in
                            FindTaskHosts(handler.Executables))
                        {
                            yield return nested;
                        }
                    }
                }
            }
        }

        protected virtual void WriteNativePackage(
            Package package,
            Stream output)
        {
            package.SaveToXML(
                out var xml,
                null);

            if (string.IsNullOrWhiteSpace(xml))
            {
                throw new InvalidOperationException(
                    "mutation.native_save.empty");
            }

            using (var writer = new StreamWriter(
                output,
                new UTF8Encoding(false),
                4096,
                leaveOpen: true))
            {
                writer.Write(xml);
                writer.Flush();
            }
        }

        protected virtual Package ReadNativePackage(
            Stream input)
        {
            input.Position = 0;

            string xml;

            using (var reader = new StreamReader(
                input,
                Encoding.UTF8,
                true,
                4096,
                leaveOpen: true))
            {
                xml = reader.ReadToEnd();
            }

            if (string.IsNullOrWhiteSpace(xml))
            {
                throw new InvalidOperationException(
                    "mutation.reload.empty");
            }

            var package = new Package();
            try
            {
                package.LoadFromXML(xml, null);
                return package;
            }
            catch
            {
                package.Dispose();
                throw;
            }
        }

        private static RenameSemanticVerification VerifyRenameOnly(
            CorpusSnapshot before,
            CorpusSnapshot after,
            MutationExecutionPlan plan)
        {
            if (!string.Equals(
                before.SchemaVersion,
                after.SchemaVersion,
                StringComparison.Ordinal))
            {
                return RenameSemanticVerification.Failure(
                    "mutation.semantic_diff.schema_changed");
            }

            if (!string.Equals(
                    before.PackageId,
                    after.PackageId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    before.PackageName,
                    after.PackageName,
                    StringComparison.Ordinal))
            {
                return RenameSemanticVerification.Failure(
                    "mutation.semantic_diff.package_changed");
            }

            var beforeNodes = before.Nodes.ToDictionary(
                node => node.Key,
                StringComparer.Ordinal);

            var afterNodes = after.Nodes.ToDictionary(
                node => node.Key,
                StringComparer.Ordinal);

            if (beforeNodes.Count != afterNodes.Count ||
                beforeNodes.Keys.Except(
                    afterNodes.Keys,
                    StringComparer.Ordinal).Any())
            {
                return RenameSemanticVerification.Failure(
                    "mutation.semantic_diff.node_inventory_changed");
            }

            var changedNodes = new List<string>();

            foreach (var pair in beforeNodes)
            {
                var oldNode = pair.Value;
                var newNode = afterNodes[pair.Key];

                var nameChanged = !string.Equals(
                    oldNode.Name,
                    newNode.Name,
                    StringComparison.Ordinal);

                var otherMetadataChanged =
                    oldNode.Kind != newNode.Kind ||
                    !string.Equals(
                        oldNode.NativeId,
                        newNode.NativeId,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        oldNode.ParentId,
                        newNode.ParentId,
                        StringComparison.Ordinal);

                if (otherMetadataChanged)
                {
                    return RenameSemanticVerification.Failure(
                        "mutation.semantic_diff.node_metadata_changed");
                }

                if (nameChanged)
                {
                    changedNodes.Add(pair.Key);

                        if ((oldNode.Kind != SemanticObjectKind.Executable && oldNode.Kind != SemanticObjectKind.DataFlow) ||
                        !string.Equals(
                            oldNode.NativeId,
                            plan.Request.Target.NativeId,
                            StringComparison.Ordinal) ||
                        !string.Equals(
                            oldNode.Name,
                            plan.ExpectedCurrentName,
                            StringComparison.Ordinal) ||
                        !string.Equals(
                            newNode.Name,
                            plan.ProposedName,
                            StringComparison.Ordinal))
                    {
                        return RenameSemanticVerification.Failure(
                            "mutation.semantic_diff.unexpected_rename");
                    }
                }
            }

            if (changedNodes.Count < 1 || changedNodes.Count > 2 ||
                changedNodes.Count(key => beforeNodes[key].Kind == SemanticObjectKind.Executable) != 1)
            {
                return RenameSemanticVerification.Failure(
                    "mutation.semantic_diff.rename_count_invalid");
            }

            if (!EdgesEqual(before.Edges, after.Edges))
            {
                return RenameSemanticVerification.Failure(
                    "mutation.semantic_diff.edges_changed");
            }

            if (!CoverageEqual(
                before.CoverageGaps,
                after.CoverageGaps))
            {
                return RenameSemanticVerification.Failure(
                    "mutation.semantic_diff.coverage_changed");
            }

            return RenameSemanticVerification.Success();
        }

        private static bool EdgesEqual(
            IEnumerable<CorpusEdge> first,
            IEnumerable<CorpusEdge> second)
        {
            return EdgeKeys(first).SequenceEqual(
                EdgeKeys(second),
                StringComparer.Ordinal);
        }

        private static IEnumerable<string> EdgeKeys(
            IEnumerable<CorpusEdge> edges)
        {
            return edges
                .Select(edge =>
                    edge.From + "\u001f" +
                    edge.To + "\u001f" +
                    edge.Kind + "\u001f" +
                    edge.Evidence)
                .OrderBy(value => value, StringComparer.Ordinal);
        }

        private static bool CoverageEqual(
            IEnumerable<CorpusCoverageGap> first,
            IEnumerable<CorpusCoverageGap> second)
        {
            return CoverageKeys(first).SequenceEqual(
                CoverageKeys(second),
                StringComparer.Ordinal);
        }

        private static IEnumerable<string> CoverageKeys(
            IEnumerable<CorpusCoverageGap> gaps)
        {
            return gaps
                .Select(gap =>
                    gap.ReasonCode + "\u001f" +
                    gap.Count)
                .OrderBy(value => value, StringComparer.Ordinal);
        }

        private static string SafeCode(
            string candidate,
            string fallback)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return fallback;
            }

            if (!candidate.StartsWith(
                "mutation.",
                StringComparison.Ordinal))
            {
                return fallback;
            }

            return candidate.All(character =>
                char.IsLetterOrDigit(character) ||
                character == '.' ||
                character == '_')
                    ? candidate
                    : fallback;
        }

        private static MutationHostResult Failure(
            MutationExecutionStatus status,
            string code,
            string operationId,
            string sourceHash,
            IEnumerable<string> diagnosticCodes,
            CorpusFingerprint beforeFingerprint = null,
            CorpusFingerprint afterFingerprint = null,
            bool checkpointCreated = false,
            string checkpointId = "",
            bool saveAsCompleted = false,
            bool reloadSucceeded = false,
            bool validationSucceeded = false,
            bool semanticDiffMatched = false,
            string destinationHash = "")
        {
            return new MutationHostResult(
                status,
                code,
                operationId,
                checkpointId,
                sourceHash,
                destinationHash,
                beforeFingerprint,
                afterFingerprint,
                checkpointCreated,
                saveAsCompleted,
                reloadSucceeded,
                validationSucceeded,
                semanticDiffMatched,
                diagnosticCodes);
        }

        private sealed class RenameSemanticVerification
        {
            private RenameSemanticVerification(
                bool succeeded,
                string code)
            {
                Succeeded = succeeded;
                Code = code ?? string.Empty;
            }

            public bool Succeeded { get; }

            public string Code { get; }

            public static RenameSemanticVerification Success()
            {
                return new RenameSemanticVerification(
                    true,
                    string.Empty);
            }

            public static RenameSemanticVerification Failure(
                string code)
            {
                return new RenameSemanticVerification(
                    false,
                    code);
            }
        }
    }

    internal static class MutationHash
    {
        public static string FileSha256(string path)
        {
            using (var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read))
            using (var algorithm = SHA256.Create())
            {
                return Hex(
                    algorithm.ComputeHash(stream));
            }
        }

        public static bool IsSha256(string value)
        {
            if (value == null || value.Length != 64)
            {
                return false;
            }

            return value.All(character =>
                (character >= '0' && character <= '9') ||
                (character >= 'a' && character <= 'f'));
        }

        private static string Hex(byte[] value)
        {
            return BitConverter
                .ToString(value)
                .Replace("-", string.Empty)
                .ToLowerInvariant();
        }
    }
}