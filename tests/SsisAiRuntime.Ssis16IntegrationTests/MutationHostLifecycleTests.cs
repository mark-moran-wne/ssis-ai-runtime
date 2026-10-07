using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.SqlServer.Dts.Runtime;
using SsisAiRuntime.Core;
using SsisAiRuntime.Corpus;
using SsisAiRuntime.Inspectors;
using SsisAiRuntime.Mutations;
using SsisAiRuntime.MutationHost;
using SsisAiRuntime.Ssis16;
using Host = SsisAiRuntime.MutationHost.MutationHost;

namespace SsisAiRuntime.Ssis16IntegrationTests
{
    internal static class MutationHostLifecycleTests
    {
        private static string currentCase;
        private static int passedCases;

        public static int Run()
        {
            passedCases = 0;
            try
            {
                Case("successful-data-flow-rename", fixture =>
                {
                    var result = fixture.Execute();
                    Require(result.Completed, "success:" + result.Code + ":" + string.Join(",", result.DiagnosticCodes));
                    Require(result.CheckpointCreated && result.ReloadSucceeded && result.ValidationSucceeded && result.SemanticDiffMatched, "success.evidence");
                    Require(!result.BeforeFingerprint.IsComplete && !result.AfterFingerprint.IsComplete,
                        "success.scoped_coverage_not_full_coverage");
                    Require(result.DestinationArtifactHash == Hash(fixture.Destination), "success.destination.hash");
                    Require(fixture.Checkpoint.Calls == 1 && fixture.Validator.Calls == 1 &&
                        Hash(fixture.CheckpointPath) == fixture.ExpectedHash, "success.checkpoint");
                    var loaded = new PackageLoader().Load(fixture.Destination);
                    Require(loaded.Succeeded, "success.reload");
                    try { Require(((TaskHost)loaded.Session.Package.Executables[0]).Name == "Renamed", "success.name"); }
                    finally { loaded.Session.Package.Dispose(); }
                });
                Case("wrong-current-name", fixture =>
                {
                    fixture.Plan = new MutationExecutionPlan(fixture.Plan.Request, "StaleName", "Renamed",
                        fixture.Plan.Impact, fixture.Plan.Requirements);
                    fixture.Refused("mutation.plan.changed");
                    Require(fixture.Checkpoint.Calls == 0, "stale.no_checkpoint");
                });
                Case("missing-target", fixture =>
                {
                    var request = new MutationRequest(MutationKind.RenameTask,
                        new MutationTarget(SemanticObjectKind.Executable, "missing-id"), "Renamed");
                    fixture.Plan = new MutationExecutionPlan(request, "Original", "Renamed", fixture.Plan.Impact, fixture.Plan.Requirements);
                    fixture.Refused("mutation.preview.no_longer_valid");
                });
                Case("malformed-duplicate-native-ids", fixture =>
                {
                    var xml = XDocument.Load(fixture.Source);
                    var target = xml.Descendants().Single(element => element.Name.LocalName == "Executable" &&
                        element.Attributes().Any(attribute => attribute.Name.LocalName == "DTSID" &&
                            attribute.Value == fixture.Plan.Request.Target.NativeId));
                    var duplicate = new XElement(target);
                    duplicate.Attributes().Single(attribute => attribute.Name.LocalName == "ObjectName").Value = "MalformedDuplicate";
                    duplicate.Attributes().Single(attribute => attribute.Name.LocalName == "refId").Value += "-duplicate";
                    target.Parent.Add(duplicate);
                    Require(xml.Descendants().Count(element => element.Name.LocalName == "Executable" &&
                        element.Attributes().Any(attribute => attribute.Name.LocalName == "DTSID" &&
                            attribute.Value == fixture.Plan.Request.Target.NativeId)) == 2, "duplicate.fixture.precondition");
                    xml.Save(fixture.Source);
                    fixture.ExpectedHash = Hash(fixture.Source);
                    fixture.Refused();
                    Require(fixture.Checkpoint.Calls == 0 && fixture.Validator.Calls == 0,
                        "duplicate.refused_before_checkpoint");
                });
                Case("source-hash-changed", fixture =>
                {
                    File.AppendAllText(fixture.Source, Environment.NewLine);
                    var changedHash = Hash(fixture.Source);
                    fixture.Refused("mutation.source.hash_changed");
                    Require(fixture.Checkpoint.Calls == 0, "hash.no_checkpoint");
                    Require(Hash(fixture.Source) == changedHash, "hash.changed_source_preserved");
                });
                Case("missing-requirements", fixture =>
                {
                    fixture.Plan = new MutationExecutionPlan(fixture.Plan.Request, fixture.Plan.ExpectedCurrentName,
                        fixture.Plan.ProposedName, fixture.Plan.Impact,
                        new MutationExecutionRequirements(false, false, false, false, false, Array.Empty<string>()));
                    fixture.Refused("mutation.plan.changed");
                    Require(fixture.Checkpoint.Calls == 0, "requirements.no_checkpoint");
                });
                Case("existing-destination", fixture =>
                {
                    File.WriteAllText(fixture.Destination, "other-writer");
                    var result = fixture.Execute();
                    Require(!result.Completed && result.Code == "mutation.destination.exists" &&
                        File.ReadAllText(fixture.Destination) == "other-writer", "existing.preserved");
                });
                Case("checkpoint-failure", fixture =>
                {
                    fixture.Checkpoint.Fail = true;
                    fixture.Refused("mutation.checkpoint.test_failed");
                    Require(fixture.Validator.Calls == 0, "checkpoint.no_validator");
                });
                Case("checkpoint-hash-mismatch", fixture =>
                {
                    fixture.Checkpoint.WrongHash = true;
                    fixture.Refused("mutation.checkpoint.hash_mismatch");
                });
                Case("asynchronous-checkpoint", fixture =>
                {
                    fixture.Checkpoint.Deferred = true;
                    var callerThread = Thread.CurrentThread.ManagedThreadId;
                    var pending = fixture.BeginExecute();
                    try
                    {
                        Require(!pending.IsCompleted && fixture.Checkpoint.Calls == 1 &&
                            !File.Exists(fixture.CheckpointPath) && !File.Exists(fixture.Destination),
                            "async.pending_precondition");
                    }
                    finally { fixture.Checkpoint.Release(); }
                    var result = pending.GetAwaiter().GetResult();
                    Require(result.Completed && fixture.Checkpoint.CopyThreadId != callerThread &&
                        Hash(fixture.CheckpointPath) == fixture.ExpectedHash, "async.checkpoint.completed");
                });
                Case("validation-failure-cleanup", fixture =>
                {
                    fixture.Validator.Fail = true;
                    var result = fixture.Refused();
                    Require(result.ReloadSucceeded && !result.ValidationSucceeded &&
                        result.DiagnosticCodes.Contains("mutation.validation.failed"), "validation.evidence");
                });
                Case("save-failure-cleanup", fixture =>
                {
                    fixture.SaveFails = true;
                    var result = fixture.Refused("mutation.execution.failed");
                    Require(result.CheckpointCreated && !result.SaveAsCompleted && !result.ReloadSucceeded,
                        "save.failure.evidence");
                });
                Case("reload-failure-cleanup", fixture =>
                {
                    fixture.ReloadFails = true;
                    var result = fixture.Refused("mutation.reload.test_failed");
                    Require(result.CheckpointCreated && !result.ReloadSucceeded && fixture.Validator.Calls == 0,
                        "reload.failure.evidence");
                });
                Case("destination-created-during-verification", fixture =>
                {
                    fixture.Validator.OnValidate = session => File.WriteAllText(fixture.Destination, "other-writer");
                    var result = fixture.Execute();
                    Require(!result.Completed && !result.SaveAsCompleted && File.ReadAllText(fixture.Destination) == "other-writer",
                        "publication.race.preserved");
                });
                Case("validator-exception-cleanup", fixture =>
                {
                    fixture.Validator.OnValidate = session => { throw new InvalidOperationException("mutation.validator.test_failed"); };
                    fixture.Refused("mutation.validator.test_failed");
                });
                Case("source-lock-during-checkpoint", fixture =>
                {
                    fixture.Checkpoint.OnCheckpoint = request =>
                    {
                        var refused = false;
                        try { File.AppendAllText(fixture.Source, "unexpected"); }
                        catch (IOException) { refused = true; }
                        Require(refused, "source.lock");
                    };
                    Require(fixture.Execute().Completed, "source.lock.success");
                });
                Case("cancellation-after-checkpoint", fixture =>
                {
                    using (var cancellation = new CancellationTokenSource())
                    {
                        fixture.Checkpoint.OnCheckpoint = request => cancellation.Cancel();
                        var cancelled = false;
                        try { fixture.Execute(cancellation.Token); }
                        catch (OperationCanceledException) { cancelled = true; }
                        Require(cancelled && !File.Exists(fixture.Destination), "cancellation.no_publication");
                    }
                });
                Case("nested-target", fixture => Require(fixture.Execute().Completed, "nested.rename"), nested: true);
                Case("event-handler-target", fixture => Require(fixture.Execute().Completed, "handler.rename"), eventHandler: true);
                Case("semantic-diff-rejection", fixture =>
                {
                    fixture.Validator.AlterPackageName = true;
                    var result = fixture.Refused();
                    Require(result.DiagnosticCodes.Contains("mutation.semantic_diff.package_changed"), "diff.evidence");
                });
                Case("missing-destination-directory", fixture =>
                {
                    fixture.Destination = Path.Combine(fixture.Directory, "absent", "output.dtsx");
                    fixture.Refused();
                });
                Case("incomplete-source-coverage", fixture =>
                {
                    var package = new Package();
                    try
                    {
                        package.LoadFromXML(File.ReadAllText(fixture.Source), null);
                        package.SetExpression("Disable", "@[$Project::Unavailable]");
                        new Application().SaveToXml(fixture.Source, package, null);
                    }
                    finally { package.Dispose(); }
                    fixture.ExpectedHash = Hash(fixture.Source);
                    fixture.Refused("mutation.preview.no_longer_valid");
                    Require(fixture.Checkpoint.Calls == 0, "coverage.no_checkpoint");
                });
                Require(passedCases == 22, "lifecycle.case_count_changed");
                Console.WriteLine("Mutation host lifecycle: PASS; " + passedCases + " cases; scratch rename, async checkpoint, duplicate-ID refusal, native reload, cleanup and source preservation. Validators are test doubles; no native Validate or Execute.");
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("Mutation host lifecycle failed at " + currentCase + ": " + error.Message);
                return 1;
            }
        }

        private static void Case(string name, Action<Fixture> test, bool nested = false, bool eventHandler = false)
        {
            currentCase = name;
            using (var fixture = new Fixture(nested, eventHandler))
            {
                test(fixture);
                Require(Hash(fixture.Source) == fixture.ExpectedHash || name == "source-hash-changed", "source.unchanged");
                Require(System.IO.Directory.GetFiles(fixture.Directory, ".ssis-mutation-*.dtsx", SearchOption.AllDirectories).Length == 0, "staging.cleaned");
            }
            passedCases++;
            Console.WriteLine("Mutation host " + name + ": PASS.");
        }

        private sealed class Fixture : IDisposable
        {
            public string Directory { get; } = Path.Combine(Path.GetTempPath(), "SsisMutationHost-" + Guid.NewGuid().ToString("N"));
            public string Source { get; }
            public string Destination { get; set; }
            public string CheckpointPath { get; }
            public string ExpectedHash { get; set; }
            public MutationExecutionPlan Plan { get; set; }
            public TestCheckpoint Checkpoint { get; }
            public TestValidator Validator { get; } = new TestValidator();
            public bool SaveFails { get; set; }
            public bool ReloadFails { get; set; }

            public Fixture(bool nested = false, bool eventHandler = false)
            {
                System.IO.Directory.CreateDirectory(Directory);
                Source = Path.Combine(Directory, "source.dtsx");
                Destination = Path.Combine(Directory, "output.dtsx");
                CheckpointPath = Path.Combine(Directory, "checkpoint.dtsx");
                string targetId;
                using (var package = new Package { Name = "HostFixture", ProtectionLevel = DTSProtectionLevel.DontSaveSensitive })
                {
                    var executables = nested ? ((Sequence)package.Executables.Add("STOCK:Sequence")).Executables : package.Executables;
                    if (eventHandler)
                    {
                        var owner = (TaskHost)executables.Add("STOCK:PipelineTask");
                        owner.Name = "EventOwner";
                        executables = ((DtsEventHandler)owner.EventHandlers.Add("OnError")).Executables;
                    }
                    var task = (TaskHost)executables.Add("STOCK:PipelineTask");
                    task.Name = "Original";
                    targetId = task.ID;
                    new Application().SaveToXml(Source, package, null);
                }
                ExpectedHash = Hash(Source);
                var loaded = new PackageLoader().Load(Source);
                Require(loaded.Succeeded, "fixture.load");
                try
                {
                    var snapshot = new PackageAnalysisSnapshotFactory().Create(loaded.Session).Items.Single();
                    var preview = new MutationPreviewer().Preview(snapshot, new MutationRequest(MutationKind.RenameTask,
                        new MutationTarget(SemanticObjectKind.Executable, targetId), "Renamed"));
                    Require(preview.IsValid, "fixture.preview:" + preview.Status + ":" + string.Join(",",
                        preview.CoverageGaps.Select(gap => gap.ReasonCode + "/" + gap.Name).Distinct()));
                    Plan = preview.ExecutionPlan;
                }
                finally { loaded.Session.Package.Dispose(); }
                Checkpoint = new TestCheckpoint(CheckpointPath);
            }

            public MutationHostResult Execute(CancellationToken cancellationToken = default(CancellationToken))
            {
                return BeginExecute(cancellationToken).GetAwaiter().GetResult();
            }

            public Task<MutationHostResult> BeginExecute(CancellationToken cancellationToken = default(CancellationToken))
            {
                var host = new FaultHost(Checkpoint, Validator, SaveFails, ReloadFails);
                return host.ExecuteAsync(new MutationHostRequest(Source, Destination, Plan, ExpectedHash), cancellationToken);
            }

            public MutationHostResult Refused(string code = null)
            {
                var result = Execute();
                Require(!result.Completed && !File.Exists(Destination), "refused.not_published");
                if (code != null) { Require(result.Code == code, "refused.code:" + result.Code); }
                return result;
            }

            public void Dispose() => System.IO.Directory.Delete(Directory, true);
        }

        private sealed class TestCheckpoint : IMutationCheckpointService
        {
            private readonly string path;
            private readonly TaskCompletionSource<bool> gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            public TestCheckpoint(string path) { this.path = path; }
            public int Calls { get; private set; }
            public bool Fail { get; set; }
            public bool WrongHash { get; set; }
            public bool Deferred { get; set; }
            public int CopyThreadId { get; private set; }
            public Action<MutationCheckpointRequest> OnCheckpoint { get; set; }
            public Task<MutationCheckpointResult> CreateAsync(MutationCheckpointRequest request, CancellationToken cancellationToken)
            {
                Calls++;
                cancellationToken.ThrowIfCancellationRequested();
                if (Deferred) { return CreateDeferredAsync(request, cancellationToken); }
                return System.Threading.Tasks.Task.FromResult(Copy(request, cancellationToken));
            }

            public void Release() => gate.TrySetResult(true);

            private async Task<MutationCheckpointResult> CreateDeferredAsync(MutationCheckpointRequest request, CancellationToken cancellationToken)
            {
                await gate.Task.ConfigureAwait(false);
                return Copy(request, cancellationToken);
            }

            private MutationCheckpointResult Copy(MutationCheckpointRequest request, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CopyThreadId = Thread.CurrentThread.ManagedThreadId;
                if (Fail) { return new MutationCheckpointResult(false, "", "", "mutation.checkpoint.test_failed"); }
                File.Copy(request.SourcePackagePath, path);
                OnCheckpoint?.Invoke(request);
                return new MutationCheckpointResult(true, "test-checkpoint", WrongHash ? new string('0', 64) : Hash(path), "");
            }
        }

        private sealed class FaultHost : Host
        {
            private readonly bool saveFails;
            private readonly bool reloadFails;
            public FaultHost(TestCheckpoint checkpoint, TestValidator validator, bool saveFails, bool reloadFails)
                : base(new PackageLoader(), new PackageAnalysisSnapshotFactory(), new CorpusSnapshotBuilder(),
                    new CorpusFingerprintProvider(), new MutationPreviewer(), checkpoint, validator, new MutationArtifactStager())
            {
                this.saveFails = saveFails;
                this.reloadFails = reloadFails;
            }

            protected override void WriteNativePackage(Package package, Stream output)
            {
                if (saveFails) { output.WriteByte(1); throw new IOException("test.native.save.failure"); }
                base.WriteNativePackage(package, output);
            }

            protected override Package ReadNativePackage(Stream input)
            {
                if (reloadFails) { throw new InvalidOperationException("mutation.reload.test_failed"); }
                return base.ReadNativePackage(input);
            }
        }

        private sealed class TestValidator : INativeMutationValidator
        {
            public int Calls { get; private set; }
            public bool Fail { get; set; }
            public bool AlterPackageName { get; set; }
            public Action<PackageSession<Package>> OnValidate { get; set; }
            public NativeMutationValidationResult Validate(PackageSession<Package> session)
            {
                Calls++;
                OnValidate?.Invoke(session);
                if (AlterPackageName) { session.Package.Name = "UnexpectedChange"; }
                return new NativeMutationValidationResult(!Fail, false, Array.Empty<string>());
            }
        }

        private static string Hash(string path)
        {
            using (var algorithm = SHA256.Create())
            using (var stream = File.OpenRead(path))
            { return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) { throw new InvalidOperationException(message); }
        }
    }
}