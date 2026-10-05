using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using SsisAiRuntime.Core;
using SsisAiRuntime.Inspectors;
using DtsPipeline = Microsoft.SqlServer.Dts.Pipeline.Wrapper;
using DtsRuntime = Microsoft.SqlServer.Dts.Runtime;

namespace SsisAiRuntime.Ssis16
{
    public sealed class PackageDataFlowInspector : IPackageDataFlowInspector<DtsRuntime.Package>
    {
        private const string SourceLineageProperty = "SourceInputColumnLineageID";
        private static readonly Lazy<HashSet<string>> DataConversionClassIds = new Lazy<HashSet<string>>(() => FindBuiltInClasses("Microsoft.DataConvert", "DTSTransform.DataConvert."));
        private static readonly Lazy<HashSet<string>> DerivedColumnClassIds = new Lazy<HashSet<string>>(() => FindBuiltInClasses("Microsoft.DerivedColumn", "DTSTransform.DerivedColumn."));
        internal static bool IsDerivedColumnClass(string classId) => DerivedColumnClassIds.Value.Contains(classId);

        private static readonly HashSet<string> SafeSettings = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AccessMode",
            "AlwaysUseDefaultCodePage",
            "CommandTimeout",
            "DefaultCodePage",
            "FastLoadKeepIdentity",
            "FastLoadKeepNulls",
            "FastLoadMaxInsertCommitSize",
            "FastLoadOptions",
            "OpenRowset",
            "OpenRowsetVariable",
            "ParameterMapping",
            "SqlCommand",
            "SqlCommandVariable"
        };

        public IReadOnlyList<DataFlowOverview> Inspect(PackageSession<DtsRuntime.Package> session)
        {
            return InspectDetailed(session).Items;
        }

        public InspectionResult<DataFlowOverview> InspectDetailed(PackageSession<DtsRuntime.Package> session)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            var dataFlows = new List<DataFlowOverview>();
            var unsupportedItems = new List<UnsupportedItem>();
            AddDataFlows(session.Package, session.Package.Executables, dataFlows, unsupportedItems);
            return new InspectionResult<DataFlowOverview>(dataFlows, unsupportedItems);
        }

        private static void AddDataFlows(
            DtsRuntime.Package package,
            DtsRuntime.Executables executables,
            ICollection<DataFlowOverview> dataFlows,
            ICollection<UnsupportedItem> unsupportedItems)
        {
            foreach (DtsRuntime.Executable executable in executables)
            {
                if (executable is DtsRuntime.IDTSSequence sequence)
                {
                    AddDataFlows(package, sequence.Executables, dataFlows, unsupportedItems);
                    continue;
                }

                if (!(executable is DtsRuntime.TaskHost taskHost))
                {
                    continue;
                }

                if (taskHost.CreationName.IndexOf("SSIS.Pipeline", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                try
                {
                    var pipeline = (DtsPipeline.IDTSPipeline130)taskHost.InnerObject;
                    var components = new List<DataFlowComponentOverview>();
                    foreach (DtsPipeline.IDTSComponentMetaData100 component in pipeline.ComponentMetaDataCollection)
                    {
                        var inputColumns = new List<DataFlowColumnOverview>();
                        var virtualInputColumns = new List<DataFlowColumnOverview>();
                        var outputColumns = new List<DataFlowColumnOverview>();
                        var outputs = new List<DataFlowOutputOverview>();
                        var externalMetadataColumns = new List<DataFlowColumnOverview>();
                        var runtimeConnections = new List<DataFlowRuntimeConnectionOverview>();
                        var settings = new List<DataFlowSettingOverview>();

                        foreach (DtsPipeline.IDTSRuntimeConnection100 runtimeConnection in component.RuntimeConnectionCollection)
                        {
                            runtimeConnections.Add(new DataFlowRuntimeConnectionOverview(
                                Convert.ToString(runtimeConnection.ID, CultureInfo.InvariantCulture),
                                runtimeConnection.Name,
                                Convert.ToString(runtimeConnection.ConnectionManagerID, CultureInfo.InvariantCulture)));
                        }

                        foreach (DtsPipeline.IDTSInput100 input in component.InputCollection)
                        {
                            try
                            {
                                foreach (DtsPipeline.IDTSVirtualInputColumn100 column in input.GetVirtualInput().VirtualInputColumnCollection)
                                {
                                    virtualInputColumns.Add(new DataFlowColumnOverview(
                                        component.ID.ToString(CultureInfo.InvariantCulture), input.ID.ToString(CultureInfo.InvariantCulture),
                                        input.Name, "VirtualInput", "virtual:" + input.ID.ToString(CultureInfo.InvariantCulture) + ":" + column.LineageID.ToString(CultureInfo.InvariantCulture),
                                        column.Name, column.DataType.ToString(), column.Length, column.Precision, column.Scale,
                                        column.CodePage, column.LineageID, 0, column.UsageType.ToString()));
                                }
                            }
                            catch (Exception)
                            {
                                unsupportedItems.Add(new UnsupportedItem(taskHost.ID, input.Name, component.ComponentClassID,
                                    "The virtual input column metadata could not be read.", UnsupportedItem.ReadFailureCode));
                            }
                            foreach (DtsPipeline.IDTSInputColumn100 column in input.InputColumnCollection)
                            {
                                inputColumns.Add(new DataFlowColumnOverview(
                                    component.ID.ToString(CultureInfo.InvariantCulture),
                                    input.ID.ToString(CultureInfo.InvariantCulture),
                                    input.Name,
                                    "Input",
                                    column.ID.ToString(CultureInfo.InvariantCulture),
                                    column.Name,
                                    column.DataType.ToString(),
                                    column.Length,
                                    column.Precision,
                                    column.Scale,
                                    column.CodePage,
                                    column.LineageID,
                                    column.ExternalMetadataColumnID,
                                    column.UsageType.ToString()));
                                AddUnsupportedColumnProperties(component, column.ID, column.CustomPropertyCollection, unsupportedItems);
                            }

                            AddExternalMetadataColumns(
                                component,
                                input.ID.ToString(CultureInfo.InvariantCulture),
                                input.Name,
                                "Input",
                                input.ExternalMetadataColumnCollection,
                                externalMetadataColumns,
                                unsupportedItems);
                            AddSettings(component, "Input", input.ID.ToString(CultureInfo.InvariantCulture), input.CustomPropertyCollection, settings, unsupportedItems);
                        }

                        foreach (DtsPipeline.IDTSOutput100 output in component.OutputCollection)
                        {
                            outputs.Add(new DataFlowOutputOverview(
                                output.ID.ToString(CultureInfo.InvariantCulture), output.Name,
                                output.SynchronousInputID > 0 ? output.SynchronousInputID.ToString(CultureInfo.InvariantCulture) : string.Empty,
                                output.IsErrorOut));
                            foreach (DtsPipeline.IDTSOutputColumn100 column in output.OutputColumnCollection)
                            {
                                var handlesSourceMapping = !output.IsErrorOut && DataConversionClassIds.Value.Contains(component.ComponentClassID);
                                var sourceLineage = handlesSourceMapping
                                    ? ReadSourceLineage(taskHost.ID, component, column, unsupportedItems)
                                    : null;
                                var expressionDependencies = !output.IsErrorOut && DerivedColumnClassIds.Value.Contains(component.ComponentClassID)
                                    ? ReadExpressionDependencies(taskHost, component, column.ID, column.Name, column.CustomPropertyCollection, unsupportedItems)
                                    : null;
                                outputColumns.Add(new DataFlowColumnOverview(
                                    component.ID.ToString(CultureInfo.InvariantCulture),
                                    output.ID.ToString(CultureInfo.InvariantCulture),
                                    output.Name,
                                    "Output",
                                    column.ID.ToString(CultureInfo.InvariantCulture),
                                    column.Name,
                                    column.DataType.ToString(),
                                    column.Length,
                                    column.Precision,
                                    column.Scale,
                                    column.CodePage,
                                    column.LineageID,
                                    column.ExternalMetadataColumnID,
                                    string.Empty,
                                    sourceLineage,
                                    expressionDependencies));
                                AddUnsupportedColumnProperties(component, column.ID, column.CustomPropertyCollection, unsupportedItems, handlesSourceMapping);
                            }

                            AddExternalMetadataColumns(
                                component,
                                output.ID.ToString(CultureInfo.InvariantCulture),
                                output.Name,
                                "Output",
                                output.ExternalMetadataColumnCollection,
                                externalMetadataColumns,
                                unsupportedItems);
                            AddSettings(component, "Output", output.ID.ToString(CultureInfo.InvariantCulture), output.CustomPropertyCollection, settings, unsupportedItems);
                        }

                        if (DerivedColumnClassIds.Value.Contains(component.ComponentClassID))
                        {
                            foreach (DtsPipeline.IDTSInput100 input in component.InputCollection)
                            {
                                foreach (DtsPipeline.IDTSInputColumn100 column in input.InputColumnCollection)
                                {
                                    if (!column.CustomPropertyCollection.Cast<DtsPipeline.IDTSCustomProperty100>()
                                        .Any(property => string.Equals(property.Name, "Expression", StringComparison.OrdinalIgnoreCase))) { continue; }
                                    var dependencies = ReadExpressionDependencies(taskHost, component, column.ID, column.Name,
                                        column.CustomPropertyCollection, unsupportedItems);
                                    foreach (DtsPipeline.IDTSOutput100 output in component.OutputCollection)
                                    {
                                        if (output.IsErrorOut || output.SynchronousInputID != input.ID) { continue; }
                                        outputColumns.Add(new DataFlowColumnOverview(
                                            component.ID.ToString(CultureInfo.InvariantCulture), output.ID.ToString(CultureInfo.InvariantCulture),
                                            output.Name, "Output", "replaced:" + column.ID.ToString(CultureInfo.InvariantCulture) + ":" + output.ID.ToString(CultureInfo.InvariantCulture),
                                            column.Name, column.DataType.ToString(), column.Length, column.Precision, column.Scale,
                                            column.CodePage, column.LineageID, column.ExternalMetadataColumnID, "Replacement", null, dependencies, true));
                                    }
                                }
                            }
                        }
                        AddSettings(component, "Component", component.ID.ToString(CultureInfo.InvariantCulture), component.CustomPropertyCollection, settings, unsupportedItems);
                        components.Add(new DataFlowComponentOverview(
                            component.ID.ToString(CultureInfo.InvariantCulture),
                            component.Name,
                            component.ComponentClassID,
                            component.Description,
                            component.InputCollection.Count,
                            component.OutputCollection.Count,
                            inputColumns,
                            outputColumns,
                            externalMetadataColumns,
                            runtimeConnections,
                            settings,
                            outputs,
                            virtualInputColumns));
                    }

                    var paths = new List<DataFlowPathOverview>();
                    foreach (DtsPipeline.IDTSPath100 path in pipeline.PathCollection)
                    {
                        var output = path.StartPoint as DtsPipeline.IDTSOutput100;
                        var input = path.EndPoint as DtsPipeline.IDTSInput100;
                        paths.Add(new DataFlowPathOverview(
                            path.ID.ToString(CultureInfo.InvariantCulture),
                            path.Name,
                            output == null || output.Component == null ? string.Empty : output.Component.ID.ToString(CultureInfo.InvariantCulture),
                            output == null ? string.Empty : output.ID.ToString(CultureInfo.InvariantCulture),
                            input == null || input.Component == null ? string.Empty : input.Component.ID.ToString(CultureInfo.InvariantCulture),
                            input == null ? string.Empty : input.ID.ToString(CultureInfo.InvariantCulture)));
                    }

                    dataFlows.Add(new DataFlowOverview(taskHost.ID, taskHost.Name, components, paths));
                }
                catch (Exception)
                {
                    unsupportedItems.Add(new UnsupportedItem(
                        taskHost.ID,
                        taskHost.Name,
                        taskHost.CreationName,
                        "The native pipeline component or path collections could not be inspected.",
                        UnsupportedItem.ReadFailureCode));
                }
            }
        }

        private static void AddExternalMetadataColumns(
            DtsPipeline.IDTSComponentMetaData100 component,
            string portId,
            string portName,
            string direction,
            DtsPipeline.IDTSExternalMetadataColumnCollection100 columns,
            ICollection<DataFlowColumnOverview> results,
            ICollection<UnsupportedItem> unsupportedItems)
        {
            foreach (DtsPipeline.IDTSExternalMetadataColumn100 column in columns)
            {
                results.Add(new DataFlowColumnOverview(
                    component.ID.ToString(CultureInfo.InvariantCulture),
                    portId,
                    portName,
                    direction + "ExternalMetadata",
                    column.ID.ToString(CultureInfo.InvariantCulture),
                    column.Name,
                    column.DataType.ToString(),
                    column.Length,
                    column.Precision,
                    column.Scale,
                    column.CodePage,
                    0,
                    column.MappedColumnID,
                    "ExternalMetadata"));

                AddUnsupportedColumnProperties(component, column.ID, column.CustomPropertyCollection, unsupportedItems);
            }
        }

        private static HashSet<string> FindBuiltInClasses(string modernName, string prefix)
        {
            var classes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { modernName };
            try
            {
                foreach (DtsRuntime.PipelineComponentInfo info in new DtsRuntime.Application().PipelineComponentInfos)
                {
                    if (string.Equals(info.CreationName, modernName, StringComparison.OrdinalIgnoreCase) ||
                        info.CreationName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        classes.Add(info.CreationName);
                        classes.Add(info.ID);
                    }
                }
            }
            catch (Exception)
            {
                return classes;
            }
            return classes;
        }

        private static DataFlowExpressionDependencies ReadExpressionDependencies(DtsRuntime.TaskHost taskHost,
            DtsPipeline.IDTSComponentMetaData100 component, int columnId, string columnName,
            DtsPipeline.IDTSCustomPropertyCollection100 properties, ICollection<UnsupportedItem> unsupportedItems)
        {
            object evaluator = null;
            try
            {
                var expressions = properties.Cast<DtsPipeline.IDTSCustomProperty100>()
                    .Where(property => string.Equals(property.Name, "Expression", StringComparison.OrdinalIgnoreCase)).ToList();
                if (expressions.Count == 1 && !expressions[0].EncryptionRequired && component.InputCollection.Count == 1)
                {
                    var expression = expressions[0].Value as string;
                    if (!string.IsNullOrWhiteSpace(expression) && expression.Length <= 65536)
                    {
                        evaluator = new Microsoft.SqlServer.Dts.Runtime.Wrapper.ExpressionEvaluatorClass();
                        var parser = evaluator as DtsPipeline.IDTSExpressionEvaluatorEx100;
                        if (parser != null)
                        {
                            var columns = component.InputCollection[0].InputColumnCollection;
                            var before = SnapshotExpressionInputs(columns);
                            var observer = new NativeExpressionInputColumns(columns);
                            parser.Parse(expression, DtsRuntime.DtsConvert.GetExtendedInterface(taskHost.VariableDispenser), observer);
                            if (observer.GeneralReads == 0 && observer.MutationAttempts == 0 && before == SnapshotExpressionInputs(columns))
                            {
                                return new DataFlowExpressionDependencies(true, observer.BindingLineageIds);
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                unsupportedItems.Add(new UnsupportedItem(columnId.ToString(CultureInfo.InvariantCulture), columnName,
                    component.ComponentClassID, "The expression column references could not be resolved.", UnsupportedItem.UnsupportedMetadataCode));
                return new DataFlowExpressionDependencies(false, Array.Empty<int>());
            }
            finally
            {
                if (evaluator != null && System.Runtime.InteropServices.Marshal.IsComObject(evaluator))
                {
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(evaluator);
                }
            }
            unsupportedItems.Add(new UnsupportedItem(columnId.ToString(CultureInfo.InvariantCulture), columnName,
                component.ComponentClassID, "The expression column references are unavailable or unsupported.", UnsupportedItem.UnsupportedMetadataCode));
            return new DataFlowExpressionDependencies(false, Array.Empty<int>());
        }

        private static string SnapshotExpressionInputs(DtsPipeline.IDTSInputColumnCollection100 columns)
        {
            return string.Join("|", columns.Cast<DtsPipeline.IDTSInputColumn100>().Select(column =>
                column.ID + ":" + column.LineageID + ":" + column.UsageType + ":" + column.DataType));
        }

        private static int? ReadSourceLineage(string flowId, DtsPipeline.IDTSComponentMetaData100 component,
            DtsPipeline.IDTSOutputColumn100 column, ICollection<UnsupportedItem> unsupportedItems)
        {
            try
            {
                var properties = column.CustomPropertyCollection.Cast<DtsPipeline.IDTSCustomProperty100>()
                    .Where(property => string.Equals(property.Name, SourceLineageProperty, StringComparison.OrdinalIgnoreCase)).ToList();
                if (properties.Count == 1 && !properties[0].EncryptionRequired)
                {
                    var value = properties[0].Value;
                    if (value is int sourceLineage && sourceLineage > 0)
                    {
                        return sourceLineage;
                    }
                }
                var encrypted = properties.Count == 1 && properties[0].EncryptionRequired;
                unsupportedItems.Add(new UnsupportedItem(column.ID.ToString(CultureInfo.InvariantCulture), column.Name,
                    component.ComponentClassID, "The explicit source-lineage mapping is unavailable or unsupported.",
                    encrypted ? UnsupportedItem.IntentionalOmissionCode : UnsupportedItem.UnsupportedMetadataCode));
            }
            catch (Exception)
            {
                unsupportedItems.Add(new UnsupportedItem(flowId, column.Name, component.ComponentClassID,
                    "The explicit source-lineage mapping could not be read.", UnsupportedItem.ReadFailureCode));
            }
            return null;
        }

        private static void AddUnsupportedColumnProperties(
            DtsPipeline.IDTSComponentMetaData100 component,
            int columnId,
            DtsPipeline.IDTSCustomPropertyCollection100 properties,
            ICollection<UnsupportedItem> unsupportedItems,
            bool sourceMappingHandled = false)
        {
            foreach (DtsPipeline.IDTSCustomProperty100 property in properties)
            {
                if (sourceMappingHandled && string.Equals(property.Name, SourceLineageProperty, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                unsupportedItems.Add(new UnsupportedItem(
                    columnId.ToString(CultureInfo.InvariantCulture),
                    property.Name,
                    component.ComponentClassID,
                    "Column custom-property values are not inspected.",
                    UnsupportedItem.IntentionalOmissionCode));
            }
        }

        private static void AddSettings(
            DtsPipeline.IDTSComponentMetaData100 component,
            string ownerType,
            string ownerId,
            DtsPipeline.IDTSCustomPropertyCollection100 properties,
            ICollection<DataFlowSettingOverview> settings,
            ICollection<UnsupportedItem> unsupportedItems)
        {
            foreach (DtsPipeline.IDTSCustomProperty100 property in properties)
            {
                if (!SafeSettings.Contains(property.Name))
                {
                    unsupportedItems.Add(new UnsupportedItem(
                        component.ID.ToString(CultureInfo.InvariantCulture),
                        property.Name,
                        component.ComponentClassID,
                        "This custom property value is not in the safe data-flow settings allowlist.",
                        UnsupportedItem.IntentionalOmissionCode));
                    continue;
                }

                if (property.EncryptionRequired)
                {
                    settings.Add(new DataFlowSettingOverview(ownerType, ownerId, property.Name, "<redacted>", true));
                    continue;
                }

                var value = property.Value;
                if (value != null && !(value is string) && !(value is IConvertible))
                {
                    unsupportedItems.Add(new UnsupportedItem(
                        component.ID.ToString(CultureInfo.InvariantCulture),
                        property.Name,
                        component.ComponentClassID,
                        "This custom property value type is not supported.",
                        UnsupportedItem.UnsupportedMetadataCode));
                    continue;
                }

                var rawValue = value == null ? string.Empty : Convert.ToString(value, CultureInfo.InvariantCulture);
                var sanitized = SqlTextSanitizer.Sanitize(rawValue);
                settings.Add(new DataFlowSettingOverview(ownerType, ownerId, property.Name, sanitized.Text, sanitized.Redacted));
            }
        }
    }
}