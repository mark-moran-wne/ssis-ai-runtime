using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml.Linq;
using Microsoft.SqlServer.Dts.Pipeline.Wrapper;
using Microsoft.SqlServer.Dts.Runtime;
using SsisAiRuntime.Mutations;
using RuntimeWrapper = Microsoft.SqlServer.Dts.Runtime.Wrapper;

namespace SsisAiRuntime.MutationHost
{
    public sealed class DataConversionInsertionPlan : IComponentInsertionPlan
    {
        private readonly DesignerLayoutSnapshot beforeLayout;
        private readonly string replacedConnectionKey;
        private readonly IReadOnlyDictionary<string, string> existingComponentMetadata;

        private DataConversionInsertionPlan(string sourceArtifactHash, string ownerTaskNativeId,
            string sourceComponentNativeId, string destinationComponentNativeId, string pathNativeId, int sourceLineageId,
            string sourceOutputName, string sourceColumnName, string componentName, string outputName,
            RuntimeWrapper.DataType outputDataType, int length, int precision, int scale, int codePage,
            ComponentPlacementIntent placementIntent, DesignerPlacement placement,
            DesignerLayoutSnapshot beforeLayout, string replacedConnectionKey, string ownerTaskLayoutId,
            string sourceComponentLayoutId, string destinationComponentLayoutId, string anchorLayoutId,
            string packageId, string packageName, IDictionary<string, string> existingComponentMetadata)
        {
            SourceArtifactHash = sourceArtifactHash;
            OwnerTaskNativeId = ownerTaskNativeId;
            SourceComponentNativeId = sourceComponentNativeId;
            DestinationComponentNativeId = destinationComponentNativeId;
            PathNativeId = pathNativeId;
            SourceLineageId = sourceLineageId;
            SourceOutputName = sourceOutputName;
            SourceColumnName = sourceColumnName;
            ComponentName = componentName;
            OutputName = outputName;
            OutputDataType = outputDataType;
            Length = length;
            Precision = precision;
            Scale = scale;
            CodePage = codePage;
            PlacementIntent = placementIntent;
            Placement = placement;
            this.beforeLayout = beforeLayout;
            this.replacedConnectionKey = replacedConnectionKey;
            OwnerTaskLayoutId = ownerTaskLayoutId;
            SourceComponentLayoutId = sourceComponentLayoutId;
            DestinationComponentLayoutId = destinationComponentLayoutId;
            AnchorLayoutId = anchorLayoutId;
            PackageId = packageId;
            PackageName = packageName;
            this.existingComponentMetadata = new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(existingComponentMetadata, StringComparer.Ordinal));
            PathLayoutId = beforeLayout.Connections.Single(connection => connection.Key == replacedConnectionKey).LayoutId;
            Requirements = MutationExecutionPolicies.ComponentInsertion();
        }

        public string OperationName => "insert_data_conversion";
        public string SourceArtifactHash { get; }
        public string OwnerTaskNativeId { get; }
        public string SourceComponentNativeId { get; }
        public string DestinationComponentNativeId { get; }
        public string PathNativeId { get; }
        public string PathLayoutId { get; }
        public int SourceLineageId { get; }
        public string SourceOutputName { get; }
        public string SourceColumnName { get; }
        public string ComponentName { get; }
        public string OutputName { get; }
        public RuntimeWrapper.DataType OutputDataType { get; }
        public int Length { get; }
        public int Precision { get; }
        public int Scale { get; }
        public int CodePage { get; }
        public ComponentPlacementIntent PlacementIntent { get; }
        public DesignerPlacement Placement { get; }
        public MutationExecutionRequirements Requirements { get; }
        public string OwnerTaskLayoutId { get; }
        public string SourceComponentLayoutId { get; }
        public string DestinationComponentLayoutId { get; }
        public string AnchorLayoutId { get; }
        public string PackageId { get; }
        public string PackageName { get; }

        public static DataConversionInsertionPlan Create(Package package, string sourceArtifactHash,
            string ownerTaskNativeId, string sourceComponentNativeId, string destinationComponentNativeId,
            int sourceLineageId, string componentName, string outputName, RuntimeWrapper.DataType outputDataType,
            int length, int precision, int scale, int codePage, ComponentPlacementIntent placementIntent,
            string packagePath = null, string selectedPathNativeId = null)
        {
            if (package == null) { throw new ArgumentNullException(nameof(package)); }
            if (!MutationHash.IsSha256(sourceArtifactHash)) { throw new ArgumentException("A SHA-256 source hash is required.", nameof(sourceArtifactHash)); }
            if (!Enum.IsDefined(typeof(RuntimeWrapper.DataType), outputDataType) || !AllowedTypes.Contains(outputDataType))
            { throw new ArgumentOutOfRangeException(nameof(outputDataType)); }
            ValidateText(componentName, nameof(componentName), 128);
            ValidateText(outputName, nameof(outputName), 128);
            if (string.IsNullOrWhiteSpace(ownerTaskNativeId) || string.IsNullOrWhiteSpace(sourceComponentNativeId) ||
                string.IsNullOrWhiteSpace(destinationComponentNativeId))
            { throw new ArgumentException("Native task, source, and destination IDs are required."); }
            if (sourceLineageId <= 0) { throw new ArgumentOutOfRangeException(nameof(sourceLineageId)); }
            ValidateTypeParameters(outputDataType, length, precision, scale, codePage);
            if (placementIntent == null) { throw new ArgumentNullException(nameof(placementIntent)); }
            if (placementIntent.OwnerNativeId != ownerTaskNativeId) { throw new InvalidOperationException("mutation.insertion.placement_owner_mismatch"); }

            var task = FindPipelineTask(package, ownerTaskNativeId);
            if (package.Variables.Cast<Variable>().Any(variable => !variable.SystemVariable) ||
                task.Variables.Cast<Variable>().Any(variable => !variable.SystemVariable))
            { throw new InvalidOperationException("mutation.insertion.user_variables_unsupported"); }
            if (package.EventHandlers.Count != 0 || task.EventHandlers.Count != 0)
            { throw new InvalidOperationException("mutation.insertion.event_handlers_unsupported"); }

            var pipeline = (IDTSPipeline130)task.InnerObject;
            var source = FindComponent(pipeline, sourceComponentNativeId);
            var destination = FindComponent(pipeline, destinationComponentNativeId);
            var sourceColumns = source.OutputCollection.Cast<IDTSOutput100>().Where(output => !output.IsErrorOut)
                .SelectMany(output => output.OutputColumnCollection.Cast<IDTSOutputColumn100>()
                    .Where(column => column.LineageID == sourceLineageId).Select(column => new { Output = output, Column = column }))
                .ToArray();
            if (sourceColumns.Length != 1) { throw new InvalidOperationException("mutation.insertion.source_lineage_not_found"); }
            var sourceOutputs = source.OutputCollection.Cast<IDTSOutput100>().Where(output => !output.IsErrorOut).ToArray();
            var destinationInputs = destination.InputCollection.Cast<IDTSInput100>().ToArray();
            var paths = pipeline.PathCollection.Cast<IDTSPath100>().Where(path =>
                sourceOutputs.Any(output => output.ID == path.StartPoint.ID) &&
                destinationInputs.Any(input => input.ID == path.EndPoint.ID)).ToArray();
            var selectedPath = string.IsNullOrWhiteSpace(selectedPathNativeId)
                ? paths.Length == 1 ? paths[0] : null
                : paths.SingleOrDefault(path => path.ID.ToString(CultureInfo.InvariantCulture) == selectedPathNativeId);
            if (selectedPath == null)
            { throw new InvalidOperationException(string.IsNullOrWhiteSpace(selectedPathNativeId)
                ? "mutation.insertion.path_ambiguous" : "mutation.insertion.path_selection_mismatch"); }

            var layout = new DesignerLayoutExtractor().Extract(package, packagePath);
            var placementPreview = new DesignerPlacementPreviewer().Preview(package, placementIntent, packagePath);
            if (!placementPreview.IsValid) { throw new InvalidOperationException(placementPreview.RefusalCode); }
            var taskLayout = layout.Nodes.SingleOrDefault(node => node.OwnerNativeId == "package" && node.NativeId == ownerTaskNativeId);
            var sourceNode = layout.Nodes.SingleOrDefault(node => node.OwnerNativeId == ownerTaskNativeId && node.NativeId == sourceComponentNativeId);
            var destinationNode = layout.Nodes.SingleOrDefault(node => node.OwnerNativeId == ownerTaskNativeId && node.NativeId == destinationComponentNativeId);
            var anchorNode = layout.Nodes.SingleOrDefault(node => node.OwnerNativeId == ownerTaskNativeId && node.NativeId == placementIntent.AnchorNativeId);
            if (taskLayout == null || sourceNode == null || !sourceNode.HasPosition || destinationNode == null)
            { throw new InvalidOperationException("designer.layout.position_unavailable"); }
            if (anchorNode == null && placementIntent.Relation != ComponentPlacementRelation.OpenSpace)
            { throw new InvalidOperationException("designer.placement.target_not_found"); }
            var connection = layout.Connections.SingleOrDefault(item => item.NativeId == paths[0].ID.ToString(CultureInfo.InvariantCulture));
            if (connection == null) { throw new InvalidOperationException("designer.layout.connection_unresolved"); }
            var metadata = ComponentMetadata(package, taskLayout.LayoutId);

            return new DataConversionInsertionPlan(sourceArtifactHash, ownerTaskNativeId, sourceComponentNativeId,
                destinationComponentNativeId, selectedPath.ID.ToString(CultureInfo.InvariantCulture), sourceLineageId,
                sourceColumns[0].Output.Name, sourceColumns[0].Column.Name, componentName, outputName,
                outputDataType, length, precision, scale, codePage, placementIntent,
                placementPreview.Placement, layout, connection.Key, taskLayout.LayoutId, sourceNode.LayoutId,
                destinationNode.LayoutId, anchorNode?.LayoutId ?? string.Empty, package.ID, package.Name, metadata);
        }

        public static DataConversionInsertionPlan CreateForPath(Package package, string sourceArtifactHash,
            string ownerTaskNativeId, string pathNativeId, int sourceLineageId, string componentName,
            string outputName, RuntimeWrapper.DataType outputDataType, int length, int precision, int scale,
            int codePage, ComponentPlacementIntent placementIntent, string packagePath = null)
        {
            var task = FindPipelineTask(package, ownerTaskNativeId);
            var path = DataFlowPathSplicer.FindPath((IDTSPipeline130)task.InnerObject, pathNativeId);
            return Create(package, sourceArtifactHash, ownerTaskNativeId,
                path.StartPoint.Component.ID.ToString(CultureInfo.InvariantCulture),
                path.EndPoint.Component.ID.ToString(CultureInfo.InvariantCulture), sourceLineageId,
                componentName, outputName, outputDataType, length, precision, scale, codePage,
                placementIntent, packagePath, pathNativeId);
        }

        public string SerializePreviewContract()
        {
            var contract = new DataConversionInsertionPreviewContract(SourceArtifactHash, OwnerTaskLayoutId,
                PathLayoutId, SourceComponentLayoutId, DestinationComponentLayoutId, SourceColumnName, ComponentName, OutputName,
                OutputDataType.ToString(), Length, Precision, Scale, CodePage, PlacementIntent.Relation.ToString(),
                AnchorLayoutId, Placement.Position.Left, Placement.Position.Top, Placement.Position.Width,
                Placement.Position.Height, Requirements.Requirements);
            var serializer = new DataContractJsonSerializer(typeof(DataConversionInsertionPreviewContract));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, contract);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        public IComponentInsertionPlan RebindCurrent(Package package, string sourceArtifactHash, string packagePath = null)
        {
            if (!string.Equals(SourceArtifactHash, sourceArtifactHash, StringComparison.Ordinal))
            { throw new InvalidOperationException("mutation.source.hash_changed"); }
            var layout = new DesignerLayoutExtractor().Extract(package, packagePath);
            var taskNode = layout.Nodes.SingleOrDefault(node => node.OwnerNativeId == "package" && node.LayoutId == OwnerTaskLayoutId);
            if (taskNode == null) { throw new InvalidOperationException("mutation.insertion.task_identity_changed"); }
            var sourceNode = layout.Nodes.SingleOrDefault(node => node.OwnerNativeId == taskNode.NativeId && node.LayoutId == SourceComponentLayoutId);
            var destinationNode = layout.Nodes.SingleOrDefault(node => node.OwnerNativeId == taskNode.NativeId && node.LayoutId == DestinationComponentLayoutId);
            if (sourceNode == null || destinationNode == null) { throw new InvalidOperationException("mutation.insertion.component_identity_changed"); }
            var anchorNode = string.IsNullOrEmpty(AnchorLayoutId) ? null : layout.Nodes.SingleOrDefault(node =>
                node.OwnerNativeId == taskNode.NativeId && node.LayoutId == AnchorLayoutId);
            if (!string.IsNullOrEmpty(AnchorLayoutId) && anchorNode == null) { throw new InvalidOperationException("mutation.insertion.anchor_identity_changed"); }
            var selectedConnection = layout.Connections.SingleOrDefault(connection =>
                connection.OwnerNativeId == taskNode.NativeId && connection.Key == replacedConnectionKey);
            if (selectedConnection == null) { throw new InvalidOperationException("mutation.insertion.path_identity_changed"); }
            var task = FindPipelineTask(package, taskNode.NativeId);
            var source = FindComponent((IDTSPipeline130)task.InnerObject, sourceNode.NativeId);
            var lineage = source.OutputCollection.Cast<IDTSOutput100>().Where(output => output.Name == SourceOutputName)
                .SelectMany(output => output.OutputColumnCollection.Cast<IDTSOutputColumn100>())
                .SingleOrDefault(column => column.Name == SourceColumnName);
            if (lineage == null) { throw new InvalidOperationException("mutation.insertion.source_column_identity_changed"); }
            var intent = new ComponentPlacementIntent(taskNode.NativeId, PlacementIntent.Relation,
                anchorNode?.NativeId ?? string.Empty, PlacementIntent.Width, PlacementIntent.Height);
            var current = Create(package, sourceArtifactHash, taskNode.NativeId, sourceNode.NativeId, destinationNode.NativeId,
                lineage.LineageID, ComponentName, OutputName, OutputDataType, Length, Precision, Scale, CodePage,
                intent, packagePath, selectedConnection.NativeId);
            if (!Placement.Position.Equals(current.Placement.Position) ||
                !new DesignerLayoutComparer().Compare(beforeLayout, current.beforeLayout).Matches ||
                !string.Equals(replacedConnectionKey, current.replacedConnectionKey, StringComparison.Ordinal))
            { throw new InvalidOperationException("mutation.insertion.layout_changed"); }
            return current;
        }

        IPackageMutationPlan IPackageMutationPlan.RebindCurrent(Package package, string sourceArtifactHash,
            string packagePath) => RebindCurrent(package, sourceArtifactHash, packagePath);

        public void Apply(Package package)
        {
            var task = FindPipelineTask(package, OwnerTaskNativeId);
            var pipeline = (IDTSPipeline130)task.InnerObject;
            var creationName = new Application().PipelineComponentInfos.Cast<PipelineComponentInfo>()
                .Single(info => info.CreationName.StartsWith("DTSTransform.DataConvert.", StringComparison.OrdinalIgnoreCase)).CreationName;
            var component = pipeline.ComponentMetaDataCollection.New();
            component.ComponentClassID = creationName;
            var design = component.Instantiate();
            design.ProvideComponentProperties();
            component.Name = ComponentName;
            var output = component.OutputCollection.Cast<IDTSOutput100>().First(port => !port.IsErrorOut);
            DataFlowPathSplicer.Splice(pipeline, PathNativeId, component, input =>
            {
                design.SetUsageType(input.ID, input.GetVirtualInput(), SourceLineageId, DTSUsageType.UT_READONLY);
                var converted = design.InsertOutputColumnAt(output.ID, 0, OutputName, string.Empty);
                design.SetOutputColumnProperty(output.ID, converted.ID, "SourceInputColumnLineageID", SourceLineageId);
                design.SetOutputColumnDataTypeProperties(output.ID, converted.ID, OutputDataType, Length, Precision, Scale, CodePage);
            });
            new DesignerLayoutWriter().SetComponentPosition(package, OwnerTaskNativeId,
                component.ID.ToString(CultureInfo.InvariantCulture), Placement);
        }

        public string VerifyFailureCode(Package package)
        {
            if (package == null) { return "mutation.insertion.package_missing"; }
            try
            {
                if (package.ID != PackageId || package.Name != PackageName) { return "mutation.insertion.package_identity_changed"; }
                var layout = new DesignerLayoutExtractor().Extract(package);
                var taskNode = layout.Nodes.SingleOrDefault(node => node.OwnerNativeId == "package" && node.LayoutId == OwnerTaskLayoutId);
                if (taskNode == null) { return "mutation.insertion.task_identity_changed"; }
                var task = FindPipelineTask(package, taskNode.NativeId);
                var pipeline = (IDTSPipeline130)task.InnerObject;
                var components = pipeline.ComponentMetaDataCollection.Cast<IDTSComponentMetaData100>().ToArray();
                var sourceNode = layout.Nodes.SingleOrDefault(node => node.OwnerNativeId == task.ID && node.LayoutId == SourceComponentLayoutId);
                var destinationNode = layout.Nodes.SingleOrDefault(node => node.OwnerNativeId == task.ID && node.LayoutId == DestinationComponentLayoutId);
                if (sourceNode == null || destinationNode == null) { return "mutation.insertion.component_identity_changed"; }
                var source = FindComponent(pipeline, sourceNode.NativeId);
                var destination = FindComponent(pipeline, destinationNode.NativeId);
                var sourceColumn = source.OutputCollection.Cast<IDTSOutput100>()
                    .Where(port => !port.IsErrorOut && port.Name == SourceOutputName)
                    .SelectMany(port => port.OutputColumnCollection.Cast<IDTSOutputColumn100>())
                    .SingleOrDefault(item => item.Name == SourceColumnName);
                if (sourceColumn == null) { return "mutation.insertion.source_column_identity_changed"; }
                var existing = ComponentMetadata(package, OwnerTaskLayoutId);
                if (!existing.Remove(OwnerTaskLayoutId + "\\" + ComponentName) || existing.Count != existingComponentMetadata.Count ||
                    existingComponentMetadata.Any(pair => !existing.TryGetValue(pair.Key, out var value) || value != pair.Value))
                { return "mutation.insertion.existing_component_metadata_changed"; }
                var inserted = components.Where(component => component.Name == ComponentName).ToArray();
                if (components.Length != beforeLayout.Nodes.Count(node => node.OwnerNativeId == OwnerTaskNativeId) + 1 || inserted.Length != 1)
                { return "mutation.insertion.component_inventory_changed"; }
                if (pipeline.PathCollection.Count != beforeLayout.Connections.Count(connection => connection.OwnerNativeId == OwnerTaskNativeId) + 1)
                { return "mutation.insertion.path_inventory_changed"; }
                var output = inserted[0].OutputCollection.Cast<IDTSOutput100>().Single(port => !port.IsErrorOut);
                var column = output.OutputColumnCollection.Cast<IDTSOutputColumn100>().Single(item => item.Name == OutputName);
                if (column.DataType != OutputDataType || column.Length != Length || column.Precision != Precision || column.Scale != Scale || column.CodePage != CodePage)
                { return "mutation.insertion.output_metadata_changed"; }
                var sourceInputId = inserted[0].InputCollection[0].ID;
                var mapping = inserted[0].OutputCollection.Cast<IDTSOutput100>().SelectMany(port => port.OutputColumnCollection.Cast<IDTSOutputColumn100>())
                    .Single(item => item.Name == OutputName).CustomPropertyCollection.Cast<IDTSCustomProperty100>()
                    .SingleOrDefault(property => property.Name == "SourceInputColumnLineageID");
                if (mapping == null || Convert.ToInt32(mapping.Value, CultureInfo.InvariantCulture) != sourceColumn.LineageID)
                { return "mutation.insertion.source_mapping_changed"; }
                var paths = pipeline.PathCollection.Cast<IDTSPath100>().ToArray();
                if (!paths.Any(path => path.StartPoint.Component.ID == source.ID && path.EndPoint.Component.ID == inserted[0].ID) ||
                    !paths.Any(path => path.StartPoint.Component.ID == inserted[0].ID && path.EndPoint.Component.ID == destination.ID))
                { return "mutation.insertion.path_endpoints_changed"; }
                var node = layout.Nodes.SingleOrDefault(item => item.OwnerNativeId == task.ID && item.LayoutId == OwnerTaskLayoutId + "\\" + ComponentName);
                if (node == null || !node.HasPosition || !node.Position.Equals(Placement.Position))
                { return "mutation.insertion.placement_not_preserved"; }
                if (!new DesignerLayoutComparer().ComparePreservingExisting(beforeLayout, layout,
                    explicitlyReplacedConnectionKeys: new[] { replacedConnectionKey }).Matches)
                { return "mutation.insertion.existing_layout_changed"; }
                return string.Empty;
            }
            catch (Exception) { return "mutation.insertion.verification_failed"; }
        }

        private static readonly RuntimeWrapper.DataType[] AllowedTypes = {
            RuntimeWrapper.DataType.DT_WSTR, RuntimeWrapper.DataType.DT_STR,
            RuntimeWrapper.DataType.DT_I2, RuntimeWrapper.DataType.DT_I4, RuntimeWrapper.DataType.DT_I8,
            RuntimeWrapper.DataType.DT_R4, RuntimeWrapper.DataType.DT_R8
        };

        private static void ValidateTypeParameters(RuntimeWrapper.DataType type, int length, int precision, int scale, int codePage)
        {
            if (type == RuntimeWrapper.DataType.DT_WSTR && (length < 1 || length > 4000)) { throw new ArgumentOutOfRangeException(nameof(length)); }
            if (type == RuntimeWrapper.DataType.DT_STR && (length < 1 || length > 8000 || codePage <= 0)) { throw new ArgumentOutOfRangeException(nameof(length)); }
            if (type != RuntimeWrapper.DataType.DT_WSTR && type != RuntimeWrapper.DataType.DT_STR && (length != 0 || codePage != 0))
            { throw new ArgumentException("Length/code page only apply to string conversions."); }
            if (precision != 0 || scale != 0) { throw new ArgumentException("Decimal conversions are not supported by this plan."); }
        }

        private static void ValidateText(string value, string name, int maximumLength)
        { if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength || value.Any(char.IsControl)) { throw new ArgumentException("A bounded non-empty value is required.", name); } }

        private static TaskHost FindPipelineTask(Package package, string nativeId)
        {
            var matches = FindTaskHosts(package).Where(task => task.ID == nativeId && task.InnerObject is IDTSPipeline130).ToArray();
            if (matches.Length != 1) { throw new InvalidOperationException("mutation.insertion.task_not_found_or_ambiguous"); }
            return matches[0];
        }

        private static IDTSComponentMetaData100 FindComponent(IDTSPipeline130 pipeline, string nativeId)
        {
            var matches = pipeline.ComponentMetaDataCollection.Cast<IDTSComponentMetaData100>()
                .Where(component => component.ID.ToString(CultureInfo.InvariantCulture) == nativeId).ToArray();
            if (matches.Length != 1) { throw new InvalidOperationException("mutation.insertion.component_not_found_or_ambiguous"); }
            return matches[0];
        }

        private static Dictionary<string, string> ComponentMetadata(Package package, string taskLayoutId)
        {
            package.SaveToXML(out var xml, null);
            var document = XDocument.Parse(xml);
            var prefix = taskLayoutId + "\\";
            var components = document.Descendants().Where(element => element.Name.LocalName == "component")
                .Select(element => new { Id = (string)element.Attribute("refId"), Xml = element.ToString(SaveOptions.DisableFormatting) })
                .Where(item => item.Id != null && item.Id.StartsWith(prefix, StringComparison.Ordinal))
                .ToDictionary(item => item.Id, item => item.Xml, StringComparer.Ordinal);
            if (components.Count == 0) { throw new InvalidOperationException("mutation.insertion.component_metadata_missing"); }
            return components;
        }

        private static IEnumerable<TaskHost> FindTaskHosts(Package package)
        {
            foreach (Executable executable in package.Executables)
            {
                foreach (var task in FindTaskHosts((DtsContainer)executable)) { yield return task; }
            }
        }

        private static IEnumerable<TaskHost> FindTaskHosts(DtsContainer container)
        {
            if (container is TaskHost task) { yield return task; }
            if (container is IDTSSequence sequence)
            {
                foreach (Executable executable in sequence.Executables)
                {
                    foreach (var nestedTask in FindTaskHosts((DtsContainer)executable)) { yield return nestedTask; }
                }
            }
        }
    }

    [DataContract]
    public sealed class DataConversionInsertionPreviewContract
    {
        internal DataConversionInsertionPreviewContract(string sourceHash, string owner, string pathLayoutId, string source, string destination,
            string sourceColumn, string component, string output, string dataType, int length, int precision, int scale,
            int codePage, string relation, string anchor, decimal left, decimal top, decimal width, decimal height,
            IEnumerable<string> requirements)
        {
            SchemaVersion = "1.0"; Operation = "InsertDataConversion"; SourceArtifactHash = sourceHash;
            OwnerLayoutId = owner; PathLayoutId = pathLayoutId; SourceComponentLayoutId = source; DestinationComponentLayoutId = destination;
            SourceColumnName = sourceColumn; ComponentName = component; OutputName = output; OutputDataType = dataType;
            Length = length; Precision = precision; Scale = scale; CodePage = codePage; PlacementRelation = relation;
            AnchorLayoutId = anchor; Placement = new DerivedColumnPlacementContract(left, top, width, height);
            Requirements = requirements.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        }
        [DataMember(Order = 0)] public string SchemaVersion { get; private set; }
        [DataMember(Order = 1)] public string Operation { get; private set; }
        [DataMember(Order = 2)] public string SourceArtifactHash { get; private set; }
        [DataMember(Order = 3)] public string OwnerLayoutId { get; private set; }
        [DataMember(Order = 4)] public string PathLayoutId { get; private set; }
        [DataMember(Order = 5)] public string SourceComponentLayoutId { get; private set; }
        [DataMember(Order = 6)] public string DestinationComponentLayoutId { get; private set; }
        [DataMember(Order = 7)] public string SourceColumnName { get; private set; }
        [DataMember(Order = 8)] public string ComponentName { get; private set; }
        [DataMember(Order = 9)] public string OutputName { get; private set; }
        [DataMember(Order = 10)] public string OutputDataType { get; private set; }
        [DataMember(Order = 11)] public int Length { get; private set; }
        [DataMember(Order = 12)] public int Precision { get; private set; }
        [DataMember(Order = 13)] public int Scale { get; private set; }
        [DataMember(Order = 14)] public int CodePage { get; private set; }
        [DataMember(Order = 15)] public string PlacementRelation { get; private set; }
        [DataMember(Order = 16)] public string AnchorLayoutId { get; private set; }
        [DataMember(Order = 17)] public DerivedColumnPlacementContract Placement { get; private set; }
        [DataMember(Order = 18)] public string[] Requirements { get; private set; }
    }
}
