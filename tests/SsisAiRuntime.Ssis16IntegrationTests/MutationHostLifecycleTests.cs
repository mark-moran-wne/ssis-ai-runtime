using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.SqlServer.Dts.Pipeline.Wrapper;
using Microsoft.SqlServer.Dts.Runtime;
using Microsoft.SqlServer.Dts.Tasks.TransferSqlServerObjectsTask;
using Newtonsoft.Json.Linq;
using SsisAiRuntime.Core;
using SsisAiRuntime.Corpus;
using SsisAiRuntime.Inspectors;
using SsisAiRuntime.Mutations;
using SsisAiRuntime.MutationHost;
using SsisAiRuntime.Ssis16;
using Host = SsisAiRuntime.MutationHost.MutationHost;
using RuntimeWrapper = Microsoft.SqlServer.Dts.Runtime.Wrapper;

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
                Case("file-checkpoint-service", fixture => VerifyFileCheckpointService(fixture));
                VerifyTransferTableUpdateModes();
                VerifyNativeTaskPropertySet();
                VerifyTransferTablesHostLifecycle();
                Case("native-package-validation", fixture =>
                {
                    var load = new PackageLoader().Load(fixture.Source);
                    Require(load.Succeeded, "native-validation.load");
                    try
                    {
                        var package = load.Session.Package;
                        var previousOfflineMode = package.OfflineMode;
                        var result = new NativePackageMutationValidator().Validate(load.Session);
                        Require(result.Succeeded && !result.HasWarnings && result.DiagnosticCodes.Count == 0,
                            "native-validation.result:" + string.Join(",", result.DiagnosticCodes));
                        Require(package.OfflineMode == previousOfflineMode, "native-validation.offline-mode-restored");
                    }
                    finally { load.Session.Package.Dispose(); }
                });
                VerifyTransferTaskTablesRoundTrip();
                VerifyDerivedColumnInsertionLifecycle();
                VerifyDataConversionInsertionLifecycle();
                Require(passedCases == 30, "lifecycle.case_count_changed");
                Console.WriteLine("Mutation host lifecycle: PASS; " + passedCases + " cases; rename and positioned Derived Column/Data Conversion insertion, successful offline CLI apply, validation refusal, file checkpoint, async checkpoint, native reload, cleanup and source preservation. Successful host fault-path cases use validator doubles; no package Execute or data-source connection.");
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("Mutation host lifecycle failed at " + currentCase + ": " + error.Message);
                return 1;
            }
        }

        private static void VerifyFileCheckpointService(Fixture fixture)
        {
            var loaded = new PackageLoader().Load(fixture.Source);
            Require(loaded.Succeeded, "file_checkpoint.source_load");
            CorpusFingerprint fingerprint;
            try
            {
                var snapshot = new PackageAnalysisSnapshotFactory().Create(loaded.Session).Items.Single();
                fingerprint = new CorpusFingerprintProvider().Create(new CorpusSnapshotBuilder().Build(snapshot));
            }
            finally { loaded.Session.Package.Dispose(); }

            var directory = Path.Combine(fixture.Directory, "durable-checkpoints");
            var service = new FileMutationCheckpointService(directory);
            var request = new MutationCheckpointRequest(fixture.Source, fixture.ExpectedHash, fingerprint,
                "checkpoint-operation-1", "mutation.test");
            var result = service.CreateAsync(request, CancellationToken.None).GetAwaiter().GetResult();
            var artifact = Path.Combine(directory, "checkpoint-operation-1.dtsx");
            Require(result.Succeeded && result.ArtifactHash == fixture.ExpectedHash && File.Exists(artifact) &&
                File.ReadAllBytes(artifact).SequenceEqual(File.ReadAllBytes(fixture.Source)), "file_checkpoint.created_and_hashed");
            var originalCheckpoint = File.ReadAllBytes(artifact);
            var refused = service.CreateAsync(request, CancellationToken.None).GetAwaiter().GetResult();
            Require(!refused.Succeeded && refused.Code == "mutation.checkpoint.exists" &&
                File.ReadAllBytes(artifact).SequenceEqual(originalCheckpoint), "file_checkpoint.overwrite_refused");
        }

        private static void VerifyTransferTaskTablesRoundTrip()
        {
            currentCase = "transfer-task-tables-round-trip";
            var directory = Path.Combine(Path.GetTempPath(), "SsisTransferTask-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "source.dtsx");
            try
            {
                var application = new Application();
                var creationName = application.TaskInfos.Cast<TaskInfo>()
                    .Single(info => info.Name == "Transfer SQL Server Objects Task").CreationName;
                using (var package = new Package { Name = "TransferTaskFixture", ProtectionLevel = DTSProtectionLevel.DontSaveSensitive })
                {
                    var host = (TaskHost)package.Executables.Add(creationName);
                    host.Name = "CopySelectedObjects";
                    var transfer = (ITransferSqlServerObjectsTask)host.InnerObject;
                    transfer.CopyAllObjects = false;
                    transfer.CopyAllTables = false;
                    transfer.TablesList.Clear();
                    transfer.TablesList.Add("PersonCards");
                    application.SaveToXml(path, package, null);
                }

                var loaded = new PackageLoader().Load(path);
                Require(loaded.Succeeded, "transfer-task.reload");
                try
                {
                    var host = (TaskHost)loaded.Session.Package.Executables[0];
                    var transfer = (ITransferSqlServerObjectsTask)host.InnerObject;
                    Require(!transfer.CopyAllObjects && !transfer.CopyAllTables &&
                        transfer.TablesList.Cast<string>().SequenceEqual(new[] { "PersonCards" }),
                        "transfer-task.tables.persisted");
                    var cli = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SsisAiRuntime.Cli.exe");
                    var inventory = Program.RunExecutable(cli,
                        new[] { "ai", "task.properties", path, "--task", host.ID }, 0);
                    var tableProperty = ((JArray)inventory["results"]["properties"])
                        .Single(item => (string)item["name"] == "TablesList");
                    var viewProperty = ((JArray)inventory["results"]["properties"])
                        .Single(item => (string)item["name"] == "ViewsList");
                    Require((string)tableProperty["specializedOperation"] == "update_transfer_tables" &&
                        (string)viewProperty["mutationAction"] == "update_task_collection",
                        "transfer-task.property-discovery");
                    var existingDataProperty = ((JArray)inventory["results"]["properties"])
                        .Single(item => (string)item["name"] == "ExistingData");
                    Require((string)existingDataProperty["valueKind"] == "Enum" &&
                        ((JArray)existingDataProperty["enumValues"]).Any(value => (string)value == "Append") &&
                        ((JArray)existingDataProperty["enumValues"]).Any(value => (string)value == "Replace"),
                        "transfer-task.enum-discovery");
                    var enumRequest = new JObject
                    {
                        ["schemaVersion"] = "2.0", ["operation"] = "set_task_property",
                        ["target"] = new JObject { ["kind"] = "TaskHost", ["nativeId"] = host.ID },
                        ["request"] = new JObject { ["propertyName"] = "ExistingData", ["value"] = "Append" },
                        ["sourceArtifactHash"] = Hash(path)
                    };
                    var enumPreview = Program.RunExecutableWithInput(cli,
                        new[] { "ai", "mutation.preview", path }, enumRequest.ToString(), 0);
                    Require((bool)enumPreview["succeeded"] &&
                        (string)enumPreview["preview"]["resultingValue"] == "Append",
                        "transfer-task.enum-name-preview");
                    var request = new JObject
                    {
                        ["schemaVersion"] = "2.0",
                        ["operation"] = "update_transfer_tables",
                        ["target"] = new JObject { ["kind"] = "TaskHost", ["nativeId"] = host.ID },
                        ["request"] = new JObject
                        {
                            ["mode"] = "Add",
                            ["tableNames"] = new JArray("ODS_APPLICANTS", "SPT_CS_ACYR", "ODS_FA_TERM_AWARDS", "PersonCards")
                        },
                        ["sourceArtifactHash"] = Hash(path)
                    };
                    var preview = Program.RunExecutableWithInput(cli,
                        new[] { "ai", "mutation.preview", path }, request.ToString(), 0);
                    Require((bool)preview["succeeded"] &&
                        (string)preview["preview"]["operation"] == "UpdateTransferTables" &&
                        ((JArray)preview["preview"]["resultingTables"]).Select(item => (string)item)
                            .SequenceEqual(new[] { "PersonCards", "ODS_APPLICANTS", "SPT_CS_ACYR", "ODS_FA_TERM_AWARDS" }) &&
                        !(bool)preview["packageExecuted"] && !(bool)preview["nativeValidationPerformed"],
                        "transfer-task.cli.preview");
                    var collectionRequest = new JObject
                    {
                        ["schemaVersion"] = "2.0", ["operation"] = "update_task_collection",
                        ["target"] = new JObject { ["kind"] = "TaskHost", ["nativeId"] = host.ID },
                        ["request"] = new JObject
                        {
                            ["propertyName"] = "ViewsList", ["mode"] = "Replace",
                            ["values"] = new JArray("PersonCardsView")
                        },
                        ["sourceArtifactHash"] = Hash(path)
                    };
                    var collectionPreview = Program.RunExecutableWithInput(cli,
                        new[] { "ai", "mutation.preview", path }, collectionRequest.ToString(), 0);
                    Require((bool)collectionPreview["succeeded"] &&
                        (string)collectionPreview["preview"]["operation"] == "TaskPropertyMutation" &&
                        ((JArray)collectionPreview["preview"]["resultingCollection"])
                            .Any(value => (string)value == "PersonCardsView"),
                        "transfer-task.generic-collection-preview");
                }
                finally { loaded.Session.Package.Dispose(); }
                passedCases++;
                Console.WriteLine("Transfer task native API: PASS; TablesList save/reload; no database connection or package validation/execution.");
            }
            finally { if (Directory.Exists(directory)) { Directory.Delete(directory, true); } }
        }

        private static void VerifyTransferTableUpdateModes()
        {
            currentCase = "transfer-table-update-modes";
            const string sourceHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            using (var package = new Package { Name = "TransferTableModes", ProtectionLevel = DTSProtectionLevel.DontSaveSensitive })
            {
                var application = new Application();
                var creationName = application.TaskInfos.Cast<TaskInfo>()
                    .Single(info => info.Name == "Transfer SQL Server Objects Task").CreationName;
                var host = (TaskHost)package.Executables.Add(creationName);
                host.Name = "CopySelectedObjects";
                var transfer = (ITransferSqlServerObjectsTask)host.InnerObject;
                transfer.CopyAllObjects = false;
                transfer.CopyAllTables = false;
                transfer.TablesList.Clear();
                transfer.TablesList.Add("PersonCards");

                var viewsAdd = TaskHostPropertyMutationPlan.CreateStringCollection(package, sourceHash, host.ID,
                    "ViewsList", TaskStringCollectionEditMode.Add, new[] { "PersonCardsView" });
                viewsAdd.Apply(package);
                Require(viewsAdd.VerifyFailureCode(package).Length == 0 &&
                    transfer.ViewsList.Cast<string>().SequenceEqual(new[] { "PersonCardsView" }),
                    "transfer-collections.generic-add");
                transfer.CopyAllViews = true;
                var viewsReplace = TaskHostPropertyMutationPlan.CreateStringCollection(package, sourceHash, host.ID,
                    "ViewsList", TaskStringCollectionEditMode.Replace, new[] { "CurrentPersonCardsView" });
                var viewsPreview = JObject.Parse(viewsReplace.SerializePreviewContract());
                Require(((JArray)viewsPreview["ChangedFlags"]).Any(item =>
                    (string)item["Name"] == "CopyAllViews" && (bool)item["Before"] && !(bool)item["After"]),
                    "transfer-collections.preview-copy-all-delta");
                viewsReplace.Apply(package);
                Require(viewsReplace.VerifyFailureCode(package).Length == 0 && !transfer.CopyAllViews &&
                    transfer.ViewsList.Cast<string>().SequenceEqual(new[] { "CurrentPersonCardsView" }),
                    "transfer-collections.generic-replace-copy-all");

                var existingDataProperty = TaskHostPropertyMutationPlan.Inspect(package, host.ID).Properties
                    .Single(item => item.Name == "ExistingData");
                Require(existingDataProperty.ValueKind == "Enum" && existingDataProperty.EnumValues.Contains("Append") &&
                    existingDataProperty.EnumValues.Contains("Replace"), "transfer-task.enum-values-discovered");
                var enumPlan = TaskHostPropertyMutationPlan.CreateScalar(package, sourceHash, host.ID,
                    "ExistingData", TaskHostPropertyMutationPlan.ParsePropertyEnumValue(package, host.ID, "ExistingData", "Append"));
                Require(enumPlan.SafeResultingValue == "Append", "transfer-task.enum-name-preview");
                enumPlan.Apply(package);
                Require(enumPlan.VerifyFailureCode(package).Length == 0 && transfer.ExistingData.ToString() == "Append",
                    "transfer-task.enum-name-apply");

                var requested = new[] { "ODS_APPLICANTS", "SPT_CS_ACYR", "ODS_FA_TERM_AWARDS", "PersonCards" };
                transfer.CopyAllTables = true;
                var addWithCopyAllRefused = false;
                try
                {
                    TransferTablesMutationPlan.Create(package, sourceHash, host.ID,
                        TransferTableUpdateMode.Add, requested);
                }
                catch (InvalidOperationException error)
                { addWithCopyAllRefused = error.Message == "mutation.transfer.tables.copy_all_enabled_use_replace"; }
                Require(addWithCopyAllRefused, "transfer-tables.add.copy-all-refused");
                transfer.CopyAllTables = false;

                var add = TransferTablesMutationPlan.Create(package, sourceHash, host.ID,
                    TransferTableUpdateMode.Add, requested);
                Require(add.OriginalTables.SequenceEqual(new[] { "PersonCards" }) &&
                    add.ResultingTables.SequenceEqual(new[] { "PersonCards", "ODS_APPLICANTS", "SPT_CS_ACYR", "ODS_FA_TERM_AWARDS" }),
                    "transfer-tables.add.preview");
                var rebound = add.RebindCurrent(package, sourceHash);
                rebound.Apply(package);
                Require(rebound.VerifyFailureCode(package).Length == 0, "transfer-tables.add.verify");

                var remove = TransferTablesMutationPlan.Create(package, sourceHash, host.ID,
                    TransferTableUpdateMode.Remove, new[] { "SPT_CS_ACYR" });
                Require(remove.ResultingTables.SequenceEqual(new[] { "PersonCards", "ODS_APPLICANTS", "ODS_FA_TERM_AWARDS" }),
                    "transfer-tables.remove.preview");
                remove.Apply(package);
                Require(remove.VerifyFailureCode(package).Length == 0, "transfer-tables.remove.verify");

                transfer.CopyAllObjects = true;
                transfer.CopyAllTables = true;
                var replace = TransferTablesMutationPlan.Create(package, sourceHash, host.ID,
                    TransferTableUpdateMode.Replace, new[] { "PersonCards", "ODS_APPLICANTS" });
                Require(replace.ResultingTables.SequenceEqual(new[] { "PersonCards", "ODS_APPLICANTS" }) &&
                    !replace.ResultingCopyAllObjects && !replace.ResultingCopyAllTables,
                    "transfer-tables.replace.preview");
                replace.Apply(package);
                Require(replace.VerifyFailureCode(package).Length == 0 && transfer.TablesList.Cast<string>()
                    .SequenceEqual(new[] { "PersonCards", "ODS_APPLICANTS" }), "transfer-tables.replace.verify");
            }
            passedCases++;
            Console.WriteLine("Transfer task table update modes: PASS; replace/add/remove and catch-all handling; no database connection or package execution.");
        }

        private static void VerifyNativeTaskPropertySet()
        {
            currentCase = "native-task-property-set";
            var directory = Path.Combine(Path.GetTempPath(), "SsisTaskProperty-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "source.dtsx");
            var destinationPath = Path.Combine(directory, "output.dtsx");
            var checkpointDirectory = Path.Combine(directory, "checkpoints");
            try
            {
                using (var package = new Package { Name = "TaskPropertyFixture", ProtectionLevel = DTSProtectionLevel.DontSaveSensitive })
                {
                    var task = (TaskHost)package.Executables.Add("STOCK:PipelineTask");
                    task.Name = "PropertyTarget";
                    var property = task.Properties["DefaultBufferMaxRows"];
                    Require(property.Get && property.Set && property.Type == TypeCode.Int32,
                        "task-property.native-descriptor");
                    new Application().SaveToXml(path, package, null);
                }

                var loaded = new PackageLoader().Load(path);
                Require(loaded.Succeeded, "task-property.reload");
                string taskId;
                int original;
                try
                {
                    var task = (TaskHost)loaded.Session.Package.Executables[0];
                    taskId = task.ID;
                    original = (int)task.Properties["DefaultBufferMaxRows"].GetValue(task);
                }
                finally { loaded.Session.Package.Dispose(); }
                var cli = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SsisAiRuntime.Cli.exe");
                var inventory = Program.RunExecutable(cli,
                    new[] { "ai", "task.properties", path, "--task", taskId }, 0, 3, 4);
                Require((bool)inventory["succeeded"], "task-property.cli.describe.failed:" +
                    string.Join(",", ((JArray)inventory["diagnostics"]).Select(item => (string)item["code"])));
                var rowsProperty = ((JArray)inventory["results"]["properties"])
                    .Single(item => (string)item["name"] == "DefaultBufferMaxRows");
                Require((bool)rowsProperty["canRead"] && (bool)rowsProperty["canWrite"] &&
                    (string)rowsProperty["type"] == "Int32" && (int)rowsProperty["currentValue"] == original,
                    "task-property.cli.describe");
                var request = new JObject
                {
                    ["schemaVersion"] = "2.0", ["operation"] = "set_task_property",
                    ["target"] = new JObject { ["kind"] = "TaskHost", ["nativeId"] = taskId },
                    ["request"] = new JObject { ["propertyName"] = "DefaultBufferMaxRows", ["value"] = original + 1 },
                    ["sourceArtifactHash"] = Hash(path)
                };
                var preview = Program.RunExecutableWithInput(cli,
                    new[] { "ai", "mutation.preview", path }, request.ToString(), 0, 4);
                Require((bool)preview["succeeded"], "task-property.cli.preview.failed:" +
                    string.Join(",", ((JArray)preview["diagnostics"]).Select(item => (string)item["code"])));
                Require((bool)preview["succeeded"] &&
                    (string)preview["preview"]["operation"] == "TaskPropertyMutation" &&
                    (string)preview["preview"]["propertyName"] == "DefaultBufferMaxRows",
                    "task-property.cli.preview");
                var legacyRequest = (JObject)request.DeepClone();
                legacyRequest["schemaVersion"] = "1.0";
                var legacyResponse = Program.RunExecutableWithInput(cli,
                    new[] { "ai", "mutation.preview", path }, legacyRequest.ToString(), 2);
                Require(!(bool)legacyResponse["succeeded"] &&
                    (string)legacyResponse["schemaVersion"] == "2.0" &&
                    (string)legacyResponse["diagnostics"][0]["code"] == "ai.mutation.request.schema_unsupported",
                    "task-property.legacy-schema-rejected");
                var sourceHash = Hash(path);
                var apply = Program.RunExecutableWithInput(cli,
                    new[] { "ai", "mutation.apply", path, destinationPath, checkpointDirectory }, request.ToString(), 0, 4);
                Require((bool)apply["succeeded"], "task-property.cli.apply.failed:" +
                    string.Join(",", ((JArray)apply["diagnostics"]).Select(item => (string)item["code"])));
                Require((bool)apply["succeeded"] && (string)apply["result"]["validation"]["status"] == "Succeeded" &&
                    (string)apply["result"]["semanticVerification"]["status"] == "Succeeded" && Hash(path) == sourceHash &&
                    Directory.GetFiles(checkpointDirectory, "*.dtsx").Length == 1,
                    "task-property.cli.apply");
                var renameRequest = new JObject
                {
                    ["schemaVersion"] = "2.0",
                    ["operation"] = "rename_task",
                    ["target"] = new JObject { ["kind"] = "Executable", ["nativeId"] = taskId },
                    ["request"] = new JObject { ["proposedName"] = "RenamedPropertyTarget" },
                    ["sourceArtifactHash"] = sourceHash
                };
                var renamePreview = Program.RunExecutableWithInput(cli,
                    new[] { "ai", "mutation.preview", path }, renameRequest.ToString(), 0, 4);
                Require((bool)renamePreview["succeeded"] &&
                    (string)renamePreview["operation"] == "rename_task" &&
                    (string)renamePreview["result"]["adapter"]["kind"] == "ssis16.task-host-rename" &&
                    (string)renamePreview["preview"]["status"] == "Valid" &&
                    !renamePreview.ToString().Contains("RenamedPropertyTarget") &&
                    !renamePreview.ToString().Contains("PropertyTarget"),
                    "task-property.rename.cli.preview");
                var renameDestination = Path.Combine(directory, "renamed-task.dtsx");
                var renameCheckpoints = Path.Combine(directory, "rename-checkpoints");
                var renameApply = Program.RunExecutableWithInput(cli,
                    new[] { "ai", "mutation.apply", path, renameDestination, renameCheckpoints }, renameRequest.ToString(), 0, 4);
                Require((bool)renameApply["succeeded"] &&
                    (string)renameApply["result"]["mutation"]["outcome"] == "AppliedAndVerified" &&
                    (string)renameApply["result"]["validation"]["status"] == "Succeeded" &&
                    (string)renameApply["result"]["semanticVerification"]["status"] == "Succeeded" &&
                    (string)renameApply["result"]["rollback"]["status"] == "NotRequired" &&
                    Hash(path) == sourceHash && File.Exists(renameDestination),
                    "task-property.rename.cli.apply");
                var renamed = new PackageLoader().Load(renameDestination);
                Require(renamed.Succeeded, "task-property.rename.output-load");
                try
                {
                    Require(renamed.Session.Package.Executables.Count == 1 &&
                        ((TaskHost)renamed.Session.Package.Executables[0]).ID == taskId &&
                        ((TaskHost)renamed.Session.Package.Executables[0]).Name == "RenamedPropertyTarget",
                        "task-property.rename.invariants");
                }
                finally { renamed.Session.Package.Dispose(); }
                var refusedApply = Program.RunExecutableWithInput(cli,
                    new[] { "ai", "mutation.apply", path, destinationPath, checkpointDirectory }, request.ToString(), 4);
                Require(!(bool)refusedApply["succeeded"] &&
                    (string)refusedApply["result"]["mutation"]["outcome"] == "RejectedBeforeMutation" &&
                    (string)refusedApply["result"]["rollback"]["status"] == "NotRequired" &&
                    refusedApply["result"]["rollback"]["sourceRestored"].Type == JTokenType.Null &&
                    Hash(path) == sourceHash,
                    "task-property.cli.refusal-does-not-claim-restoration");
                var output = new PackageLoader().Load(destinationPath);
                Require(output.Succeeded, "task-property.output-load");
                try
                {
                    var task = (TaskHost)output.Session.Package.Executables[0];
                    Require((int)task.Properties["DefaultBufferMaxRows"].GetValue(task) == original + 1,
                        "task-property.value-persisted");
                }
                finally { output.Session.Package.Dispose(); }
            }
            finally { if (Directory.Exists(directory)) { Directory.Delete(directory, true); } }
            passedCases++;
            Console.WriteLine("Native task property API: PASS; typed scalar set and save/reload; no database connection or package execution.");
        }

        private static void VerifyTransferTablesHostLifecycle()
        {
            currentCase = "transfer-tables-host-lifecycle";
            var directory = Path.Combine(Path.GetTempPath(), "SsisTransferApply-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var sourcePath = Path.Combine(directory, "source.dtsx");
            var destinationPath = Path.Combine(directory, "output.dtsx");
            var checkpointPath = Path.Combine(directory, "checkpoint.dtsx");
            try
            {
                var application = new Application();
                var creationName = application.TaskInfos.Cast<TaskInfo>()
                    .Single(info => info.Name == "Transfer SQL Server Objects Task").CreationName;
                using (var package = new Package { Name = "TransferApplyFixture", ProtectionLevel = DTSProtectionLevel.DontSaveSensitive })
                {
                    var host = (TaskHost)package.Executables.Add(creationName);
                    host.Name = "CopySelectedObjects";
                    var transfer = (ITransferSqlServerObjectsTask)host.InnerObject;
                    transfer.CopyAllObjects = false;
                    transfer.CopyAllTables = false;
                    transfer.TablesList.Clear();
                    transfer.TablesList.Add("PersonCards");
                    application.SaveToXml(sourcePath, package, null);
                }

                var loaded = new PackageLoader().Load(sourcePath);
                Require(loaded.Succeeded, "transfer-host.source-load");
                TransferTablesMutationPlan plan;
                try
                {
                    var task = (TaskHost)loaded.Session.Package.Executables[0];
                    plan = TransferTablesMutationPlan.Create(loaded.Session.Package, Hash(sourcePath), task.ID,
                        TransferTableUpdateMode.Add, new[] { "ODS_APPLICANTS", "SPT_CS_ACYR", "ODS_FA_TERM_AWARDS" });
                }
                finally { loaded.Session.Package.Dispose(); }

                var checkpoint = new TestCheckpoint(checkpointPath);
                var validator = new TestValidator();
                var hostService = new Host(new PackageLoader(), new PackageAnalysisSnapshotFactory(), new CorpusSnapshotBuilder(),
                    new CorpusFingerprintProvider(), new MutationPreviewer(), checkpoint, validator, new MutationArtifactStager());
                var sourceHash = Hash(sourcePath);
                var result = hostService.ExecuteAsync(new MutationHostRequest(sourcePath, destinationPath, plan, sourceHash),
                    CancellationToken.None).GetAwaiter().GetResult();
                Require(result.Completed && checkpoint.Calls == 1 && validator.Calls == 1 && Hash(sourcePath) == sourceHash,
                    "transfer-host.lifecycle:" + result.Code + ":" + string.Join(",", result.DiagnosticCodes));
                var output = new PackageLoader().Load(destinationPath);
                Require(output.Succeeded, "transfer-host.output-load");
                try
                {
                    var task = (TaskHost)output.Session.Package.Executables[0];
                    var transfer = (ITransferSqlServerObjectsTask)task.InnerObject;
                    Require(plan.VerifyFailureCode(output.Session.Package).Length == 0 &&
                        transfer.TablesList.Cast<string>().SequenceEqual(new[]
                            { "PersonCards", "ODS_APPLICANTS", "SPT_CS_ACYR", "ODS_FA_TERM_AWARDS" }),
                        "transfer-host.output-verified");
                }
                finally { output.Session.Package.Dispose(); }
            }
            finally { if (Directory.Exists(directory)) { Directory.Delete(directory, true); } }
            passedCases++;
            Console.WriteLine("Transfer task host lifecycle: PASS; replace/add/remove table selection, checkpoint/save/reload/verify; no database connection or package execution.");
        }

        private static void VerifyDerivedColumnInsertionLifecycle()
        {
            currentCase = "derived-column-insertion-lifecycle";
            var directory = Path.Combine(Path.GetTempPath(), "SsisDerivedInsertion-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var sourcePath = Path.Combine(directory, "source.dtsx");
            var destinationPath = Path.Combine(directory, "output.dtsx");
            var checkpointPath = Path.Combine(directory, "checkpoint.dtsx");
            var inputDataPath = Path.Combine(directory, "input.csv");
            var outputDataPath = Path.Combine(directory, "output.csv");
            File.WriteAllText(inputDataPath, "42" + Environment.NewLine, System.Text.Encoding.Unicode);
            File.WriteAllText(outputDataPath, string.Empty, System.Text.Encoding.Unicode);
            string taskId;
            string sourceId;
            string destinationId;
            string pathId;
            int lineageId;
            try
            {
                using (var package = new Package { Name = "DerivedInsertionFixture", ProtectionLevel = DTSProtectionLevel.DontSaveSensitive })
                {
                    var task = (TaskHost)package.Executables.Add("STOCK:PipelineTask");
                    task.Name = "InsertFlow";
                    taskId = task.ID;
                    var pipeline = (IDTSPipeline130)task.InnerObject;
                    var app = new Application();
                    var inputConnection = CreateFlatFileConnection(package, inputDataPath, "InputValue", RuntimeWrapper.DataType.DT_I4, 4);
                    var outputConnection = CreateFlatFileConnection(package, outputDataPath, "InputValue", RuntimeWrapper.DataType.DT_I4, 4);
                    var source = pipeline.ComponentMetaDataCollection.New();
                    source.ComponentClassID = FindComponent(app, "DTSAdapter.FlatFileSource.");
                    var sourceDesign = source.Instantiate();
                    sourceDesign.ProvideComponentProperties();
                    source.Name = "Source";
                    source.LocaleID = 1033;
                    source.RuntimeConnectionCollection[0].ConnectionManagerID = inputConnection.ID;
                    source.RuntimeConnectionCollection[0].ConnectionManager = DtsConvert.GetExtendedInterface(inputConnection);
                    sourceDesign.AcquireConnections(null);
                    try { sourceDesign.ReinitializeMetaData(); }
                    finally { sourceDesign.ReleaseConnections(); }
                    var sourceErrorOutput = source.OutputCollection.Cast<IDTSOutput100>().Single(port => port.IsErrorOut);
                    var sourceErrorColumn = sourceErrorOutput.OutputColumnCollection.Cast<IDTSOutputColumn100>()
                        .Single(column => column.Name == "Flat File Source Error Output Column");
                    sourceErrorColumn.SetDataTypeProperties(RuntimeWrapper.DataType.DT_TEXT, 0, 0, 0, 1252);
                    var destination = pipeline.ComponentMetaDataCollection.New();
                    destination.ComponentClassID = FindComponent(app, "DTSAdapter.FlatFileDestination.");
                    var destinationDesign = destination.Instantiate();
                    destinationDesign.ProvideComponentProperties();
                    destination.Name = "Destination";
                    destination.LocaleID = 1033;
                    destination.RuntimeConnectionCollection[0].ConnectionManagerID = outputConnection.ID;
                    destination.RuntimeConnectionCollection[0].ConnectionManager = DtsConvert.GetExtendedInterface(outputConnection);
                    sourceId = source.ID.ToString();
                    destinationId = destination.ID.ToString();
                    var output = source.OutputCollection.Cast<IDTSOutput100>().First(port => !port.IsErrorOut);
                    var inputColumn = output.OutputColumnCollection.Cast<IDTSOutputColumn100>().Single();
                    lineageId = inputColumn.LineageID;
                    var input = destination.InputCollection[0];
                    pipeline.PathCollection.New().AttachPathAndPropagateNotifications(output, input);
                    destinationDesign.AcquireConnections(null);
                    try { destinationDesign.ReinitializeMetaData(); }
                    finally { destinationDesign.ReleaseConnections(); }
                    var selected = destinationDesign.SetUsageType(input.ID, input.GetVirtualInput(), inputColumn.LineageID, DTSUsageType.UT_READONLY);
                    var destinationExternal = input.ExternalMetadataColumnCollection.Cast<IDTSExternalMetadataColumn100>().Single();
                    destinationDesign.MapInputColumn(input.ID, selected.ID, destinationExternal.ID);
                    package.DesignTimeProperties = "<Objects Version='8'><Package design-time-name='Package'><LayoutInfo><GraphLayout>" +
                        "<NodeLayout Id='Package\\InsertFlow' TopLeft='20,20' Size='180,42'/>" +
                        "</GraphLayout></LayoutInfo></Package><TaskHost design-time-name='Package\\InsertFlow'><LayoutInfo><GraphLayout>" +
                        "<NodeLayout Id='Package\\InsertFlow\\Source' TopLeft='100,100' Size='114,42'/>" +
                        "<NodeLayout Id='Package\\InsertFlow\\Destination' TopLeft='400,100' Size='134,42'/>" +
                        "</GraphLayout></LayoutInfo></TaskHost></Objects>";
                    app.SaveToXml(sourcePath, package, null);
                }

                var sourceLoad = new PackageLoader().Load(sourcePath);
                Require(sourceLoad.Succeeded, "insert.source.load");
                try
                {
                    var sourcePackage = sourceLoad.Session.Package;
                    var sourceConnectionManagers = sourcePackage.Connections.Cast<ConnectionManager>().ToArray();
                    Require(sourceConnectionManagers.All(connection =>
                        ((RuntimeWrapper.IDTSConnectionManagerFlatFile100)connection.InnerObject).Unicode),
                        "insert.flatfile.unicode_reloaded");
                    var previousOfflineMode = sourcePackage.OfflineMode;
                    sourcePackage.OfflineMode = true;
                    sourcePackage.Validate(sourcePackage.Connections, sourcePackage.Variables, null, null);
                    Require(sourcePackage.Errors.Count == 0, "insert.source.native_validation");
                    sourcePackage.OfflineMode = previousOfflineMode;
                    var sourceTask = (TaskHost)sourceLoad.Session.Package.Executables[0];
                    Require(sourceLoad.Session.Package.Connections.Cast<ConnectionManager>().All(connection =>
                        ((RuntimeWrapper.IDTSConnectionManagerFlatFile100)connection.InnerObject).LocaleID == 1033),
                        "insert.flatfile.locale_reloaded");
                    taskId = sourceTask.ID;
                    var sourcePipeline = (IDTSPipeline130)sourceTask.InnerObject;
                    var sourceComponent = sourcePipeline.ComponentMetaDataCollection.Cast<IDTSComponentMetaData100>()
                        .Single(component => component.Name == "Source");
                    var destinationComponent = sourcePipeline.ComponentMetaDataCollection.Cast<IDTSComponentMetaData100>()
                        .Single(component => component.Name == "Destination");
                    sourceId = sourceComponent.ID.ToString();
                    destinationId = destinationComponent.ID.ToString();
                    pathId = sourcePipeline.PathCollection.Cast<IDTSPath100>().Single().ID.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    lineageId = sourceComponent.OutputCollection.Cast<IDTSOutput100>().First(port => !port.IsErrorOut)
                        .OutputColumnCollection.Cast<IDTSOutputColumn100>().Single(column => column.Name == "InputValue").LineageID;
                }
                finally { sourceLoad.Session.Package.Dispose(); }

                var sourceHash = Hash(sourcePath);
                var cli = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SsisAiRuntime.Cli.exe");
                var capabilities = Program.RunExecutable(cli, new[] { "ai", "mutation.capabilities" }, 0);
                Require((bool)capabilities["succeeded"] &&
                    ((JArray)capabilities["results"]["operations"]).Count == 9 &&
                    (string)capabilities["results"]["requestEnvelopeSchema"]["properties"]["schemaVersion"]["const"] == "2.0" &&
                    (string)capabilities["results"]["operations"][0]["targetKind"] == "DataFlowPath" &&
                    (string)capabilities["results"]["operations"][1]["targetKind"] == "DataFlowPath" &&
                    ((JArray)capabilities["results"]["operations"][0]["requiredInputs"]).Any(value => (string)value == "sourceLineageId") &&
                    ((JArray)capabilities["results"]["operations"][1]["requiredInputs"]).Any(value => (string)value == "sourceLineageId") &&
                    ((JArray)capabilities["results"]["operations"][1]["inputSchema"]["properties"]["outputDataType"]["enum"])
                        .Any(value => (string)value == "DT_WSTR") &&
                    (string)capabilities["results"]["operations"][2]["name"] == "update_transfer_tables" &&
                    ((JArray)capabilities["results"]["operations"][2]["inputSchema"]["properties"]["mode"]["enum"])
                        .Select(value => (string)value).SequenceEqual(new[] { "Replace", "Add", "Remove" }) &&
                    (string)capabilities["results"]["operations"][3]["name"] == "set_task_property" &&
                    (string)capabilities["results"]["operations"][4]["name"] == "update_task_collection" &&
                    (string)capabilities["results"]["operations"][5]["name"] == "rename_task" &&
                    (string)capabilities["results"]["operations"][5]["targetKind"] == "Executable" &&
                    ((JArray)capabilities["results"]["operations"][5]["actions"]).Any(value => (string)value == "apply") &&
                    (string)capabilities["results"]["operations"][6]["name"] == "set_variable_metadata" &&
                    (string)capabilities["results"]["operations"][6]["targetKind"] == "Variable" &&
                    ((JArray)capabilities["results"]["operations"][6]["requiredInputs"]).Values<string>()
                        .SequenceEqual(new[] { "description" }) &&
                    (string)capabilities["results"]["operations"][7]["name"] == "set_variable_expression" &&
                    (string)capabilities["results"]["operations"][7]["targetKind"] == "Variable" &&
                    ((JArray)capabilities["results"]["operations"][7]["requiredInputs"]).Values<string>()
                        .SequenceEqual(new[] { "expression" }) &&
                    (string)capabilities["results"]["operations"][8]["name"] == "set_variable_value" &&
                    (string)capabilities["results"]["operations"][8]["targetKind"] == "Variable" &&
                    ((JArray)capabilities["results"]["operations"][8]["requiredInputs"]).Values<string>()
                        .SequenceEqual(new[] { "environmentVariableName" }) &&
                    ((JArray)capabilities["results"]["operations"]).All(operation =>
                        operation["lifecycleRequirements"] != null &&
                        (bool)operation["lifecycleRequirements"]["checkpointRequired"] &&
                        (bool)operation["lifecycleRequirements"]["saveAsRequired"] &&
                        (bool)operation["lifecycleRequirements"]["reloadRequired"] &&
                        (bool)operation["lifecycleRequirements"]["validationRequired"] &&
                        (bool)operation["lifecycleRequirements"]["semanticDiffRequired"]) &&
                    (bool)capabilities["results"]["operations"][6]["lifecycleRequirements"]["checkpointRequired"] &&
                    (bool)capabilities["results"]["operations"][6]["lifecycleRequirements"]["semanticDiffRequired"] &&
                    ((JArray)capabilities["results"]["operations"][7]["lifecycleRequirements"]["requirements"])
                        .Values<string>().Contains("variable.expression.references_resolved") &&
                    ((JArray)capabilities["results"]["operations"][8]["limitations"]).Values<string>()
                        .Any(value => value.Contains("sensitivity is unclassified")) &&
                    ((JArray)capabilities["results"]["operations"][0]["inputSchema"]["properties"]["placement"]["properties"]["relation"]["enum"])
                        .Any(value => (string)value == "RightOf") &&
                    (bool)capabilities["results"]["applyAvailable"] &&
                    ((JArray)capabilities["results"]["operations"][0]["actions"]).Any(value => (string)value == "apply"),
                    "insert.ai.capabilities");
                var previewRequest = new JObject
                {
                    ["schemaVersion"] = "2.0",
                    ["operation"] = "insert_derived_column",
                    ["target"] = new JObject { ["kind"] = "DataFlowPath", ["nativeId"] = pathId, ["ownerNativeId"] = taskId },
                    ["request"] = new JObject
                    {
                        ["sourceComponentId"] = sourceId,
                        ["destinationComponentId"] = destinationId,
                        ["sourceLineageId"] = lineageId,
                        ["componentName"] = "AddCalculated",
                        ["outputName"] = "CalculatedValue",
                        ["expression"] = "#" + lineageId + " + 1",
                        ["placement"] = new JObject
                        {
                            ["relation"] = "RightOf", ["anchorComponentId"] = destinationId,
                            ["width"] = 140, ["height"] = 42
                        }
                    },
                    ["sourceArtifactHash"] = Hash(sourcePath)
                };
                var cliPreview = Program.RunExecutableWithInput(cli,
                    new[] { "ai", "mutation.preview", sourcePath }, previewRequest.ToString(), 0);
                Require((bool)cliPreview["succeeded"], "insert.ai.preview.success");
                Require((string)cliPreview["preview"]["operation"] == "InsertDerivedColumn",
                    "insert.ai.preview.operation");
                Require(!cliPreview.ToString().Contains("#" + lineageId + " + 1"), "insert.ai.preview.expression_redacted");
                Require(!(bool)cliPreview["packageExecuted"] && !(bool)cliPreview["nativeValidationPerformed"],
                    "insert.ai.preview.non_executing");
                Require(Hash(sourcePath) == sourceHash, "insert.ai.preview.source_preserved");
                var cliDestination = Path.Combine(directory, "cli-output.dtsx");
                var cliCheckpointDirectory = Path.Combine(directory, "cli-checkpoints");
                var cliApply = Program.RunExecutableWithInput(cli,
                    new[] { "ai", "mutation.apply", sourcePath, cliDestination, cliCheckpointDirectory },
                    previewRequest.ToString(), 0);
                Require((bool)cliApply["succeeded"] && (int)cliApply["exitCode"] == 0 &&
                    (string)cliApply["result"]["mutation"]["diagnosticCode"] == "mutation.completed" &&
                    (bool)cliApply["nativeValidationPerformed"] &&
                    (string)cliApply["result"]["validation"]["status"] == "Succeeded" &&
                    (string)cliApply["result"]["semanticVerification"]["status"] == "Succeeded" &&
                    (string)cliApply["result"]["layoutVerification"]["status"] == "Succeeded" &&
                    (string)cliApply["result"]["artifact"]["status"] == "Succeeded" &&
                    (string)cliApply["result"]["reload"]["status"] == "Succeeded" && File.Exists(cliDestination),
                    "insert.ai.apply.completed");
                Require(((JArray)cliApply["diagnostics"]).Count == 0 &&
                    Directory.GetFiles(cliCheckpointDirectory, "*.dtsx").Length == 1 && Hash(sourcePath) == sourceHash &&
                    (string)cliApply["result"]["artifact"]["destinationArtifactHash"] == Hash(cliDestination),
                    "insert.ai.apply.checkpoint_and_source_preserved");
                var checkpoint = new TestCheckpoint(checkpointPath);
                var validator = new TestValidator();
                var host = new Host(new PackageLoader(), new PackageAnalysisSnapshotFactory(), new CorpusSnapshotBuilder(),
                    new CorpusFingerprintProvider(), new MutationPreviewer(), checkpoint, validator, new MutationArtifactStager());
                var plan = host.PreviewDerivedColumnInsertion(sourcePath, taskId, sourceId, destinationId, lineageId,
                    "AddCalculated", "CalculatedValue", "#" + lineageId + " + 1",
                    new ComponentPlacementIntent(taskId, ComponentPlacementRelation.RightOf, destinationId, 140, 42));
                var previewJson = plan.SerializePreviewContract();
                Require(previewJson == plan.SerializePreviewContract() &&
                    previewJson.Contains("InsertDerivedColumn") && previewJson.Contains("RightOf") &&
                    previewJson.Contains("PathLayoutId") && !previewJson.Contains("PathNativeId") &&
                    previewJson.Contains("Package\\\\InsertFlow\\\\Destination") &&
                    !previewJson.Contains("#" + lineageId + " + 1") &&
                    !previewJson.Contains("sourceNativeId") && !previewJson.Contains("destinationNativeId"),
                    "insert.preview_contract.stable_and_redacted");
                Require(!File.Exists(destinationPath), "insert.preview_no_publication");
                var result = host.ExecuteAsync(new MutationHostRequest(sourcePath, destinationPath, plan, sourceHash),
                    CancellationToken.None).GetAwaiter().GetResult();
                Require(result.Completed && result.CheckpointCreated && result.SaveAsCompleted && result.ReloadSucceeded &&
                    result.ValidationSucceeded && result.SemanticDiffMatched, "insert.lifecycle.completed:" + result.Code + ":" +
                    string.Join(",", result.DiagnosticCodes));
                Require(checkpoint.Calls == 1 && validator.Calls == 1 && Hash(sourcePath) == sourceHash &&
                    result.DestinationArtifactHash == Hash(destinationPath), "insert.lifecycle.safety_evidence");
                var loaded = new PackageLoader().Load(destinationPath);
                Require(loaded.Succeeded, "insert.destination.load");
                try { Require(plan.Verify(loaded.Session.Package), "insert.destination.verify"); }
                finally { loaded.Session.Package.Dispose(); }
                var connectionCheck = new PackageLoader().Load(destinationPath);
                Require(connectionCheck.Succeeded, "insert.connection.reload");
                try { Require(connectionCheck.Session.Package.Connections.Count == 2, "insert.connection.preserved"); }
                finally { connectionCheck.Session.Package.Dispose(); }
                passedCases++;
                Console.WriteLine("Mutation host derived-column insertion: PASS; checkpoint/save-as/reload/layout verification with validator double; no data-source connection or package execution.");
            }
            finally
            {
                if (Directory.Exists(directory)) { Directory.Delete(directory, true); }
            }
        }

        private static void VerifyDataConversionInsertionLifecycle()
        {
            currentCase = "data-conversion-insertion-lifecycle";
            var directory = Path.Combine(Path.GetTempPath(), "SsisDataConversionInsertion-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var sourcePath = Path.Combine(directory, "source.dtsx");
            var destinationPath = Path.Combine(directory, "output.dtsx");
            var checkpointPath = Path.Combine(directory, "checkpoint.dtsx");
            string taskId;
            string sourceId;
            string destinationId;
            string pathId;
            int lineageId;
            try
            {
                using (var package = new Package { Name = "DataConversionInsertionFixture", ProtectionLevel = DTSProtectionLevel.DontSaveSensitive })
                {
                    var task = (TaskHost)package.Executables.Add("STOCK:PipelineTask");
                    task.Name = "ConvertFlow";
                    taskId = task.ID;
                    var pipeline = (IDTSPipeline130)task.InnerObject;
                    var application = new Application();
                    var connection = package.Connections.Add("OLEDB");
                    connection.Name = "OfflineTestDb";
                    connection.ConnectionString = "Provider=SQLOLEDB;Data Source=ssis-runtime-offline.invalid;Initial Catalog=TestDB;Integrated Security=SSPI;";
                    var source = AddComponent(pipeline, application, "DTSAdapter.OleDbSource.", "Source");
                    var destination = AddComponent(pipeline, application, "DTSAdapter.OleDbDestination.", "Destination");
                    source.RuntimeConnectionCollection[0].ConnectionManagerID = connection.ID;
                    source.RuntimeConnectionCollection[0].ConnectionManager = DtsConvert.GetExtendedInterface(connection);
                    destination.RuntimeConnectionCollection[0].ConnectionManagerID = connection.ID;
                    destination.RuntimeConnectionCollection[0].ConnectionManager = DtsConvert.GetExtendedInterface(connection);
                    sourceId = source.ID.ToString();
                    destinationId = destination.ID.ToString();
                    var output = source.OutputCollection.Cast<IDTSOutput100>().First(port => !port.IsErrorOut);
                    var sourceColumn = output.OutputColumnCollection.New();
                    sourceColumn.Name = "IntegerValue";
                    sourceColumn.SetDataTypeProperties(RuntimeWrapper.DataType.DT_I4, 0, 0, 0, 0);
                    lineageId = sourceColumn.LineageID;
                    pipeline.PathCollection.New().AttachPathAndPropagateNotifications(output, destination.InputCollection[0]);
                    package.DesignTimeProperties = "<Objects Version='8'><Package design-time-name='Package'><LayoutInfo><GraphLayout>" +
                        "<NodeLayout Id='Package\\ConvertFlow' TopLeft='20,20' Size='180,42'/>" +
                        "</GraphLayout></LayoutInfo></Package><TaskHost design-time-name='Package\\ConvertFlow'><LayoutInfo><GraphLayout>" +
                        "<NodeLayout Id='Package\\ConvertFlow\\Source' TopLeft='100,100' Size='114,42'/>" +
                        "<NodeLayout Id='Package\\ConvertFlow\\Destination' TopLeft='400,100' Size='134,42'/>" +
                        "</GraphLayout></LayoutInfo></TaskHost></Objects>";
                    application.SaveToXml(sourcePath, package, null);
                }

                var loaded = new PackageLoader().Load(sourcePath);
                Require(loaded.Succeeded, "dataconvert.source.load");
                try
                {
                    var task = (TaskHost)loaded.Session.Package.Executables[0];
                    taskId = task.ID;
                    var pipeline = (IDTSPipeline130)task.InnerObject;
                    var source = pipeline.ComponentMetaDataCollection.Cast<IDTSComponentMetaData100>().Single(component => component.Name == "Source");
                    var destination = pipeline.ComponentMetaDataCollection.Cast<IDTSComponentMetaData100>().Single(component => component.Name == "Destination");
                    sourceId = source.ID.ToString();
                    destinationId = destination.ID.ToString();
                    pathId = pipeline.PathCollection.Cast<IDTSPath100>().Single().ID.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    lineageId = source.OutputCollection.Cast<IDTSOutput100>().First(port => !port.IsErrorOut)
                        .OutputColumnCollection.Cast<IDTSOutputColumn100>().Single(column => column.Name == "IntegerValue").LineageID;
                }
                finally { loaded.Session.Package.Dispose(); }

                var hash = Hash(sourcePath);
                var cli = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SsisAiRuntime.Cli.exe");
                var previewRequest = new JObject
                {
                    ["schemaVersion"] = "2.0",
                    ["operation"] = "insert_data_conversion",
                    ["target"] = new JObject { ["kind"] = "DataFlowPath", ["nativeId"] = pathId, ["ownerNativeId"] = taskId },
                    ["request"] = new JObject
                    {
                        ["sourceComponentId"] = sourceId,
                        ["destinationComponentId"] = destinationId,
                        ["sourceLineageId"] = lineageId,
                        ["componentName"] = "ConvertInteger",
                        ["outputName"] = "IntegerText",
                        ["outputDataType"] = "DT_WSTR",
                        ["length"] = 32,
                        ["placement"] = new JObject
                        {
                            ["relation"] = "RightOf", ["anchorComponentId"] = destinationId,
                            ["width"] = 140, ["height"] = 42
                        }
                    },
                    ["sourceArtifactHash"] = Hash(sourcePath)
                };
                var cliPreview = Program.RunExecutableWithInput(cli,
                    new[] { "ai", "mutation.preview", sourcePath }, previewRequest.ToString(), 0);
                Require((bool)cliPreview["succeeded"] &&
                    (string)cliPreview["preview"]["operation"] == "InsertDataConversion" &&
                    (string)cliPreview["preview"]["outputDataType"] == "DT_WSTR" &&
                    !(bool)cliPreview["packageExecuted"] && !(bool)cliPreview["nativeValidationPerformed"],
                    "dataconvert.ai.preview");
                Require(Hash(sourcePath) == hash, "dataconvert.ai.preview.source_preserved");
                var checkpoint = new TestCheckpoint(checkpointPath);
                var validator = new TestValidator();
                var host = new Host(new PackageLoader(), new PackageAnalysisSnapshotFactory(), new CorpusSnapshotBuilder(),
                    new CorpusFingerprintProvider(), new MutationPreviewer(), checkpoint, validator, new MutationArtifactStager());
                var plan = host.PreviewDataConversionInsertion(sourcePath, taskId, sourceId, destinationId, lineageId,
                    "ConvertInteger", "IntegerText", RuntimeWrapper.DataType.DT_WSTR, 32, 0, 0, 0,
                    new ComponentPlacementIntent(taskId, ComponentPlacementRelation.RightOf, destinationId, 140, 42));
                var preview = plan.SerializePreviewContract();
                Require(preview.Contains("InsertDataConversion") && preview.Contains("DT_WSTR") &&
                    preview.Contains("RightOf") && preview.Contains("PathLayoutId") &&
                    !preview.Contains("PathNativeId") && !preview.Contains("sourceNativeId") &&
                    !preview.Contains("destinationNativeId"), "dataconvert.preview_contract");
                var result = host.ExecuteAsync(new MutationHostRequest(sourcePath, destinationPath, plan, hash),
                    CancellationToken.None).GetAwaiter().GetResult();
                Require(result.Completed && result.CheckpointCreated && result.SaveAsCompleted && result.ReloadSucceeded &&
                    result.ValidationSucceeded && result.SemanticDiffMatched, "dataconvert.lifecycle:" + result.Code + ":" +
                    string.Join(",", result.DiagnosticCodes));
                Require(checkpoint.Calls == 1 && validator.Calls == 1 && Hash(sourcePath) == hash &&
                    result.DestinationArtifactHash == Hash(destinationPath), "dataconvert.safety_evidence");
                passedCases++;
                Console.WriteLine("Mutation host Data Conversion insertion: PASS; positioned DT_WSTR conversion, source mapping, checkpoint/save-as/reload/verification with validator double; no data-source connection or package execution.");
            }
            finally
            {
                if (Directory.Exists(directory)) { Directory.Delete(directory, true); }
            }
        }

        private static IDTSComponentMetaData100 AddComponent(IDTSPipeline130 pipeline, Application application,
            string creationNamePrefix, string name)
        {
            var info = application.PipelineComponentInfos.Cast<PipelineComponentInfo>()
                .Single(item => item.CreationName.StartsWith(creationNamePrefix, StringComparison.OrdinalIgnoreCase));
            var component = pipeline.ComponentMetaDataCollection.New();
            component.ComponentClassID = info.CreationName;
            component.Instantiate().ProvideComponentProperties();
            component.Name = name;
            return component;
        }

        private static string FindComponent(Application application, string creationNamePrefix) =>
            application.PipelineComponentInfos.Cast<PipelineComponentInfo>()
                .Single(item => item.CreationName.StartsWith(creationNamePrefix, StringComparison.OrdinalIgnoreCase))
                .CreationName;

        private static ConnectionManager CreateFlatFileConnection(Package package, string path, string columnName,
            RuntimeWrapper.DataType dataType, int maximumWidth)
        {
            var connection = package.Connections.Add("FLATFILE");
            connection.ConnectionString = path;
            var file = (RuntimeWrapper.IDTSConnectionManagerFlatFile100)connection.InnerObject;
            file.Format = "Delimited";
            file.Unicode = true;
            file.LocaleID = 1033;
            file.ColumnNamesInFirstDataRow = false;
            var column = file.Columns.Add();
            column.ColumnType = "Delimited";
            column.ColumnDelimiter = "\r\n";
            column.DataType = dataType;
            column.MaximumWidth = maximumWidth;
            ((RuntimeWrapper.IDTSName100)column).Name = columnName;
            return connection;
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

        internal sealed class TestCheckpoint : IMutationCheckpointService
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

        internal sealed class TestValidator : INativeMutationValidator
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