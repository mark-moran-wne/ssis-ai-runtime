using System;
using System.Globalization;
using System.Linq;
using Microsoft.SqlServer.Dts.Pipeline.Wrapper;
using Microsoft.SqlServer.Dts.Runtime;
using RuntimeWrapper = Microsoft.SqlServer.Dts.Runtime.Wrapper;
using SsisAiRuntime.MutationHost;

namespace SsisAiRuntime.Ssis16IntegrationTests
{
    internal static class UnionAllMetadataProbe
    {
        public static void Run()
        {
            var stage = "package_create";
            var validationDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "SsisAiRuntime-UnionAll-Source-" + Guid.NewGuid().ToString("N"));
            try
            {
                System.IO.Directory.CreateDirectory(validationDirectory);
                var sourceFile = System.IO.Path.Combine(validationDirectory, "source.txt");
                System.IO.File.WriteAllText(sourceFile, "42" + Environment.NewLine, System.Text.Encoding.ASCII);
                using (var package = new Package
                { Name = "UnionAllMetadataProbe", ProtectionLevel = DTSProtectionLevel.DontSaveSensitive })
                {
                    var task = (TaskHost)package.Executables.Add("STOCK:PipelineTask");
                    task.Name = "MergeFlow";
                    var pipeline = (IDTSPipeline130)task.InnerObject;
                    var application = new Application();
                    stage = "create_components";
                    var sourceConnection = CreateFlatFileConnection(package, sourceFile);
                    var sourceA = AddFlatFileSource(pipeline, application, sourceConnection, "Source A");
                    var sourceB = AddFlatFileSource(pipeline, application, sourceConnection, "Source B");
                    var union = AddComponent(pipeline, application, "DTSTransform.UnionAll.", "Union All");
                    package.DesignTimeProperties = "<Objects Version='8'><Package design-time-name='Package'><LayoutInfo><GraphLayout>" +
                        "<NodeLayout Id='Package\\MergeFlow' TopLeft='20,20' Size='180,42'/>" +
                        "</GraphLayout></LayoutInfo></Package><TaskHost design-time-name='Package\\MergeFlow'><LayoutInfo><GraphLayout>" +
                        "<NodeLayout Id='Package\\MergeFlow\\Source A' TopLeft='80,80' Size='114,42'/>" +
                        "<NodeLayout Id='Package\\MergeFlow\\Source B' TopLeft='80,180' Size='114,42'/>" +
                        "<NodeLayout Id='Package\\MergeFlow\\Union All' TopLeft='300,130' Size='120,42'/>" +
                        "</GraphLayout></LayoutInfo></TaskHost></Objects>";
                    var sourceAOutput = sourceA.OutputCollection.Cast<IDTSOutput100>().First(output => !output.IsErrorOut);
                    var sourceBOutput = sourceB.OutputCollection.Cast<IDTSOutput100>().First(output => !output.IsErrorOut);
                    stage = "add_source_columns";
                    var sourceAColumn = sourceAOutput.OutputColumnCollection.Cast<IDTSOutputColumn100>()
                        .Single(column => column.Name == "SharedValue");
                    var sourceBColumn = sourceBOutput.OutputColumnCollection.Cast<IDTSOutputColumn100>()
                        .Single(column => column.Name == "SharedValue");
                    while (union.InputCollection.Count < 2)
                    {
                        var input = union.InputCollection.New();
                        input.Name = "Union All Input " + union.InputCollection.Count.ToString(CultureInfo.InvariantCulture);
                    }

                    var unionDesign = union.Instantiate();
                    var input1 = union.InputCollection[0];
                    var input2 = union.InputCollection[1];
                    stage = "attach_source_a";
                    pipeline.PathCollection.New().AttachPathAndPropagateNotifications(sourceAOutput, input1);
                    stage = "attach_source_b";
                    pipeline.PathCollection.New().AttachPathAndPropagateNotifications(sourceBOutput, input2);
                    stage = "select_input_1_usage";
                    SelectInputColumn(unionDesign, input1, sourceAColumn.LineageID);
                    stage = "select_input_2_usage";
                    SelectInputColumn(unionDesign, input2, sourceBColumn.LineageID);
                    stage = "baseline_reinitialize";
                    unionDesign.ReinitializeMetaData();
                    var selectedInputName = input1.Name;
                    var nonselectedInputsBefore = union.InputCollection.Cast<IDTSInput100>()
                        .Where(input => input.ID != input1.ID)
                        .ToDictionary(input => input.Name, input => InputState(union, input));
                    var outputsBefore = union.OutputCollection.Cast<IDTSOutput100>().Where(output => !output.IsErrorOut)
                        .SelectMany(output => output.OutputColumnCollection.Cast<IDTSOutputColumn100>())
                        .ToDictionary(column => column.Name, OutputState);
                    var nonselectedPath = pipeline.PathCollection.Cast<IDTSPath100>()
                        .Where(path => path.EndPoint.Component.ID != union.ID || path.EndPoint.ID != input1.ID)
                        .Select(PathState).ToArray();
                    var componentIdsBefore = ComponentIds(pipeline);
                    var componentClassesBefore = ComponentClasses(pipeline);
                    var layoutBefore = new DesignerLayoutExtractor().Extract(package);
                    var replacedConnectionKey = layoutBefore.Connections.Single(connection =>
                        connection.SourceNativeId == sourceA.ID.ToString(CultureInfo.InvariantCulture) &&
                        connection.DestinationNativeId == union.ID.ToString(CultureInfo.InvariantCulture)).Key;
                    var baselineValidation = new NativePackageMutationValidator().Validate(
                        new SsisAiRuntime.Core.PackageSession<Package>(
                            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "UnionAllMetadataProbe.dtsx"),
                            package.Name, package));
                    stage = "selected_input_splice";
                    ProbeSelectedInputSplice(package, pipeline, sourceAOutput, sourceAColumn.LineageID, union,
                        selectedInputName, nonselectedInputsBefore, outputsBefore, nonselectedPath,
                        componentIdsBefore, componentClassesBefore, layoutBefore, replacedConnectionKey, baselineValidation);
                }
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException("Union All metadata acceptance failed at " + stage, exception);
            }
            finally
            {
                if (System.IO.Directory.Exists(validationDirectory))
                { System.IO.Directory.Delete(validationDirectory, true); }
            }
        }

        private static void ProbeSelectedInputSplice(Package package, IDTSPipeline130 pipeline, IDTSOutput100 sourceOutput,
            int sourceLineageId, IDTSComponentMetaData100 union, string selectedInputName,
            System.Collections.Generic.IDictionary<string, string> nonselectedInputsBefore,
            System.Collections.Generic.IDictionary<string, string> outputsBefore, string[] nonselectedPathsBefore,
            System.Collections.Generic.IDictionary<string, int> componentIdsBefore,
            System.Collections.Generic.IDictionary<string, string> componentClassesBefore,
            DesignerLayoutSnapshot layoutBefore, string replacedConnectionKey,
            NativeMutationValidationResult baselineValidation)
        {
            var originalPath = pipeline.PathCollection.Cast<IDTSPath100>().Single(path =>
                path.StartPoint.ID == sourceOutput.ID && path.EndPoint.Component.ID == union.ID);
            var endpointAdapter = UnionAllInputAdapter.CreateIfRequired(pipeline, originalPath);
            Require(endpointAdapter != null, "union_all.adapter_not_detected");
            endpointAdapter.Prepare();
            var component = AddComponent(pipeline, new Application(), "DTSTransform.DerivedColumn.", "ProbeDerived");
            var componentDesign = component.Instantiate();
            var componentInput = component.InputCollection[0];
            var output = component.OutputCollection.Cast<IDTSOutput100>().Single(port => !port.IsErrorOut);
            DataFlowPathSplicer.Splice(pipeline, originalPath.ID.ToString(CultureInfo.InvariantCulture), component, input =>
            {
                componentDesign.SetUsageType(input.ID, input.GetVirtualInput(), sourceLineageId, DTSUsageType.UT_READONLY);
                var column = componentDesign.InsertOutputColumnAt(output.ID, 0, "ProbeValue", string.Empty);
                componentDesign.SetOutputColumnDataTypeProperties(output.ID, column.ID, RuntimeWrapper.DataType.DT_I4,
                    0, 0, 0, 0);
                componentDesign.SetOutputColumnProperty(output.ID, column.ID, "Expression",
                    "#" + sourceLineageId.ToString(CultureInfo.InvariantCulture) + " + 1");
            }, "ProbeValue", endpointAdapter);
            VerifySplicedState(pipeline, union, selectedInputName, nonselectedInputsBefore,
                outputsBefore, nonselectedPathsBefore);
            VerifyComponentIdentities(pipeline, componentIdsBefore, componentClassesBefore, true);
            var layoutAfter = new DesignerLayoutExtractor().Extract(package);
            var layoutComparison = new DesignerLayoutComparer().ComparePreservingExisting(layoutBefore, layoutAfter,
                explicitlyReplacedConnectionKeys: new[] { replacedConnectionKey });
            Require(layoutComparison.Matches, "union_all.in_memory_layout_changed: " +
                string.Join(",", layoutComparison.Differences));
            var inMemoryValidation = new NativePackageMutationValidator().Validate(
                new SsisAiRuntime.Core.PackageSession<Package>(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "UnionAllMetadataProbe-PostSplice.dtsx"),
                    package.Name, package));
            Require(ValidationState(inMemoryValidation) == ValidationState(baselineValidation),
                "union_all.in_memory_validation_changed: before=" + ValidationState(baselineValidation) +
                "; after=" + ValidationState(inMemoryValidation));

            var reloadPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "SsisAiRuntime-UnionAll-" + Guid.NewGuid().ToString("N") + ".dtsx");
            try
            {
                var application = new Application();
                application.SaveToXml(reloadPath, package, null);
                using (var reloadedPackage = application.LoadPackage(reloadPath, null))
                {
                    var reloadedTask = reloadedPackage.Executables.Cast<Executable>().OfType<TaskHost>()
                        .Single(task => task.Name == "MergeFlow");
                    var reloadedPipeline = (IDTSPipeline130)reloadedTask.InnerObject;
                    var reloadedUnion = reloadedPipeline.ComponentMetaDataCollection
                        .Cast<IDTSComponentMetaData100>().Single(component => component.Name == "Union All");
                    Require(reloadedPackage.ID == package.ID && reloadedPackage.Name == package.Name,
                        "union_all.package_identity_changed_after_reload");
                    VerifyComponentIdentities(reloadedPipeline, componentIdsBefore, componentClassesBefore, false);
                    var reloadedSourceA = reloadedPipeline.ComponentMetaDataCollection
                        .Cast<IDTSComponentMetaData100>().Single(component => component.Name == "Source A");
                    var reloadedSourceConnection = reloadedPackage.Connections.Cast<ConnectionManager>()
                        .Single(connection => connection.Name == "UnionAllSource");
                    Require(reloadedSourceA.RuntimeConnectionCollection[0].ConnectionManagerID == reloadedSourceConnection.ID,
                        "union_all.reloaded_source_connection_id_changed");
                    Require(System.IO.File.Exists(reloadedSourceConnection.ConnectionString),
                        "union_all.reloaded_source_file_missing");
                    var reloadedFlatFile = (RuntimeWrapper.IDTSConnectionManagerFlatFile100)reloadedSourceConnection.InnerObject;
                    Require(!reloadedFlatFile.Unicode, "union_all.reloaded_source_not_ansi");
                    Require(reloadedFlatFile.Columns.Cast<RuntimeWrapper.IDTSConnectionManagerFlatFileColumn100>()
                        .Single().DataType == RuntimeWrapper.DataType.DT_I4, "union_all.reloaded_source_column_type_changed");
                    var reloadedSourceOutputs = reloadedSourceA.OutputCollection.Cast<IDTSOutput100>().ToArray();
                    Require(reloadedSourceOutputs.Single(output => !output.IsErrorOut).OutputColumnCollection
                        .Cast<IDTSOutputColumn100>().Single().DataType == RuntimeWrapper.DataType.DT_I4,
                        "union_all.reloaded_source_output_type_changed");
                    Require(reloadedSourceOutputs.Single(output => output.IsErrorOut).OutputColumnCollection
                        .Cast<IDTSOutputColumn100>().Single(column => column.Name == "Flat File Source Error Output Column")
                        .DataType == RuntimeWrapper.DataType.DT_TEXT,
                        "union_all.reloaded_source_error_output_type_changed");
                    VerifySplicedState(reloadedPipeline, reloadedUnion, selectedInputName,
                        nonselectedInputsBefore, outputsBefore, nonselectedPathsBefore);
                    var reloadedLayout = new DesignerLayoutExtractor().Extract(reloadedPackage);
                    var reloadedLayoutComparison = new DesignerLayoutComparer().ComparePreservingExisting(
                        layoutBefore, reloadedLayout, explicitlyReplacedConnectionKeys: new[] { replacedConnectionKey });
                    Require(reloadedLayoutComparison.Matches, "union_all.reloaded_layout_changed: " +
                        string.Join(",", reloadedLayoutComparison.Differences));
                    var offlineModeBefore = reloadedPackage.OfflineMode;
                    var validation = new NativePackageMutationValidator().Validate(
                        new SsisAiRuntime.Core.PackageSession<Package>(reloadPath, reloadedPackage.Name, reloadedPackage));
                    Require(ValidationState(validation) == ValidationState(baselineValidation),
                        "union_all.offline_validation_changed: before=" + ValidationState(baselineValidation) +
                        "; after=" + ValidationState(validation) + "; errorCodes=" + string.Join(",",
                            reloadedPackage.Errors.Cast<DtsError>().Select(error =>
                                error.ErrorCode.ToString("X8", CultureInfo.InvariantCulture) + ":" + ErrorName(error.ErrorCode) + ":" +
                                error.Source + ":" + error.SubComponent)));
                    Require(reloadedPackage.OfflineMode == offlineModeBefore, "union_all.offline_mode_not_restored");
                    Console.WriteLine(validation.Succeeded
                        ? "Union All offline Package.Validate: PASS; OfflineMode restored; no execution or data-source connection."
                        : "Union All offline Package.Validate: PASS; pre-existing synthetic-source diagnostics unchanged; " +
                            "OfflineMode restored; no execution or data-source connection.");
                }
            }
            finally
            {
                if (System.IO.File.Exists(reloadPath)) { System.IO.File.Delete(reloadPath); }
            }
        }

        private static void VerifySplicedState(IDTSPipeline130 pipeline, IDTSComponentMetaData100 union,
            string selectedInputName, System.Collections.Generic.IDictionary<string, string> nonselectedInputsBefore,
            System.Collections.Generic.IDictionary<string, string> outputsBefore, string[] nonselectedPathsBefore)
        {
            var inputs = union.InputCollection.Cast<IDTSInput100>().ToArray();
            var selected = inputs.SingleOrDefault(input => input.Name == selectedInputName);
            Require(selected != null, "union_all.selected_input_missing");
            var outputs = union.OutputCollection.Cast<IDTSOutput100>().Where(output => !output.IsErrorOut)
                .SelectMany(output => output.OutputColumnCollection.Cast<IDTSOutputColumn100>()).ToArray();
            var sharedOutput = outputs.SingleOrDefault(column => column.Name == "SharedValue");
            Require(sharedOutput != null && InputOutputLineage(selected, "SharedValue") == sharedOutput.LineageID,
                "union_all.prior_mapping_changed");
            var insertedOutput = outputs.SingleOrDefault(column => column.Name == "ProbeValue");
            Require(insertedOutput != null && insertedOutput.DataType == RuntimeWrapper.DataType.DT_I4,
                "union_all.inserted_output_missing");
            Require(InputOutputLineage(selected, "ProbeValue") == insertedOutput.LineageID,
                "union_all.inserted_mapping_missing");
            foreach (var expected in nonselectedInputsBefore)
            {
                var actual = inputs.SingleOrDefault(input => input.Name == expected.Key);
                Require(actual != null && InputState(union, actual) == expected.Value,
                    "union_all.nonselected_input_changed");
            }
            foreach (var expected in outputsBefore)
            {
                var actual = outputs.SingleOrDefault(column => column.Name == expected.Key);
                Require(actual != null && OutputState(actual) == expected.Value,
                    "union_all.prior_output_changed");
            }
            Require(outputs.Length == outputsBefore.Count + 1, "union_all.output_inventory_changed");
            var paths = pipeline.PathCollection.Cast<IDTSPath100>().ToArray();
            Require(paths.Length == nonselectedPathsBefore.Length + 2, "union_all.path_count_changed");
            foreach (var expected in nonselectedPathsBefore)
            { Require(paths.Any(path => PathState(path) == expected), "union_all.nonselected_path_changed"); }
            Require(paths.Any(path => path.StartPoint.Component.Name == "ProbeDerived" &&
                path.EndPoint.Component.ID == union.ID && path.EndPoint.Name == selectedInputName),
                "union_all.inserted_path_missing");
            Console.WriteLine("Union All selected-input metadata: PASS; prior output and nonselected input/path preserved; " +
                "new output mapped; paths=" + paths.Length + ".");
        }

        private static void SelectInputColumn(IDTSDesigntimeComponent100 design, IDTSInput100 input, int lineageId)
        {
            design.SetUsageType(input.ID, input.GetVirtualInput(), lineageId, DTSUsageType.UT_READONLY);
        }

        private static string InputState(IDTSComponentMetaData100 union, IDTSInput100 input)
        {
            var columns = input.InputColumnCollection.Cast<IDTSInputColumn100>().Select(column => string.Join(":",
                column.Name, column.DataType.ToString(), column.Length.ToString(CultureInfo.InvariantCulture),
                column.Precision.ToString(CultureInfo.InvariantCulture), column.Scale.ToString(CultureInfo.InvariantCulture),
                column.CodePage.ToString(CultureInfo.InvariantCulture),
                OutputNameForLineage(union, InputOutputLineage(column))))
                .OrderBy(value => value, StringComparer.Ordinal);
            return string.Join(";", input.HasSideEffects.ToString(), input.ErrorRowDisposition.ToString(),
                input.TruncationRowDisposition.ToString(), string.Join("|", columns));
        }

        private static string OutputState(IDTSOutputColumn100 column) => string.Join("|", column.Name,
            column.DataType.ToString(),
            column.Length.ToString(CultureInfo.InvariantCulture), column.Precision.ToString(CultureInfo.InvariantCulture),
            column.Scale.ToString(CultureInfo.InvariantCulture), column.CodePage.ToString(CultureInfo.InvariantCulture));

        private static string OutputNameForLineage(IDTSComponentMetaData100 union, int lineageId)
        {
            var column = union.OutputCollection.Cast<IDTSOutput100>().Where(output => !output.IsErrorOut)
                .SelectMany(output => output.OutputColumnCollection.Cast<IDTSOutputColumn100>())
                .SingleOrDefault(output => output.LineageID == lineageId);
            return column?.Name ?? string.Empty;
        }

        private static int InputOutputLineage(IDTSInput100 input, string name)
        {
            var column = input.InputColumnCollection.Cast<IDTSInputColumn100>().SingleOrDefault(item => item.Name == name);
            return column == null ? 0 : InputOutputLineage(column);
        }

        private static int InputOutputLineage(IDTSInputColumn100 column)
        {
            var property = column.CustomPropertyCollection.Cast<IDTSCustomProperty100>()
                .SingleOrDefault(item => item.Name == "OutputColumnLineageID");
            return property == null ? 0 : Convert.ToInt32(property.Value, CultureInfo.InvariantCulture);
        }

        private static string PathState(IDTSPath100 path) => string.Join("->", path.StartPoint.Component.Name,
            path.StartPoint.Name, path.EndPoint.Component.Name, path.EndPoint.Name);

        private static System.Collections.Generic.IDictionary<string, int> ComponentIds(IDTSPipeline130 pipeline) =>
            pipeline.ComponentMetaDataCollection.Cast<IDTSComponentMetaData100>()
                .ToDictionary(component => component.Name, component => component.ID, StringComparer.Ordinal);

        private static System.Collections.Generic.IDictionary<string, string> ComponentClasses(IDTSPipeline130 pipeline) =>
            pipeline.ComponentMetaDataCollection.Cast<IDTSComponentMetaData100>()
                .ToDictionary(component => component.Name, component => component.ComponentClassID, StringComparer.Ordinal);

        private static void VerifyComponentIdentities(IDTSPipeline130 pipeline,
            System.Collections.Generic.IDictionary<string, int> componentIdsBefore,
            System.Collections.Generic.IDictionary<string, string> componentClassesBefore, bool requireNativeIds)
        {
            var components = pipeline.ComponentMetaDataCollection.Cast<IDTSComponentMetaData100>()
                .ToDictionary(component => component.Name, StringComparer.Ordinal);
            foreach (var expected in componentClassesBefore)
            {
                Require(components.TryGetValue(expected.Key, out var actual) && actual.ComponentClassID == expected.Value,
                    "union_all.component_identity_changed");
                if (requireNativeIds)
                { Require(actual.ID == componentIdsBefore[expected.Key], "union_all.component_native_id_changed"); }
            }
        }

        private static string ValidationState(NativeMutationValidationResult result) => string.Join("|",
            result.Succeeded.ToString(), result.HasWarnings.ToString(), string.Join(",", result.DiagnosticCodes));

        private static string ErrorName(int errorCode)
        {
            var field = typeof(HResults).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .FirstOrDefault(item => item.FieldType == typeof(int) &&
                    Convert.ToInt32(item.GetValue(null), CultureInfo.InvariantCulture) == errorCode);
            return field?.Name ?? "unknown";
        }

        private static void Require(bool condition, string code)
        {
            if (!condition) { throw new InvalidOperationException(code); }
        }

        private static ConnectionManager CreateFlatFileConnection(Package package, string path)
        {
            var connection = package.Connections.Add("FLATFILE");
            connection.Name = "UnionAllSource";
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

        private static IDTSComponentMetaData100 AddComponent(IDTSPipeline130 pipeline, Application application,
            string creationNamePrefix, string name)
        {
            var creationName = application.PipelineComponentInfos.Cast<PipelineComponentInfo>()
                .Single(info => info.CreationName.StartsWith(creationNamePrefix, StringComparison.OrdinalIgnoreCase))
                .CreationName;
            var component = pipeline.ComponentMetaDataCollection.New();
            component.ComponentClassID = creationName;
            component.Instantiate().ProvideComponentProperties();
            component.Name = name;
            return component;
        }
    }
}