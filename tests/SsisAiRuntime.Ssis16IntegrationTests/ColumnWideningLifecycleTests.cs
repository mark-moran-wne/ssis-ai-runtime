using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Xml.Linq;
using Microsoft.SqlServer.Dts.Pipeline.Wrapper;
using Microsoft.SqlServer.Dts.Runtime;
using SsisAiRuntime.FlowRunner;
using SsisAiRuntime.Corpus;
using SsisAiRuntime.Mutations;
using SsisAiRuntime.MutationHost;
using SsisAiRuntime.Ssis16;
using Host = SsisAiRuntime.MutationHost.MutationHost;
using RuntimeWrapper = Microsoft.SqlServer.Dts.Runtime.Wrapper;

namespace SsisAiRuntime.Ssis16IntegrationTests
{
    internal static class ColumnWideningLifecycleTests
    {
        private static string currentCase;

        public static int Run()
        {
            try
            {
                Case("success", fixture =>
                {
                    var plan = fixture.Preview();
                    Require(plan.Changes.Count == 6 && plan.Changes.All(change => change.CurrentWidth == 4 && change.ProposedWidth == 64), "preview.widths");
                    var result = fixture.Execute(plan);
                    Require(result.Completed, "success:" + result.Code + ":" + string.Join(",", result.DiagnosticCodes));
                    var loaded = new PackageLoader().Load(fixture.Destination);
                    Require(loaded.Succeeded, "success.reload");
                    try
                    {
                        var pipeline = (IDTSPipeline130)((TaskHost)loaded.Session.Package.Executables[0]).InnerObject;
                        var input = pipeline.ComponentMetaDataCollection.Cast<IDTSComponentMetaData100>()
                            .Single(component => component.ID == fixture.DestinationId).InputCollection[0].InputColumnCollection[0];
                        Require(input.ID == fixture.InputColumnId && input.Length == 64, "success.input_identity");
                        loaded.Session.Package.SaveToXML(out var afterXml, null);
                        RequireOnlyWidthChanges(fixture.OriginalXml, afterXml);
                    }
                    finally { loaded.Session.Package.Dispose(); }
                    Require(Hash(fixture.CheckpointPath) == plan.SourceArtifactHash, "checkpoint.artifact");
                });
                Case("stale-source", fixture =>
                {
                    var plan = fixture.Preview();
                    fixture.ChangePackage(package => package.Name = "ChangedAfterPreview");
                    var result = fixture.Execute(plan);
                    Require(!result.Completed && result.Code == "mutation.source.hash_changed", "stale.refusal");
                });
                Case("stale-width", fixture =>
                {
                    var plan = fixture.Preview();
                    fixture.ChangePackage(package => ((RuntimeWrapper.IDTSConnectionManagerFlatFile100)package.Connections[0].InnerObject)
                        .Columns[0].MaximumWidth = 8);
                    Require(!fixture.Execute(plan).Completed, "width.stale_refusal");
                });
                Case("shrinking-refused", fixture => fixture.PreviewRefused(2));
                Case("missing-id", fixture => fixture.PreviewRefused(64, missingColumn: true));
                Case("existing-destination", fixture =>
                {
                    var plan = fixture.Preview();
                    File.WriteAllText(fixture.Destination, "existing");
                    var result = fixture.Execute(plan);
                    Require(!result.Completed && File.ReadAllText(fixture.Destination) == "existing", "existing.preserved");
                });
                Case("checkpoint-failure", fixture =>
                {
                    fixture.Checkpoint.Fail = true;
                    Require(!fixture.Execute(fixture.Preview()).Completed && fixture.Validator.Calls == 0, "checkpoint.refused");
                });
                Case("validator-mutation", fixture =>
                {
                    fixture.Validator.AlterPackageName = true;
                    var result = fixture.Execute(fixture.Preview());
                    Require(!result.Completed && result.DiagnosticCodes.Contains("mutation.width.unexpected_change"), "diff.refused");
                });
                Case("branched-flow", fixture =>
                {
                    fixture.ChangePackage(package =>
                    {
                        var pipeline = (IDTSPipeline130)((TaskHost)package.Executables[0]).InnerObject;
                        var source = pipeline.ComponentMetaDataCollection.Cast<IDTSComponentMetaData100>().Single(component => component.ID == fixture.SourceId);
                        var destination = pipeline.ComponentMetaDataCollection.Cast<IDTSComponentMetaData100>().Single(component => component.ID == fixture.DestinationId);
                        var originalPath = pipeline.PathCollection[0];
                        pipeline.PathCollection.RemoveObjectByID(originalPath.ID);
                        var multicast = pipeline.ComponentMetaDataCollection.New();
                        multicast.ComponentClassID = Find("DTSTransform.Multicast.");
                        multicast.Instantiate().ProvideComponentProperties();
                        pipeline.PathCollection.New().AttachPathAndPropagateNotifications(source.OutputCollection[0], multicast.InputCollection[0]);
                        pipeline.PathCollection.New().AttachPathAndPropagateNotifications(multicast.OutputCollection[0], destination.InputCollection[0]);
                        var branch = pipeline.ComponentMetaDataCollection.New();
                        branch.ComponentClassID = Find("DTSAdapter.FlatFileDestination.");
                        branch.Instantiate().ProvideComponentProperties();
                        var branchOutput = multicast.OutputCollection.New();
                        branchOutput.Name = "Branch";
                        branchOutput.SynchronousInputID = multicast.InputCollection[0].ID;
                        pipeline.PathCollection.New().AttachPathAndPropagateNotifications(branchOutput, branch.InputCollection[0]);
                    });
                    fixture.PreviewRefused(64);
                });
                Case("incompatible-type", fixture =>
                {
                    fixture.ChangePackage(package =>
                    {
                        var pipeline = (IDTSPipeline130)((TaskHost)package.Executables[0]).InnerObject;
                        var destination = pipeline.ComponentMetaDataCollection.Cast<IDTSComponentMetaData100>().Single(component => component.ID == fixture.DestinationId);
                        destination.InputCollection[0].ExternalMetadataColumnCollection[0].DataType = RuntimeWrapper.DataType.DT_I4;
                    });
                    fixture.PreviewRefused(64);
                });
                Console.WriteLine("Column widening lifecycle: PASS; 10 cases, native copy save/reload, exact IDs/mappings, stale-state/type/topology refusal, checkpoint/diff rejection; no DDL, Validate or Execute.");
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("Column widening lifecycle failed at " + currentCase + ": " + error.Message);
                return 1;
            }
        }

        private static void Case(string name, Action<Fixture> test)
        {
            currentCase = name;
            using (var fixture = new Fixture())
            {
                test(fixture);
                Require(Hash(fixture.Source) == fixture.CurrentHash, "source.unchanged_by_execution");
                Require(Directory.GetFiles(fixture.DirectoryPath, ".ssis-mutation-*.dtsx").Length == 0, "staging.cleaned");
                if (name != "success" && name != "existing-destination") { Require(!File.Exists(fixture.Destination), "not.published"); }
            }
            Console.WriteLine("Column widening " + name + ": PASS.");
        }

        private sealed class Fixture : IDisposable
        {
            public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "SsisWidening-" + Guid.NewGuid().ToString("N"));
            public string Source { get; }
            public string Destination { get; }
            public string CheckpointPath { get; }
            public string CurrentHash { get; private set; }
            public string TaskId { get; }
            public int SourceId { get; }
            public int ColumnId { get; }
            public int DestinationId { get; }
            public int InputColumnId { get; }
            public string OriginalXml { get; }
            public MutationHostLifecycleTests.TestCheckpoint Checkpoint { get; }
            public MutationHostLifecycleTests.TestValidator Validator { get; } = new MutationHostLifecycleTests.TestValidator();
            private readonly Host host;

            public Fixture()
            {
                Directory.CreateDirectory(DirectoryPath);
                Source = Path.Combine(DirectoryPath, "source.dtsx");
                Destination = Path.Combine(DirectoryPath, "copy.dtsx");
                CheckpointPath = Path.Combine(DirectoryPath, "checkpoint.dtsx");
                using (var package = new Package { Name = "WidenFixture", ProtectionLevel = DTSProtectionLevel.DontSaveSensitive })
                {
                    var inputConnection = Connection(package, Path.Combine(DirectoryPath, "input.csv"));
                    var outputConnection = Connection(package, Path.Combine(DirectoryPath, "output.csv"));
                    var task = (TaskHost)package.Executables.Add("STOCK:PipelineTask");
                    TaskId = task.ID;
                    var pipeline = (IDTSPipeline130)task.InnerObject;
                    var source = pipeline.ComponentMetaDataCollection.New();
                    source.ComponentClassID = Find("DTSAdapter.FlatFileSource.");
                    source.Instantiate().ProvideComponentProperties();
                    source.Name = "Source";
                    SourceId = source.ID;
                    source.RuntimeConnectionCollection[0].ConnectionManagerID = inputConnection.ID;
                    source.RuntimeConnectionCollection[0].ConnectionManager = DtsConvert.GetExtendedInterface(inputConnection);
                    var output = source.OutputCollection.Cast<IDTSOutput100>().First(port => !port.IsErrorOut);
                    var external = output.ExternalMetadataColumnCollection.New();
                    external.Name = "Value"; external.DataType = RuntimeWrapper.DataType.DT_WSTR; external.Length = 4;
                    var column = output.OutputColumnCollection.New();
                    column.Name = "Value"; column.SetDataTypeProperties(RuntimeWrapper.DataType.DT_WSTR, 4, 0, 0, 0);
                    column.ExternalMetadataColumnID = external.ID;
                    ColumnId = column.ID;
                    var destination = pipeline.ComponentMetaDataCollection.New();
                    destination.ComponentClassID = Find("DTSAdapter.FlatFileDestination.");
                    var design = destination.Instantiate(); design.ProvideComponentProperties();
                    destination.Name = "Destination";
                    DestinationId = destination.ID;
                    destination.RuntimeConnectionCollection[0].ConnectionManagerID = outputConnection.ID;
                    destination.RuntimeConnectionCollection[0].ConnectionManager = DtsConvert.GetExtendedInterface(outputConnection);
                    var input = destination.InputCollection[0];
                    pipeline.PathCollection.New().AttachPathAndPropagateNotifications(output, input);
                    var destinationExternal = input.ExternalMetadataColumnCollection.New();
                    destinationExternal.Name = "Value"; destinationExternal.DataType = RuntimeWrapper.DataType.DT_WSTR; destinationExternal.Length = 4;
                    var selected = design.SetUsageType(input.ID, input.GetVirtualInput(), column.LineageID, DTSUsageType.UT_READONLY);
                    InputColumnId = selected.ID;
                    design.MapInputColumn(input.ID, selected.ID, destinationExternal.ID);
                    NativeFlatFileColumnEditor.Add(pipeline, inputConnection, source, destination, "Unchanged", 12, outputConnection);
                    new Application().SaveToXml(Source, package, null);
                }
                CurrentHash = Hash(Source);
                var loaded = new PackageLoader().Load(Source);
                Require(loaded.Succeeded, "fixture.native_selector_load");
                try
                {
                    var task = (TaskHost)loaded.Session.Package.Executables[0];
                    TaskId = task.ID;
                    var pipeline = (IDTSPipeline130)task.InnerObject;
                    var source = pipeline.ComponentMetaDataCollection.Cast<IDTSComponentMetaData100>().Single(component => component.Name == "Source");
                    var destination = pipeline.ComponentMetaDataCollection.Cast<IDTSComponentMetaData100>().Single(component => component.Name == "Destination");
                    SourceId = source.ID;
                    ColumnId = source.OutputCollection.Cast<IDTSOutput100>().First(port => !port.IsErrorOut)
                        .OutputColumnCollection.Cast<IDTSOutputColumn100>().Single(column => column.Name == "Value").ID;
                    DestinationId = destination.ID;
                    InputColumnId = destination.InputCollection[0].InputColumnCollection[0].ID;
                    loaded.Session.Package.SaveToXML(out var originalXml, null);
                    OriginalXml = originalXml;
                }
                finally { loaded.Session.Package.Dispose(); }
                Checkpoint = new MutationHostLifecycleTests.TestCheckpoint(CheckpointPath);
                host = new Host(new PackageLoader(), new PackageAnalysisSnapshotFactory(), new CorpusSnapshotBuilder(),
                    new CorpusFingerprintProvider(), new MutationPreviewer(), Checkpoint, Validator, new MutationArtifactStager());
            }

            public ColumnWideningPlan Preview(int width = 64) => host.PreviewColumnWidening(Source, TaskId, SourceId, ColumnId, DestinationId, width);
            public MutationHostResult Execute(ColumnWideningPlan plan) => host.ExecuteAsync(
                new MutationHostRequest(Source, Destination, plan, plan.SourceArtifactHash), CancellationToken.None).GetAwaiter().GetResult();
            public void PreviewRefused(int width, bool missingColumn = false)
            {
                var refused = false;
                try { host.PreviewColumnWidening(Source, TaskId, SourceId, missingColumn ? int.MaxValue : ColumnId, DestinationId, width); }
                catch (InvalidOperationException) { refused = true; }
                Require(refused && Checkpoint.Calls == 0, "preview.refused");
            }
            public void ChangePackage(Action<Package> change)
            {
                var loaded = new PackageLoader().Load(Source);
                Require(loaded.Succeeded, "fixture.change.load");
                try { change(loaded.Session.Package); new Application().SaveToXml(Source, loaded.Session.Package, null); }
                finally { loaded.Session.Package.Dispose(); }
                CurrentHash = Hash(Source);
            }
            public void Dispose() => Directory.Delete(DirectoryPath, true);
        }

        private static ConnectionManager Connection(Package package, string path)
        {
            var connection = package.Connections.Add("FLATFILE"); connection.ConnectionString = path;
            var file = (RuntimeWrapper.IDTSConnectionManagerFlatFile100)connection.InnerObject;
            file.Format = "Delimited"; file.Unicode = true; file.ColumnNamesInFirstDataRow = false;
            var column = file.Columns.Add(); column.ColumnType = "Delimited"; column.ColumnDelimiter = "\r\n";
            column.DataType = RuntimeWrapper.DataType.DT_WSTR; column.MaximumWidth = 4;
            ((RuntimeWrapper.IDTSName100)column).Name = "Value";
            return connection;
        }
        private static string Find(string prefix) => new Application().PipelineComponentInfos.Cast<PipelineComponentInfo>()
            .First(info => info.CreationName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).CreationName;
        private static void RequireOnlyWidthChanges(string beforeXml, string afterXml)
        {
            XNamespace dts = "www.microsoft.com/SqlServer/Dts";
            var before = XDocument.Parse(beforeXml);
            var after = XDocument.Parse(afterXml);
            before.Root.Attribute(dts + "VersionGUID")?.SetValue(string.Empty);
            after.Root.Attribute(dts + "VersionGUID")?.SetValue(string.Empty);
            var firstElements = before.Root.DescendantsAndSelf().ToArray();
            var nextElements = after.Root.DescendantsAndSelf().ToArray();
            Require(firstElements.Length == nextElements.Length, "success.xml_inventory");
            var changedWidths = 0;
            foreach (var pair in firstElements.Zip(nextElements, (first, next) => new { First = first, Next = next }))
            {
                foreach (var attribute in pair.First.Attributes())
                {
                    var nextAttribute = pair.Next.Attribute(attribute.Name);
                    if (nextAttribute == null || attribute.Value == nextAttribute.Value) { continue; }
                    if (attribute.Value == "4" && nextAttribute.Value == "64" &&
                        (attribute.Name.LocalName == "MaximumWidth" || attribute.Name.LocalName == "length" || attribute.Name.LocalName == "cachedLength") &&
                        ((string)pair.First.Attribute("name") == "Value" || (string)pair.First.Attribute("cachedName") == "Value" ||
                         (string)pair.First.Attribute(dts + "ObjectName") == "Value"))
                    {
                        changedWidths++;
                        nextAttribute.Value = attribute.Value;
                    }
                }
            }
            Require(changedWidths == 6 && XNode.DeepEquals(before, after), "success.only_six_width_attributes_changed");
        }
        private static string Hash(string path)
        {
            using (var algorithm = SHA256.Create()) using (var stream = File.OpenRead(path))
            { return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
        }
        private static void Require(bool condition, string message) { if (!condition) { throw new InvalidOperationException(message); } }
    }
}