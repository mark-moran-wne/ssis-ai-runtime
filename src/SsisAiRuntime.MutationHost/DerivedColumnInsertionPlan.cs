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
    public interface IComponentInsertionPlan
    {
        string SourceArtifactHash { get; }
        string OperationName { get; }
        MutationExecutionRequirements Requirements { get; }
        IComponentInsertionPlan RebindCurrent(Package package, string sourceArtifactHash, string packagePath = null);
        void Apply(Package package);
        string VerifyFailureCode(Package package);
        string SerializePreviewContract();
    }

    public sealed class DerivedColumnInsertionPlan : IComponentInsertionPlan
    {
        private readonly DesignerLayoutSnapshot beforeLayout;
        private readonly string replacedConnectionKey;
        private readonly IReadOnlyDictionary<string, string> existingComponentMetadata;

        private DerivedColumnInsertionPlan(string sourceArtifactHash, string ownerTaskNativeId,
            string sourceComponentNativeId, string destinationComponentNativeId, int sourceLineageId,
            string componentName, string outputName, string expression, ComponentPlacementIntent placementIntent,
            DesignerPlacement placement, DesignerLayoutSnapshot beforeLayout, string replacedConnectionKey,
            string ownerTaskLayoutId, string sourceComponentLayoutId, string destinationComponentLayoutId,
            string sourceOutputName, string sourceColumnName, string anchorLayoutId,
            string packageId, string packageName, IDictionary<string, string> existingComponentMetadata)
        {
            SourceArtifactHash = sourceArtifactHash;
            OwnerTaskNativeId = ownerTaskNativeId;
            SourceComponentNativeId = sourceComponentNativeId;
            DestinationComponentNativeId = destinationComponentNativeId;
            SourceLineageId = sourceLineageId;
            ComponentName = componentName;
            OutputName = outputName;
            Expression = expression;
            PlacementIntent = placementIntent;
            Placement = placement;
            this.beforeLayout = beforeLayout;
            this.replacedConnectionKey = replacedConnectionKey;
            OwnerTaskLayoutId = ownerTaskLayoutId;
            SourceComponentLayoutId = sourceComponentLayoutId;
            DestinationComponentLayoutId = destinationComponentLayoutId;
            SourceOutputName = sourceOutputName;
            SourceColumnName = sourceColumnName;
            AnchorLayoutId = anchorLayoutId;
            PackageId = packageId;
            PackageName = packageName;
            this.existingComponentMetadata = new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(existingComponentMetadata, StringComparer.Ordinal));
            Requirements = MutationExecutionPolicies.ComponentInsertion();
        }

        public string SourceArtifactHash { get; }
        public string OperationName => "insert_derived_column";
        public string OwnerTaskNativeId { get; }
        public string SourceComponentNativeId { get; }
        public string DestinationComponentNativeId { get; }
        public int SourceLineageId { get; }
        public string ComponentName { get; }
        public string OutputName { get; }
        public string Expression { get; }
        public ComponentPlacementIntent PlacementIntent { get; }
        public DesignerPlacement Placement { get; }
        public MutationExecutionRequirements Requirements { get; }
        public string OwnerTaskLayoutId { get; }
        public string SourceComponentLayoutId { get; }
        public string DestinationComponentLayoutId { get; }
        public string SourceOutputName { get; }
        public string SourceColumnName { get; }
        public string AnchorLayoutId { get; }
        public string PackageId { get; }
        public string PackageName { get; }

        public string SerializePreviewContract()
        {
            var contract = new DerivedColumnInsertionPreviewContract(SourceArtifactHash, OwnerTaskLayoutId,
                SourceComponentLayoutId, DestinationComponentLayoutId, SourceColumnName, ComponentName,
                OutputName, PlacementIntent.Relation.ToString(), AnchorLayoutId,
                Placement.Position.Left, Placement.Position.Top, Placement.Position.Width, Placement.Position.Height,
                Requirements.Requirements);
            var serializer = new DataContractJsonSerializer(typeof(DerivedColumnInsertionPreviewContract));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, contract);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        IComponentInsertionPlan IComponentInsertionPlan.RebindCurrent(Package package, string sourceArtifactHash,
            string packagePath) => RebindCurrent(package, sourceArtifactHash, packagePath);

        public static DerivedColumnInsertionPlan Create(Package package, string sourceArtifactHash,
            string ownerTaskNativeId, string sourceComponentNativeId, string destinationComponentNativeId,
            int sourceLineageId, string componentName, string outputName, string expression,
            ComponentPlacementIntent placementIntent, string packagePath = null)
        {
            if (package == null) { throw new ArgumentNullException(nameof(package)); }
            if (!MutationHash.IsSha256(sourceArtifactHash)) { throw new ArgumentException("A SHA-256 source hash is required.", nameof(sourceArtifactHash)); }
            if (string.IsNullOrWhiteSpace(ownerTaskNativeId) || string.IsNullOrWhiteSpace(sourceComponentNativeId) ||
                string.IsNullOrWhiteSpace(destinationComponentNativeId))
            { throw new ArgumentException("Native task, source, and destination IDs are required."); }
            ValidateText(componentName, nameof(componentName), 128);
            ValidateText(outputName, nameof(outputName), 128);
            ValidateText(expression, nameof(expression), 4096);
            if (sourceLineageId <= 0) { throw new ArgumentOutOfRangeException(nameof(sourceLineageId)); }
            if (placementIntent == null) { throw new ArgumentNullException(nameof(placementIntent)); }
            if (placementIntent.OwnerNativeId != ownerTaskNativeId) { throw new InvalidOperationException("mutation.insertion.placement_owner_mismatch"); }

            var task = FindPipelineTask(package, ownerTaskNativeId);
            if (package.Executables.Count != 1 || FindTaskHosts(package).Count() != 1)
            { throw new InvalidOperationException("mutation.insertion.package_shape_unsupported"); }
            if (package.Connections.Count != 0) { throw new InvalidOperationException("mutation.insertion.package_connections_unsupported"); }
            if (package.Variables.Cast<Variable>().Any(variable => !variable.SystemVariable))
            { throw new InvalidOperationException("mutation.insertion.package_variables_unsupported"); }
            if (task.Variables.Cast<Variable>().Any(variable => !variable.SystemVariable))
            { throw new InvalidOperationException("mutation.insertion.task_variables_unsupported"); }
            if (package.EventHandlers.Count != 0 || task.EventHandlers.Count != 0)
            { throw new InvalidOperationException("mutation.insertion.event_handlers_unsupported"); }
            var pipeline = (IDTSPipeline130)task.InnerObject;
            if (pipeline.ComponentMetaDataCollection.Count != 2 || pipeline.PathCollection.Count != 1)
            { throw new InvalidOperationException("mutation.insertion.pipeline_shape_unsupported"); }
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
            if (paths.Length != 1) { throw new InvalidOperationException("mutation.insertion.path_ambiguous"); }

            var layout = new DesignerLayoutExtractor().Extract(package, packagePath);
            var preview = new DesignerPlacementPreviewer().Preview(package, placementIntent, packagePath);
            if (!preview.IsValid) { throw new InvalidOperationException(preview.RefusalCode); }
            var taskLayoutNode = layout.Nodes.SingleOrDefault(node => node.OwnerNativeId == "package" &&
                node.NativeId == ownerTaskNativeId);
            if (taskLayoutNode == null) { throw new InvalidOperationException("mutation.insertion.task_layout_identity_missing"); }
            var sourceNode = layout.Nodes.SingleOrDefault(node => node.OwnerNativeId == ownerTaskNativeId &&
                node.NativeId == sourceComponentNativeId);
            var destinationNode = layout.Nodes.SingleOrDefault(node => node.OwnerNativeId == ownerTaskNativeId &&
                node.NativeId == destinationComponentNativeId);
            if (sourceNode == null || !sourceNode.HasPosition)
            { throw new InvalidOperationException("designer.layout.position_unavailable"); }
            if (destinationNode == null) { throw new InvalidOperationException("mutation.insertion.destination_layout_identity_missing"); }
            var anchorNode = layout.Nodes.SingleOrDefault(node => node.OwnerNativeId == ownerTaskNativeId &&
                node.NativeId == placementIntent.AnchorNativeId);
            if (anchorNode == null && placementIntent.Relation != ComponentPlacementRelation.OpenSpace)
            { throw new InvalidOperationException("designer.placement.target_not_found"); }
            var existingComponentMetadata = ComponentMetadata(package, taskLayoutNode.LayoutId);
            var connection = layout.Connections.SingleOrDefault(item => item.NativeId == paths[0].ID.ToString(CultureInfo.InvariantCulture));
            if (connection == null) { throw new InvalidOperationException("designer.layout.connection_unresolved"); }

            return new DerivedColumnInsertionPlan(sourceArtifactHash, ownerTaskNativeId, sourceComponentNativeId,
                destinationComponentNativeId, sourceLineageId, componentName, outputName, expression,
                placementIntent, preview.Placement, layout, connection.Key, taskLayoutNode.LayoutId,
                sourceNode.LayoutId, destinationNode.LayoutId, sourceColumns[0].Output.Name,
                sourceColumns[0].Column.Name, anchorNode?.LayoutId ?? string.Empty,
                package.ID, package.Name, existingComponentMetadata);
        }

        public DerivedColumnInsertionPlan RebindCurrent(Package package, string sourceArtifactHash, string packagePath = null)
        {
            if (!string.Equals(SourceArtifactHash, sourceArtifactHash, StringComparison.Ordinal))
            { throw new InvalidOperationException("mutation.source.hash_changed"); }
            var currentLayout = new DesignerLayoutExtractor().Extract(package, packagePath);
            var taskNode = currentLayout.Nodes.SingleOrDefault(node => node.OwnerNativeId == "package" &&
                node.LayoutId == OwnerTaskLayoutId);
            if (taskNode == null) { throw new InvalidOperationException("mutation.insertion.task_identity_changed"); }
            var sourceNode = currentLayout.Nodes.SingleOrDefault(node => node.OwnerNativeId == taskNode.NativeId &&
                node.LayoutId == SourceComponentLayoutId);
            var destinationNode = currentLayout.Nodes.SingleOrDefault(node => node.OwnerNativeId == taskNode.NativeId &&
                node.LayoutId == DestinationComponentLayoutId);
            if (sourceNode == null || destinationNode == null)
            { throw new InvalidOperationException("mutation.insertion.component_identity_changed"); }
            var anchorNode = string.IsNullOrEmpty(AnchorLayoutId) ? null : currentLayout.Nodes.SingleOrDefault(node =>
                node.OwnerNativeId == taskNode.NativeId && node.LayoutId == AnchorLayoutId);
            if (!string.IsNullOrEmpty(AnchorLayoutId) && anchorNode == null)
            { throw new InvalidOperationException("mutation.insertion.anchor_identity_changed"); }
            var task = FindPipelineTask(package, taskNode.NativeId);
            var pipeline = (IDTSPipeline130)task.InnerObject;
            var source = FindComponent(pipeline, sourceNode.NativeId);
            var lineage = source.OutputCollection.Cast<IDTSOutput100>()
                .Where(output => output.Name == SourceOutputName)
                .SelectMany(output => output.OutputColumnCollection.Cast<IDTSOutputColumn100>())
                .SingleOrDefault(column => column.Name == SourceColumnName);
            if (lineage == null) { throw new InvalidOperationException("mutation.insertion.source_column_identity_changed"); }
            var currentIntent = new ComponentPlacementIntent(taskNode.NativeId, PlacementIntent.Relation,
                anchorNode?.NativeId ?? string.Empty, PlacementIntent.Width, PlacementIntent.Height);
            var current = Create(package, sourceArtifactHash, taskNode.NativeId, sourceNode.NativeId,
                destinationNode.NativeId, lineage.LineageID, ComponentName, OutputName, Expression,
                currentIntent, packagePath);
            if (!Placement.Position.Equals(current.Placement.Position))
            { throw new InvalidOperationException("mutation.insertion.placement_changed"); }
            if (!new DesignerLayoutComparer().Compare(beforeLayout, current.beforeLayout).Matches ||
                !string.Equals(replacedConnectionKey, current.replacedConnectionKey, StringComparison.Ordinal))
            { throw new InvalidOperationException("mutation.insertion.layout_changed"); }
            return current;
        }

        public bool MatchesCurrent(Package package, string sourceArtifactHash, string packagePath = null)
        {
            try { RebindCurrent(package, sourceArtifactHash, packagePath); return true; }
            catch (Exception) { return false; }
        }

        public void Apply(Package package)
        {
            if (package == null) { throw new ArgumentNullException(nameof(package)); }
            var task = FindPipelineTask(package, OwnerTaskNativeId);
            var pipeline = (IDTSPipeline130)task.InnerObject;
            var source = FindComponent(pipeline, SourceComponentNativeId);
            var destination = FindComponent(pipeline, DestinationComponentNativeId);
            var sourceOutput = source.OutputCollection.Cast<IDTSOutput100>().Where(output => !output.IsErrorOut)
                .Single(output => output.OutputColumnCollection.Cast<IDTSOutputColumn100>()
                    .Any(column => column.LineageID == SourceLineageId));
            var destinationInput = destination.InputCollection.Cast<IDTSInput100>()
                .Single(input => pipeline.PathCollection.Cast<IDTSPath100>().Any(path =>
                    path.StartPoint.ID == sourceOutput.ID && path.EndPoint.ID == input.ID));
            var oldPath = pipeline.PathCollection.Cast<IDTSPath100>().Single(path =>
                path.StartPoint.ID == sourceOutput.ID && path.EndPoint.ID == destinationInput.ID);

            var application = new Application();
            var creationName = application.PipelineComponentInfos.Cast<PipelineComponentInfo>()
                .Single(info => info.CreationName.StartsWith("DTSTransform.DerivedColumn.", StringComparison.OrdinalIgnoreCase))
                .CreationName;
            var component = pipeline.ComponentMetaDataCollection.New();
            component.ComponentClassID = creationName;
            var design = component.Instantiate();
            design.ProvideComponentProperties();
            component.Name = ComponentName;
            var input = component.InputCollection[0];
            pipeline.PathCollection.RemoveObjectByID(oldPath.ID);
            pipeline.PathCollection.New().AttachPathAndPropagateNotifications(sourceOutput, input);
            design.SetUsageType(input.ID, input.GetVirtualInput(), SourceLineageId, DTSUsageType.UT_READONLY);
            var output = component.OutputCollection.Cast<IDTSOutput100>().Single(port => !port.IsErrorOut);
            var outputColumn = design.InsertOutputColumnAt(output.ID, 0, OutputName, string.Empty);
            design.SetOutputColumnDataTypeProperties(output.ID, outputColumn.ID,
                RuntimeWrapper.DataType.DT_I4, 0, 0, 0, 0);
            design.SetOutputColumnProperty(output.ID, outputColumn.ID, "Expression", Expression);
            pipeline.PathCollection.New().AttachPathAndPropagateNotifications(output, destinationInput);
            new DesignerLayoutWriter().SetComponentPosition(package, OwnerTaskNativeId,
                component.ID.ToString(CultureInfo.InvariantCulture), Placement);
        }

        public bool Verify(Package package)
        {
            return VerifyFailureCode(package).Length == 0;
        }

        public string VerifyFailureCode(Package package)
        {
            if (package == null) { return "mutation.insertion.package_missing"; }
            try
            {
                if (!string.Equals(package.ID, PackageId, StringComparison.Ordinal) ||
                    !string.Equals(package.Name, PackageName, StringComparison.Ordinal))
                { return "mutation.insertion.package_identity_changed"; }
                var layout = new DesignerLayoutExtractor().Extract(package);
                var taskLayoutNode = layout.Nodes.SingleOrDefault(node => node.OwnerNativeId == "package" &&
                    node.LayoutId == OwnerTaskLayoutId);
                if (taskLayoutNode == null) { return "mutation.insertion.task_identity_changed"; }
                var task = FindPipelineTask(package, taskLayoutNode.NativeId);
                var pipeline = (IDTSPipeline130)task.InnerObject;
                var components = pipeline.ComponentMetaDataCollection.Cast<IDTSComponentMetaData100>().ToArray();
                var sourceLayoutNode = layout.Nodes.SingleOrDefault(node => node.OwnerNativeId == task.ID &&
                    node.LayoutId == SourceComponentLayoutId);
                var destinationLayoutNode = layout.Nodes.SingleOrDefault(node => node.OwnerNativeId == task.ID &&
                    node.LayoutId == DestinationComponentLayoutId);
                if (sourceLayoutNode == null || destinationLayoutNode == null)
                { return "mutation.insertion.component_identity_changed"; }
                var source = FindComponent(pipeline, sourceLayoutNode.NativeId);
                var destination = FindComponent(pipeline, destinationLayoutNode.NativeId);
                var currentComponentMetadata = ComponentMetadata(package, OwnerTaskLayoutId);
                if (!currentComponentMetadata.Remove(OwnerTaskLayoutId + "\\" + ComponentName) ||
                    currentComponentMetadata.Count != existingComponentMetadata.Count ||
                    existingComponentMetadata.Any(pair => !currentComponentMetadata.TryGetValue(pair.Key, out var value) ||
                        !string.Equals(pair.Value, value, StringComparison.Ordinal)))
                { return "mutation.insertion.existing_component_metadata_changed"; }
                var inserted = components.Where(component => component.Name == ComponentName).ToArray();
                if (components.Length != beforeLayout.Nodes.Count(node => node.OwnerNativeId == OwnerTaskNativeId) + 1 || inserted.Length != 1)
                { return "mutation.insertion.component_inventory_changed"; }
                if (pipeline.PathCollection.Count != beforeLayout.Connections.Count + 1)
                { return "mutation.insertion.path_inventory_changed"; }
                var insertedComponent = inserted[0];
                var output = insertedComponent.OutputCollection.Cast<IDTSOutput100>().Single(port => !port.IsErrorOut);
                var column = output.OutputColumnCollection.Cast<IDTSOutputColumn100>().Single(item => item.Name == OutputName);
                var expression = column.CustomPropertyCollection.Cast<IDTSCustomProperty100>()
                    .SingleOrDefault(property => property.Name == "Expression");
                var sourceColumn = source.OutputCollection.Cast<IDTSOutput100>()
                    .Where(port => !port.IsErrorOut && port.Name == SourceOutputName)
                    .SelectMany(port => port.OutputColumnCollection.Cast<IDTSOutputColumn100>())
                    .SingleOrDefault(item => item.Name == SourceColumnName);
                if (expression == null || sourceColumn == null) { return "mutation.insertion.expression_metadata_missing"; }
                if (column.DataType != RuntimeWrapper.DataType.DT_I4)
                { return "mutation.insertion.output_type_changed"; }
                var actualExpression = Convert.ToString(expression.Value, CultureInfo.InvariantCulture);
                var normalizedNumericExpression = Expression.Replace("#" + SourceLineageId,
                    "#" + sourceColumn.LineageID.ToString(CultureInfo.InvariantCulture));
                string serializedLineage = string.Empty;
                string serializedPackage;
                package.SaveToXML(out serializedPackage, null);
                var serializedXml = XDocument.Parse(serializedPackage);
                var sourceColumnXml = serializedXml.Descendants().SingleOrDefault(element =>
                    element.Name.LocalName == "outputColumn" && (string)element.Attribute("name") == SourceColumnName);
                if (sourceColumnXml != null) { serializedLineage = (string)sourceColumnXml.Attribute("lineageId") ?? string.Empty; }
                var normalizedReferenceExpression = serializedLineage.Length == 0 ? string.Empty :
                    Expression.Replace("#" + SourceLineageId, "#{" + serializedLineage + "}");
                if (!string.Equals(actualExpression, Expression, StringComparison.Ordinal) &&
                    !string.Equals(actualExpression, normalizedNumericExpression, StringComparison.Ordinal) &&
                    !string.Equals(actualExpression, normalizedReferenceExpression, StringComparison.Ordinal))
                { return "mutation.insertion.expression_changed"; }
                var paths = pipeline.PathCollection.Cast<IDTSPath100>().ToArray();
                if (!paths.Any(path => path.StartPoint.Component.ID == source.ID && path.EndPoint.Component.ID == insertedComponent.ID) ||
                    !paths.Any(path => path.StartPoint.Component.ID == insertedComponent.ID && path.EndPoint.Component.ID == destination.ID))
                { return "mutation.insertion.path_endpoints_changed"; }
                var node = layout.Nodes.SingleOrDefault(item => item.OwnerNativeId == task.ID &&
                    item.LayoutId == OwnerTaskLayoutId + "\\" + ComponentName);
                if (node == null || !node.HasPosition || !node.Position.Equals(Placement.Position))
                { return "mutation.insertion.placement_not_preserved"; }
                if (!new DesignerLayoutComparer().ComparePreservingExisting(beforeLayout, layout,
                    explicitlyReplacedConnectionKeys: new[] { replacedConnectionKey }).Matches)
                { return "mutation.insertion.existing_layout_changed"; }
                return string.Empty;
            }
            catch (InvalidOperationException exception) when (IsSafeCode(exception.Message))
            { return exception.Message; }
            catch (Exception) { return "mutation.insertion.verification_failed"; }
        }

        private static bool IsSafeCode(string value) => !string.IsNullOrWhiteSpace(value) &&
            value.All(character => char.IsLetterOrDigit(character) || character == '.' || character == '_') &&
            (value.StartsWith("mutation.", StringComparison.Ordinal) || value.StartsWith("designer.", StringComparison.Ordinal));

        private static TaskHost FindPipelineTask(Package package, string taskId)
        {
            var tasks = FindTaskHosts(package).Where(task => task.ID == taskId && task.InnerObject is IDTSPipeline130).ToArray();
            if (tasks.Length != 1) { throw new InvalidOperationException("mutation.insertion.task_not_found_or_ambiguous"); }
            return tasks[0];
        }

        private static IDTSComponentMetaData100 FindComponent(IDTSPipeline130 pipeline, string componentId)
        {
            var matches = pipeline.ComponentMetaDataCollection.Cast<IDTSComponentMetaData100>()
                .Where(component => component.ID.ToString(CultureInfo.InvariantCulture) == componentId).ToArray();
            if (matches.Length != 1) { throw new InvalidOperationException("mutation.insertion.component_not_found_or_ambiguous"); }
            return matches[0];
        }

        private static void ValidateText(string value, string name, int maximumLength)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength || value.Any(char.IsControl))
            { throw new ArgumentException("A bounded non-empty value is required.", name); }
        }

        private static Dictionary<string, string> ComponentMetadata(Package package, string ownerTaskLayoutId)
        {
            string xml;
            package.SaveToXML(out xml, null);
            var document = XDocument.Parse(xml);
            var prefix = ownerTaskLayoutId + "\\";
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var component in document.Descendants().Where(element => element.Name.LocalName == "component"))
            {
                var referenceId = (string)component.Attribute("refId");
                if (referenceId != null && referenceId.StartsWith(prefix, StringComparison.Ordinal))
                { result.Add(referenceId, component.ToString(SaveOptions.DisableFormatting)); }
            }
            if (result.Count == 0) { throw new InvalidOperationException("mutation.insertion.component_metadata_missing"); }
            return result;
        }

        private static System.Collections.Generic.IEnumerable<TaskHost> FindTaskHosts(Package package)
        {
            foreach (Executable executable in package.Executables)
            {
                foreach (var task in FindTaskHosts((DtsContainer)executable)) { yield return task; }
            }
        }

        private static System.Collections.Generic.IEnumerable<TaskHost> FindTaskHosts(DtsContainer container)
        {
            if (container is TaskHost task) { yield return task; }
            if (container is IDTSSequence sequence)
            {
                foreach (Executable executable in sequence.Executables)
                {
                    foreach (var nested in FindTaskHosts((DtsContainer)executable)) { yield return nested; }
                }
            }
        }
    }

    [DataContract]
    public sealed class DerivedColumnInsertionPreviewContract
    {
        internal DerivedColumnInsertionPreviewContract(string sourceArtifactHash, string ownerLayoutId,
            string sourceComponentLayoutId, string destinationComponentLayoutId, string sourceColumnName,
            string componentName, string outputName, string placementRelation, string anchorLayoutId,
            decimal left, decimal top, decimal width, decimal height, System.Collections.Generic.IEnumerable<string> requirements)
        {
            SchemaVersion = "1.0";
            SourceArtifactHash = sourceArtifactHash;
            OwnerLayoutId = ownerLayoutId;
            SourceComponentLayoutId = sourceComponentLayoutId;
            DestinationComponentLayoutId = destinationComponentLayoutId;
            SourceColumnName = sourceColumnName;
            ComponentName = componentName;
            OutputName = outputName;
            PlacementRelation = placementRelation;
            AnchorLayoutId = anchorLayoutId;
            Placement = new DerivedColumnPlacementContract(left, top, width, height);
            Requirements = requirements.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        }

        [DataMember(Order = 0)] public string SchemaVersion { get; private set; }
        [DataMember(Order = 1)] public string Operation { get; private set; } = "InsertDerivedColumn";
        [DataMember(Order = 2)] public string SourceArtifactHash { get; private set; }
        [DataMember(Order = 3)] public string OwnerLayoutId { get; private set; }
        [DataMember(Order = 4)] public string SourceComponentLayoutId { get; private set; }
        [DataMember(Order = 5)] public string DestinationComponentLayoutId { get; private set; }
        [DataMember(Order = 6)] public string SourceColumnName { get; private set; }
        [DataMember(Order = 7)] public string ComponentName { get; private set; }
        [DataMember(Order = 8)] public string OutputName { get; private set; }
        [DataMember(Order = 9)] public string PlacementRelation { get; private set; }
        [DataMember(Order = 10)] public string AnchorLayoutId { get; private set; }
        [DataMember(Order = 11)] public DerivedColumnPlacementContract Placement { get; private set; }
        [DataMember(Order = 12)] public string[] Requirements { get; private set; }
    }

    [DataContract]
    public sealed class DerivedColumnPlacementContract
    {
        internal DerivedColumnPlacementContract(decimal left, decimal top, decimal width, decimal height)
        { Left = left; Top = top; Width = width; Height = height; }

        [DataMember(Order = 0)] public decimal Left { get; private set; }
        [DataMember(Order = 1)] public decimal Top { get; private set; }
        [DataMember(Order = 2)] public decimal Width { get; private set; }
        [DataMember(Order = 3)] public decimal Height { get; private set; }
    }
}