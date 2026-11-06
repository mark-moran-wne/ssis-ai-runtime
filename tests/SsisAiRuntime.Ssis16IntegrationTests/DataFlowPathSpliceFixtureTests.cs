using System;
using System.IO;
using System.Linq;
using Microsoft.SqlServer.Dts.Pipeline.Wrapper;
using Microsoft.SqlServer.Dts.Runtime;
using SsisAiRuntime.MutationHost;
using RuntimeWrapper = Microsoft.SqlServer.Dts.Runtime.Wrapper;

namespace SsisAiRuntime.Ssis16IntegrationTests
{
    internal static class DataFlowPathSpliceFixtureTests
    {
        private static string currentFixture = "initialization";
        public static void Run()
        {
            try
            {
                currentFixture = "Branch";
                VerifyFixture("Branch.dtsx", "BranchFlow");
                currentFixture = "Merge";
                VerifyFixture("Merge.dtsx", "MergeFlow");
                currentFixture = "UnionAllRefreshProbe";
                currentFixture = "CrossedPaths";
                VerifyFixture("CrossedPaths.dtsx", "CrossedPathsFlow");
                currentFixture = "ConfigureFailureRollback";
                VerifyConfigureFailureRollback();
                currentFixture = "EndpointAdapterRejectionRollback";
                VerifyEndpointAdapterRejectionRollback();
                currentFixture = "NestedDataFlows";
                VerifyNestedPathRefusal();
            }
            catch (Exception exception)
            {
                var code = exception.Message;
                if (!code.StartsWith("mutation.path_splice_fixture.", StringComparison.Ordinal) &&
                    !code.StartsWith("mutation.insertion.", StringComparison.Ordinal))
                { code = exception.GetType().Name; }
                Console.Error.WriteLine("Exact-path fixture failed at " + currentFixture + " (" + code + ").");
                throw;
            }
            Console.WriteLine("Exact-path splice fixtures: PASS; Branch, Merge, CrossedPaths rewired one selected path and reloaded; configuration failure restored topology; Union All output conflict refused before mutation; NestedDataFlows refused absent task-scoped paths.");
        }

        private static void VerifyFixture(string fixtureName, string taskName)
        {
            currentFixture = fixtureName + ".load";
            var fixturePath = Path.Combine(FindRepositoryRoot(), "tests", "LayoutFixtures", fixtureName);
            var originalBytes = File.ReadAllBytes(fixturePath);
            var directory = Path.Combine(Path.GetTempPath(), "SsisPathSplice-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var roundTripPath = Path.Combine(directory, fixtureName);
            var replacedConnectionKey = string.Empty;
            string sourceName = null;
            string sourcePortName = null;
            string destinationName = null;
            string destinationPortName = null;
            try
            {
                using (var package = new Application().LoadPackage(fixturePath, null))
                {
                    var task = FindPipelineTask(package, taskName);
                    var pipeline = (IDTSPipeline130)task.InnerObject;
                    var paths = pipeline.PathCollection.Cast<IDTSPath100>().ToArray();
                    var selectedPath = taskName == "MergeFlow"
                        ? paths.Single(path => path.StartPoint.Component.Name == "Union All")
                        : paths[0];
                    var sourceOutput = (IDTSOutput100)selectedPath.StartPoint;
                    var destinationInput = (IDTSInput100)selectedPath.EndPoint;
                    sourceName = sourceOutput.Component.Name;
                    sourcePortName = sourceOutput.Name;
                    destinationName = destinationInput.Component.Name;
                    destinationPortName = destinationInput.Name;
                    var pathId = selectedPath.ID.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    var before = new DesignerLayoutExtractor().Extract(package, fixturePath);
                    var replacedConnection = before.Connections.Single(connection =>
                        connection.OwnerNativeId == task.ID && connection.NativeId == pathId);
                    replacedConnectionKey = replacedConnection.Key;
                    var componentCount = pipeline.ComponentMetaDataCollection.Count;
                    var pathCount = pipeline.PathCollection.Count;
                    var inserted = AddMulticast(pipeline, "PathSpliceProbe");

                    currentFixture = fixtureName + ".splice";
                    DataFlowPathSplicer.Splice(pipeline, pathId, inserted);

                    currentFixture = fixtureName + ".verify";
                    Require(pipeline.ComponentMetaDataCollection.Count == componentCount + 1, fixtureName + ".component_count");
                    Require(pipeline.PathCollection.Count == pathCount + 1, fixtureName + ".path_count");
                    VerifyChain(pipeline, sourceName, sourcePortName, inserted.Name, destinationName, destinationPortName,
                        fixtureName + ".immediate_chain");
                    var after = new DesignerLayoutExtractor().Extract(package);
                    var layoutComparison = new DesignerLayoutComparer().ComparePreservingExisting(before, after,
                        explicitlyReplacedConnectionKeys: new[] { replacedConnection.Key });
                    Require(layoutComparison.Matches, fixtureName + ".unselected_paths_preserved." +
                        string.Join(",", layoutComparison.Differences));
                    new Application().SaveToXml(roundTripPath, package, null);
                }

                currentFixture = fixtureName + ".reload";
                using (var reloaded = new Application().LoadPackage(roundTripPath, null))
                {
                    var reloadedTasks = FindPipelineTasks(reloaded).ToArray();
                    var task = reloadedTasks.SingleOrDefault(candidate => candidate.Name == taskName);
                    Require(task != null, fixtureName + ".reload_task");
                    var pipeline = (IDTSPipeline130)task.InnerObject;
                    Require(pipeline.ComponentMetaDataCollection.Cast<IDTSComponentMetaData100>()
                        .Count(component => component.Name == "PathSpliceProbe") == 1, fixtureName + ".reload_component");
                    Require(pipeline.PathCollection.Count == (fixtureName == "CrossedPaths.dtsx" ? 3 : 4),
                        fixtureName + ".reload_path_count");
                    VerifyChain(pipeline, sourceName, sourcePortName, "PathSpliceProbe", destinationName,
                        destinationPortName, fixtureName + ".reload_chain");
                    var afterReload = new DesignerLayoutExtractor().Extract(reloaded, roundTripPath);
                    var beforeReload = new Application().LoadPackage(fixturePath, null);
                    try
                    {
                        var before = new DesignerLayoutExtractor().Extract(beforeReload, fixturePath);
                        Require(new DesignerLayoutComparer().ComparePreservingExisting(before, afterReload,
                            explicitlyReplacedConnectionKeys: new[] { replacedConnectionKey }).Matches,
                            fixtureName + ".reload_unselected_paths_preserved");
                    }
                    finally { beforeReload.Dispose(); }
                }

                Require(File.ReadAllBytes(fixturePath).SequenceEqual(originalBytes), fixtureName + ".source_unchanged");
            }
            finally
            {
                if (Directory.Exists(directory)) { Directory.Delete(directory, true); }
            }
        }

        private static void VerifyNestedPathRefusal()
        {
            var fixturePath = Path.Combine(FindRepositoryRoot(), "tests", "LayoutFixtures", "NestedDataFlows.dtsx");
            var originalBytes = File.ReadAllBytes(fixturePath);
            using (var package = new Application().LoadPackage(fixturePath, null))
            {
                var tasks = FindPipelineTasks(package).ToArray();
                Require(tasks.Length == 2, "nested.task_count");
                Require(tasks.All(task => ((IDTSPipeline130)task.InnerObject).PathCollection.Count == 0), "nested.no_paths");
                var refused = false;
                try { DataFlowPathSplicer.FindPath((IDTSPipeline130)tasks[0].InnerObject, "1"); }
                catch (InvalidOperationException exception)
                { refused = exception.Message == "mutation.insertion.path_not_found_or_ambiguous"; }
                Require(refused, "nested.absent_path_refused");
                Require(tasks.All(task => ((IDTSPipeline130)task.InnerObject).PathCollection.Count == 0), "nested.no_cross_task_mutation");
            }
            Require(File.ReadAllBytes(fixturePath).SequenceEqual(originalBytes), "nested.source_unchanged");
        }

        private static void VerifyConfigureFailureRollback()
        {
            var fixturePath = Path.Combine(FindRepositoryRoot(), "tests", "LayoutFixtures", "Branch.dtsx");
            var originalBytes = File.ReadAllBytes(fixturePath);
            using (var package = new Application().LoadPackage(fixturePath, null))
            {
                var task = FindPipelineTask(package, "BranchFlow");
                var pipeline = (IDTSPipeline130)task.InnerObject;
                var originalPaths = pipeline.PathCollection.Cast<IDTSPath100>()
                    .Select(EndpointKey).OrderBy(key => key, StringComparer.Ordinal).ToArray();
                var originalComponentIds = pipeline.ComponentMetaDataCollection.Cast<IDTSComponentMetaData100>()
                    .Select(component => component.ID).OrderBy(id => id).ToArray();
                var selectedPathId = pipeline.PathCollection.Cast<IDTSPath100>().First().ID
                    .ToString(System.Globalization.CultureInfo.InvariantCulture);
                var inserted = AddMulticast(pipeline, "RejectedInsertion");
                var rejected = false;
                try
                {
                    DataFlowPathSplicer.Splice(pipeline, selectedPathId, inserted,
                        configureInput: _ => { throw new InvalidOperationException("test.adapter.reject"); });
                }
                catch (InvalidOperationException exception)
                {
                    rejected = exception.Message == "mutation.insertion.path_splice.component_configure_failed";
                }

                Require(rejected, "configure_failure.reported");
                var restoredPaths = pipeline.PathCollection.Cast<IDTSPath100>()
                    .Select(EndpointKey).OrderBy(key => key, StringComparer.Ordinal).ToArray();
                var restoredComponentIds = pipeline.ComponentMetaDataCollection.Cast<IDTSComponentMetaData100>()
                    .Select(component => component.ID).OrderBy(id => id).ToArray();
                Require(originalPaths.SequenceEqual(restoredPaths), "configure_failure.paths_restored");
                Require(originalComponentIds.SequenceEqual(restoredComponentIds), "configure_failure.component_removed");
            }
            Require(File.ReadAllBytes(fixturePath).SequenceEqual(originalBytes), "configure_failure.source_unchanged");
        }

        private static void VerifyEndpointAdapterRejectionRollback()
        {
            var directory = Path.Combine(Path.GetTempPath(), "SsisUnionAllRollback-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var sourcePath = Path.Combine(directory, "source.txt");
            File.WriteAllText(sourcePath, "42" + Environment.NewLine, System.Text.Encoding.ASCII);
            try
            {
                using (var package = new Package { Name = "UnionAllRollbackFixture", ProtectionLevel = DTSProtectionLevel.DontSaveSensitive })
                {
                    var task = (TaskHost)package.Executables.Add("STOCK:PipelineTask");
                    task.Name = "UnionAllFlow";
                    var pipeline = (IDTSPipeline130)task.InnerObject;
                    var application = new Application();
                    var connection = CreateFlatFileConnection(package, sourcePath);
                    var sourceA = AddFlatFileSource(pipeline, application, connection, "Source A");
                    var sourceB = AddFlatFileSource(pipeline, application, connection, "Source B");
                    var unionCreationName = application.PipelineComponentInfos.Cast<PipelineComponentInfo>()
                        .Single(info => info.CreationName.StartsWith("DTSTransform.UnionAll.", StringComparison.OrdinalIgnoreCase))
                        .CreationName;
                    var union = pipeline.ComponentMetaDataCollection.New();
                    union.ComponentClassID = unionCreationName;
                    union.Name = "Union All";
                    var unionDesign = union.Instantiate();
                    unionDesign.ProvideComponentProperties();
                    while (union.InputCollection.Count < 2)
                    {
                        var input = union.InputCollection.New();
                        input.Name = "Union All Input " + union.InputCollection.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }

                    var input1 = union.InputCollection[0];
                    var input2 = union.InputCollection[1];
                    var sourceAOutput = sourceA.OutputCollection.Cast<IDTSOutput100>().First(output => !output.IsErrorOut);
                    var sourceBOutput = sourceB.OutputCollection.Cast<IDTSOutput100>().First(output => !output.IsErrorOut);
                    var sourceAColumn = sourceAOutput.OutputColumnCollection.Cast<IDTSOutputColumn100>()
                        .Single(column => column.Name == "SharedValue");
                    var sourceBColumn = sourceBOutput.OutputColumnCollection.Cast<IDTSOutputColumn100>()
                        .Single(column => column.Name == "SharedValue");
                    pipeline.PathCollection.New().AttachPathAndPropagateNotifications(sourceAOutput, input1);
                    pipeline.PathCollection.New().AttachPathAndPropagateNotifications(sourceBOutput, input2);
                    SelectInputColumn(unionDesign, input1, sourceAColumn.LineageID);
                    SelectInputColumn(unionDesign, input2, sourceBColumn.LineageID);
                    unionDesign.ReinitializeMetaData();

                    var selectedPath = pipeline.PathCollection.Cast<IDTSPath100>()
                        .Single(path => path.StartPoint.Component.ID == sourceA.ID && path.EndPoint.ID == input1.ID);
                    var unionOutput = union.OutputCollection.Cast<IDTSOutput100>().First(output => !output.IsErrorOut);
                    var conflictColumn = unionOutput.OutputColumnCollection.Cast<IDTSOutputColumn100>()
                        .Single(column => column.Name == "SharedValue");
                    var originalPaths = pipeline.PathCollection.Cast<IDTSPath100>()
                        .Select(EndpointKey).OrderBy(key => key, StringComparer.Ordinal).ToArray();
                    var originalComponentIds = pipeline.ComponentMetaDataCollection.Cast<IDTSComponentMetaData100>()
                        .Select(component => component.ID).OrderBy(id => id).ToArray();
                    var originalInputs = union.InputCollection.Cast<IDTSInput100>()
                        .Select(UnionInputState).OrderBy(state => state, StringComparer.Ordinal).ToArray();
                    var inserted = AddMulticast(pipeline, "RejectedUnionAllInsertion");
                    var rejectionEvidence = string.Empty;
                    try
                    {
                        DataFlowPathSplicer.Splice(pipeline,
                            selectedPath.ID.ToString(System.Globalization.CultureInfo.InvariantCulture), inserted,
                            insertedOutputName: conflictColumn.Name);
                    }
                    catch (InvalidOperationException exception)
                    {
                        var aggregate = exception.InnerException as AggregateException;
                        var details = aggregate == null ? exception.InnerException?.Message
                            : string.Join(";", aggregate.Flatten().InnerExceptions.Select(inner => inner.ToString()));
                        rejectionEvidence = exception.Message + ":" + details;
                    }

                    Require(rejectionEvidence ==
                        "mutation.insertion.path_splice.endpoint_prepare_failed:mutation.insertion.union_all.output_name_conflict",
                        "endpoint_adapter_rejection.reported:" + rejectionEvidence);
                    var restoredPaths = pipeline.PathCollection.Cast<IDTSPath100>()
                        .Select(EndpointKey).OrderBy(key => key, StringComparer.Ordinal).ToArray();
                    var restoredComponentIds = pipeline.ComponentMetaDataCollection.Cast<IDTSComponentMetaData100>()
                        .Select(component => component.ID).OrderBy(id => id).ToArray();
                    var restoredInputs = union.InputCollection.Cast<IDTSInput100>()
                        .Select(UnionInputState).OrderBy(state => state, StringComparer.Ordinal).ToArray();
                    Require(originalPaths.SequenceEqual(restoredPaths), "endpoint_adapter_rejection.paths_restored");
                    Require(originalComponentIds.SequenceEqual(restoredComponentIds), "endpoint_adapter_rejection.component_removed");
                    Require(originalInputs.SequenceEqual(restoredInputs), "endpoint_adapter_rejection.inputs_restored");
                }
            }
            finally
            {
                if (Directory.Exists(directory)) { Directory.Delete(directory, true); }
            }
        }

        private static void SelectInputColumn(IDTSDesigntimeComponent100 design, IDTSInput100 input, int lineageId) =>
            design.SetUsageType(input.ID, input.GetVirtualInput(), lineageId, DTSUsageType.UT_READONLY);

        private static ConnectionManager CreateFlatFileConnection(Package package, string path)
        {
            var connection = package.Connections.Add("FLATFILE");
            connection.Name = "UnionAllRollbackSource";
            connection.ConnectionString = path;
            var file = (RuntimeWrapper.IDTSConnectionManagerFlatFile100)connection.InnerObject;
            file.Format = "Delimited";
            file.Unicode = false;
            file.LocaleID = 1033;
            file.ColumnNamesInFirstDataRow = false;
            var column = file.Columns.Add();
            column.ColumnType = "Delimited";
            column.ColumnDelimiter = Environment.NewLine;
            column.DataType = RuntimeWrapper.DataType.DT_I4;
            column.MaximumWidth = 4;
            ((RuntimeWrapper.IDTSName100)column).Name = "SharedValue";
            return connection;
        }

        private static IDTSComponentMetaData100 AddFlatFileSource(IDTSPipeline130 pipeline, Application application,
            ConnectionManager connection, string name)
        {
            var component = pipeline.ComponentMetaDataCollection.New();
            component.ComponentClassID = application.PipelineComponentInfos.Cast<PipelineComponentInfo>()
                .Single(info => info.CreationName.StartsWith("DTSAdapter.FlatFileSource.", StringComparison.OrdinalIgnoreCase))
                .CreationName;
            var design = component.Instantiate();
            design.ProvideComponentProperties();
            component.Name = name;
            component.LocaleID = 1033;
            component.RuntimeConnectionCollection[0].ConnectionManagerID = connection.ID;
            component.RuntimeConnectionCollection[0].ConnectionManager = DtsConvert.GetExtendedInterface(connection);
            design.AcquireConnections(null);
            try { design.ReinitializeMetaData(); }
            finally { design.ReleaseConnections(); }
            return component;
        }

        private static string UnionInputState(IDTSInput100 input)
        {
            var mappings = input.InputColumnCollection.Cast<IDTSInputColumn100>().Select(column =>
            {
                var property = column.CustomPropertyCollection.Cast<IDTSCustomProperty100>()
                    .SingleOrDefault(item => item.Name == "OutputColumnLineageID");
                return column.Name + ":" + column.LineageID.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    ":" + Convert.ToString(property?.Value, System.Globalization.CultureInfo.InvariantCulture);
            }).OrderBy(value => value, StringComparer.Ordinal);
            return input.Name + "|" + string.Join(";", mappings);
        }

        private static string EndpointKey(IDTSPath100 path) =>
            path.StartPoint.Component.ID + ":" + path.StartPoint.ID + "->" +
            path.EndPoint.Component.ID + ":" + path.EndPoint.ID;

        private static IDTSComponentMetaData100 AddMulticast(IDTSPipeline130 pipeline, string name)
        {
            var creationName = new Application().PipelineComponentInfos.Cast<PipelineComponentInfo>()
                .Single(info => info.CreationName.StartsWith("DTSTransform.Multicast.", StringComparison.OrdinalIgnoreCase))
                .CreationName;
            var component = pipeline.ComponentMetaDataCollection.New();
            component.ComponentClassID = creationName;
            component.Instantiate().ProvideComponentProperties();
            component.Name = name;
            return component;
        }

        private static void VerifyChain(IDTSPipeline130 pipeline, string sourceName, string sourcePortName,
            string insertedName, string destinationName, string destinationPortName, string code)
        {
            var inserted = pipeline.ComponentMetaDataCollection.Cast<IDTSComponentMetaData100>()
                .Single(component => component.Name == insertedName);
            var paths = pipeline.PathCollection.Cast<IDTSPath100>().ToArray();
            var upstream = paths.SingleOrDefault(path => path.EndPoint.Component.ID == inserted.ID);
            var downstream = paths.SingleOrDefault(path => path.StartPoint.Component.ID == inserted.ID);
            Require(upstream != null && downstream != null, code + ".adjacent_paths");
            if (sourceName != null)
            {
                Require(upstream.StartPoint.Component.Name == sourceName && upstream.StartPoint.Name == sourcePortName,
                    code + ".upstream_endpoint");
                Require(downstream.EndPoint.Component.Name == destinationName && downstream.EndPoint.Name == destinationPortName,
                    code + ".downstream_endpoint");
            }
        }

        private static TaskHost FindPipelineTask(Package package, string name) => FindPipelineTasks(package)
            .Single(task => task.Name == name);

        private static System.Collections.Generic.IEnumerable<TaskHost> FindPipelineTasks(Package package)
        {
            foreach (Executable executable in package.Executables)
            {
                foreach (var task in FindPipelineTasks((DtsContainer)executable)) { yield return task; }
            }
        }

        private static System.Collections.Generic.IEnumerable<TaskHost> FindPipelineTasks(DtsContainer container)
        {
            if (container is TaskHost task && task.InnerObject is IDTSPipeline130) { yield return task; }
            if (container is IDTSSequence sequence)
            {
                foreach (Executable executable in sequence.Executables)
                {
                    foreach (var nestedTask in FindPipelineTasks((DtsContainer)executable)) { yield return nestedTask; }
                }
            }
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(Environment.CurrentDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "SsisAiRuntime.sln")))
            { directory = directory.Parent; }
            return directory?.FullName ?? throw new DirectoryNotFoundException("ssis.repository.root_missing");
        }

        private static void Require(bool condition, string code)
        {
            if (!condition) { throw new InvalidOperationException("mutation.path_splice_fixture." + code); }
        }
    }
}