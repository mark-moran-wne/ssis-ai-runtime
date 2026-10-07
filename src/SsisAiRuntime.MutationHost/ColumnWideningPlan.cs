using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.SqlServer.Dts.Pipeline.Wrapper;
using Microsoft.SqlServer.Dts.Runtime;
using SsisAiRuntime.FlowRunner;
using SsisAiRuntime.Mutations;
using RuntimeWrapper = Microsoft.SqlServer.Dts.Runtime.Wrapper;

namespace SsisAiRuntime.MutationHost
{
    public sealed class ColumnWidthChange
    {
        internal ColumnWidthChange(string layer, int currentWidth, int proposedWidth)
        { Layer = layer; CurrentWidth = currentWidth; ProposedWidth = proposedWidth; }
        public string Layer { get; }
        public int CurrentWidth { get; }
        public int ProposedWidth { get; }
    }

    public sealed class ColumnWideningPlan
    {
        private ColumnWideningPlan(string sourceHash, string taskId, int sourceId, int outputColumnId,
            int destinationId, int proposedWidth, string beforeSignature, string afterSignature,
            IEnumerable<ColumnWidthChange> changes)
        {
            SourceArtifactHash = sourceHash;
            TaskNativeId = taskId;
            SourceComponentId = sourceId;
            SourceOutputColumnId = outputColumnId;
            DestinationComponentId = destinationId;
            ProposedWidth = proposedWidth;
            BeforeSignature = beforeSignature;
            AfterSignature = afterSignature;
            Changes = new ReadOnlyCollection<ColumnWidthChange>(changes.ToArray());
            Requirements = MutationExecutionPolicies.WidenColumn();
        }

        public string SourceArtifactHash { get; }
        public string TaskNativeId { get; }
        public int SourceComponentId { get; }
        public int SourceOutputColumnId { get; }
        public int DestinationComponentId { get; }
        public int ProposedWidth { get; }
        public IReadOnlyList<ColumnWidthChange> Changes { get; }
        public MutationExecutionRequirements Requirements { get; }
        internal string BeforeSignature { get; }
        internal string AfterSignature { get; }

        internal static ColumnWideningPlan Create(Package package, string sourceHash, string taskId,
            int sourceId, int outputColumnId, int destinationId, int width)
        {
            if (string.IsNullOrWhiteSpace(taskId) || sourceId <= 0 || outputColumnId <= 0 || destinationId <= 0)
            { throw new ArgumentException("Exact native task/component/output-column IDs are required."); }
            if (width < 1 || width > 4000) { throw new ArgumentOutOfRangeException(nameof(width)); }
            var binding = Resolve(package, taskId, sourceId, outputColumnId, destinationId);
            var changes = binding.Widths.Select(entry => new ColumnWidthChange(entry.Key, entry.Value, width)).ToArray();
            if (changes.Any(change => change.CurrentWidth > width) || !changes.Any(change => change.CurrentWidth < width))
            { throw new InvalidOperationException("mutation.width.not_a_widening"); }
            var before = Serialize(package);
            using (var clone = new Package())
            {
                clone.LoadFromXML(before, null);
                var edited = Resolve(clone, taskId, sourceId, outputColumnId, destinationId);
                edited.Apply(width);
                return new ColumnWideningPlan(sourceHash, taskId, sourceId, outputColumnId, destinationId, width,
                    Signature(before), Signature(Serialize(clone)), changes);
            }
        }

        internal void CheckCurrent(Package package, string artifactHash)
        {
            if (!string.Equals(SourceArtifactHash, artifactHash, StringComparison.Ordinal) ||
                Signature(Serialize(package)) != BeforeSignature)
            { throw new InvalidOperationException("mutation.width.plan_changed"); }
            var binding = Resolve(package, TaskNativeId, SourceComponentId, SourceOutputColumnId, DestinationComponentId);
            if (Changes.Any(change => binding.Widths[change.Layer] != change.CurrentWidth))
            { throw new InvalidOperationException("mutation.width.current_width_changed"); }
        }

        internal void Apply(Package package) => Resolve(package, TaskNativeId, SourceComponentId,
            SourceOutputColumnId, DestinationComponentId).Apply(ProposedWidth);

        internal bool Verify(Package package)
        {
            var binding = Resolve(package, TaskNativeId, SourceComponentId, SourceOutputColumnId, DestinationComponentId);
            return binding.Widths.Values.All(width => width == ProposedWidth) && Signature(Serialize(package)) == AfterSignature;
        }

        private static Binding Resolve(Package package, string taskId, int sourceId, int outputColumnId, int destinationId)
        {
            var task = Containers(package).OfType<TaskHost>().Single(item => item.ID == taskId);
            var pipeline = (IDTSPipeline130)task.InnerObject;
            var source = pipeline.ComponentMetaDataCollection.Cast<IDTSComponentMetaData100>().Single(item => item.ID == sourceId);
            var destination = pipeline.ComponentMetaDataCollection.Cast<IDTSComponentMetaData100>().Single(item => item.ID == destinationId);
            var application = new Application();
            if (!IsRegistered(application, source.ComponentClassID, "DTSAdapter.FlatFileSource.") ||
                (!IsRegistered(application, destination.ComponentClassID, "DTSAdapter.FlatFileDestination.") &&
                 !IsRegistered(application, destination.ComponentClassID, "DTSAdapter.OleDbDestination.")))
            { throw new InvalidOperationException("mutation.width.component_unsupported"); }
            var output = source.OutputCollection.Cast<IDTSOutput100>().Single(port => !port.IsErrorOut);
            var paths = pipeline.PathCollection.Cast<IDTSPath100>().Where(path => path.StartPoint.ID == output.ID).ToArray();
            if (paths.Length != 1 || destination.InputCollection.Count != 1 || paths[0].EndPoint.ID != destination.InputCollection[0].ID)
            { throw new InvalidOperationException("mutation.width.unsupported_topology"); }
            var column = output.OutputColumnCollection.Cast<IDTSOutputColumn100>().Single(item => item.ID == outputColumnId);
            var input = destination.InputCollection[0];
            var selected = input.InputColumnCollection.Cast<IDTSInputColumn100>().Single(item => item.LineageID == column.LineageID);
            var external = input.ExternalMetadataColumnCollection.Cast<IDTSExternalMetadataColumn100>().Single(item => item.ID == selected.ExternalMetadataColumnID);
            var sourceExternal = output.ExternalMetadataColumnCollection.Cast<IDTSExternalMetadataColumn100>().Single(item => item.ID == column.ExternalMetadataColumnID);
            var sourceConnection = package.Connections.Cast<ConnectionManager>().Single(item => item.ID == source.RuntimeConnectionCollection[0].ConnectionManagerID);
            var sourceFile = (RuntimeWrapper.IDTSConnectionManagerFlatFile100)sourceConnection.InnerObject;
            var fileColumn = sourceFile.Columns.Cast<RuntimeWrapper.IDTSConnectionManagerFlatFileColumn100>().Single(item =>
                ((RuntimeWrapper.IDTSName100)item).Name == column.Name);
            ConnectionManager destinationConnection = null;
            RuntimeWrapper.IDTSConnectionManagerFlatFileColumn100 destinationFileColumn = null;
            if (IsRegistered(application, destination.ComponentClassID, "DTSAdapter.FlatFileDestination."))
            {
                destinationConnection = package.Connections.Cast<ConnectionManager>().Single(item => item.ID == destination.RuntimeConnectionCollection[0].ConnectionManagerID);
                var file = (RuntimeWrapper.IDTSConnectionManagerFlatFile100)destinationConnection.InnerObject;
                destinationFileColumn = file.Columns.Cast<RuntimeWrapper.IDTSConnectionManagerFlatFileColumn100>().Single(item =>
                    ((RuntimeWrapper.IDTSName100)item).Name == column.Name);
                if (file.Format != "Delimited") { throw new InvalidOperationException("mutation.width.file_format_unsupported"); }
            }
            if (sourceFile.Format != "Delimited" ||
                (column.DataType != RuntimeWrapper.DataType.DT_WSTR && column.DataType != RuntimeWrapper.DataType.DT_STR) ||
                fileColumn.DataType != column.DataType || sourceExternal.DataType != column.DataType ||
                external.DataType != column.DataType || selected.DataType != column.DataType ||
                (destinationFileColumn != null && destinationFileColumn.DataType != column.DataType))
            { throw new InvalidOperationException("mutation.width.type_mismatch"); }
            var widths = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["sourceConnection"] = fileColumn.MaximumWidth,
                ["sourceOutput"] = column.Length,
                ["sourceExternal"] = sourceExternal.Length,
                ["destinationInput"] = selected.Length,
                ["destinationExternal"] = external.Length
            };
            if (destinationFileColumn != null) { widths.Add("destinationConnection", destinationFileColumn.MaximumWidth); }
            return new Binding(sourceConnection, destinationConnection, source, destination, column.Name, widths);
        }

        private static bool IsRegistered(Application application, string classId, string prefix) => application.PipelineComponentInfos
            .Cast<PipelineComponentInfo>().Any(info => info.CreationName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(info.CreationName, classId, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(info.ID, classId, StringComparison.OrdinalIgnoreCase)));

        private static IEnumerable<DtsContainer> Containers(DtsContainer root)
        {
            yield return root;
            if (root is IDTSSequence sequence)
            {
                foreach (Executable executable in sequence.Executables)
                {
                    foreach (var child in Containers((DtsContainer)executable)) { yield return child; }
                }
            }
            if (root is EventsProvider eventsProvider)
            {
                foreach (DtsEventHandler handler in eventsProvider.EventHandlers)
                {
                    foreach (var child in Containers(handler)) { yield return child; }
                }
            }
        }

        private static string Serialize(Package package) { package.SaveToXML(out var xml, null); return xml; }

        private static string Signature(string xml)
        {
            var document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
            document.Root.Attribute(XName.Get("VersionGUID", "www.microsoft.com/SqlServer/Dts"))?.SetValue(string.Empty);
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream, new UTF8Encoding(false), true))
                {
                    foreach (var element in document.Root.DescendantsAndSelf())
                    {
                        writer.Write(element.Name.ToString());
                        writer.Write(element.Ancestors().Count());
                        var attributes = element.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration)
                            .OrderBy(attribute => attribute.Name.ToString(), StringComparer.Ordinal).ToArray();
                        writer.Write(attributes.Length);
                        foreach (var attribute in attributes) { writer.Write(attribute.Name.ToString()); writer.Write(attribute.Value); }
                        writer.Write(string.Concat(element.Nodes().OfType<XText>().Select(node => node.Value)));
                    }
                }
                stream.Position = 0;
                using (var hash = SHA256.Create()) { return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", ""); }
            }
        }

        private sealed class Binding
        {
            private readonly ConnectionManager sourceConnection;
            private readonly ConnectionManager destinationConnection;
            private readonly IDTSComponentMetaData100 source;
            private readonly IDTSComponentMetaData100 destination;
            private readonly string name;
            public Binding(ConnectionManager sourceConnection, ConnectionManager destinationConnection,
                IDTSComponentMetaData100 source, IDTSComponentMetaData100 destination, string name, Dictionary<string, int> widths)
            {
                this.sourceConnection = sourceConnection; this.destinationConnection = destinationConnection;
                this.source = source; this.destination = destination; this.name = name; Widths = widths;
            }
            public Dictionary<string, int> Widths { get; }
            public void Apply(int width) => NativeFlatFileColumnEditor.Widen(sourceConnection, source, destination, name, width, destinationConnection);
        }
    }
}