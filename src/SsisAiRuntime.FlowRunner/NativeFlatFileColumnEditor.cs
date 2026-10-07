using System;
using System.Linq;
using Microsoft.SqlServer.Dts.Pipeline.Wrapper;
using Microsoft.SqlServer.Dts.Runtime;
using RuntimeWrapper = Microsoft.SqlServer.Dts.Runtime.Wrapper;

namespace SsisAiRuntime.FlowRunner
{
    public static class NativeFlatFileColumnEditor
    {
        public static void Widen(ConnectionManager sourceConnection, IDTSComponentMetaData100 source,
            IDTSComponentMetaData100 destination, string columnName, int width,
            ConnectionManager destinationConnection = null)
        {
            Resize(sourceConnection, source, destination, columnName, width, false, destinationConnection);
        }

        public static void Shrink(ConnectionManager sourceConnection, IDTSComponentMetaData100 source,
            IDTSComponentMetaData100 destination, string columnName, int width,
            bool acknowledgeDataLoss, ConnectionManager destinationConnection = null)
        {
            if (!acknowledgeDataLoss) { throw new InvalidOperationException("flow.column.shrink_acknowledgement_required"); }
            Resize(sourceConnection, source, destination, columnName, width, true, destinationConnection);
        }

        private static void Resize(ConnectionManager sourceConnection, IDTSComponentMetaData100 source,
            IDTSComponentMetaData100 destination, string columnName, int width, bool shrink,
            ConnectionManager destinationConnection)
        {
            if (sourceConnection == null) { throw new ArgumentNullException(nameof(sourceConnection)); }
            if (source == null) { throw new ArgumentNullException(nameof(source)); }
            if (destination == null) { throw new ArgumentNullException(nameof(destination)); }
            if (string.IsNullOrWhiteSpace(columnName)) { throw new ArgumentException("A column name is required.", nameof(columnName)); }
            if (width < 1 || width > 4000) { throw new ArgumentOutOfRangeException(nameof(width)); }
            var fileColumn = FindFileColumn(sourceConnection, columnName);
            var output = source.OutputCollection.Cast<IDTSOutput100>().Where(port => !port.IsErrorOut)
                .SelectMany(port => port.OutputColumnCollection.Cast<IDTSOutputColumn100>())
                .Single(column => column.Name == columnName);
            var input = destination.InputCollection.Cast<IDTSInput100>().Single(port =>
                port.InputColumnCollection.Cast<IDTSInputColumn100>().Any(column => column.LineageID == output.LineageID));
            var selected = input.InputColumnCollection.Cast<IDTSInputColumn100>().Single(column => column.LineageID == output.LineageID);
            var external = input.ExternalMetadataColumnCollection.Cast<IDTSExternalMetadataColumn100>()
                .Single(column => column.ID == selected.ExternalMetadataColumnID);
            var destinationFileColumn = destinationConnection == null ? null : FindFileColumn(destinationConnection, columnName);
            var sourceExternal = source.OutputCollection.Cast<IDTSOutput100>()
                .SelectMany(port => port.ExternalMetadataColumnCollection.Cast<IDTSExternalMetadataColumn100>())
                .SingleOrDefault(column => column.ID == output.ExternalMetadataColumnID);
            if ((output.DataType != RuntimeWrapper.DataType.DT_STR && output.DataType != RuntimeWrapper.DataType.DT_WSTR) ||
                fileColumn.DataType != output.DataType || external.DataType != output.DataType ||
                (sourceExternal != null && sourceExternal.DataType != output.DataType) ||
                (destinationFileColumn != null && destinationFileColumn.DataType != output.DataType) ||
                (shrink && width > output.Length) ||
                (!shrink && (width < output.Length || width < fileColumn.MaximumWidth || width < external.Length ||
                    (sourceExternal != null && width < sourceExternal.Length) ||
                    (destinationFileColumn != null && width < destinationFileColumn.MaximumWidth))))
            { throw new InvalidOperationException("flow.width.incompatible_metadata"); }

            fileColumn.MaximumWidth = width;
            if (destinationFileColumn != null) { destinationFileColumn.MaximumWidth = width; }
            if (sourceExternal != null) { sourceExternal.Length = width; }
            external.Length = width;
            output.SetDataTypeProperties(output.DataType, width, output.Precision, output.Scale, output.CodePage);
            var design = destination.Instantiate();
            design.SetUsageType(input.ID, input.GetVirtualInput(), output.LineageID, DTSUsageType.UT_IGNORED);
            var refreshed = design.SetUsageType(input.ID, input.GetVirtualInput(), output.LineageID, DTSUsageType.UT_READONLY);
            design.MapInputColumn(input.ID, refreshed.ID, external.ID);
        }

        public static void Add(IDTSPipeline130 pipeline, ConnectionManager sourceConnection,
            IDTSComponentMetaData100 source, IDTSComponentMetaData100 destination,
            string columnName, int width, ConnectionManager destinationConnection = null)
        {
            if (string.IsNullOrWhiteSpace(columnName)) { throw new ArgumentException("A column name is required.", nameof(columnName)); }
            if (width < 1 || width > 4000) { throw new ArgumentOutOfRangeException(nameof(width)); }
            var output = DirectOutput(pipeline, source, destination);
            var sourceFile = UnicodeDelimitedFile(sourceConnection);
            var destinationFile = destinationConnection == null ? null : UnicodeDelimitedFile(destinationConnection);
            var input = destination.InputCollection[0];
            if (sourceFile.Columns.Count == 0 || (destinationFile != null && destinationFile.Columns.Count == 0) ||
                output.OutputColumnCollection.Cast<IDTSOutputColumn100>().Any(column => column.Name == columnName) ||
                input.ExternalMetadataColumnCollection.Cast<IDTSExternalMetadataColumn100>().Any(column => column.Name == columnName) ||
                FileColumns(sourceFile).Any(column => ((RuntimeWrapper.IDTSName100)column).Name == columnName) ||
                (destinationFile != null && FileColumns(destinationFile).Any(column => ((RuntimeWrapper.IDTSName100)column).Name == columnName)))
            { throw new InvalidOperationException("flow.column.add_duplicate_or_invalid_schema"); }

            AppendFileColumn(sourceFile, columnName, width);
            if (destinationFile != null) { AppendFileColumn(destinationFile, columnName, width); }
            var sourceExternal = output.ExternalMetadataColumnCollection.New();
            sourceExternal.Name = columnName;
            sourceExternal.DataType = RuntimeWrapper.DataType.DT_WSTR;
            sourceExternal.Length = width;
            var added = output.OutputColumnCollection.New();
            added.Name = columnName;
            added.SetDataTypeProperties(RuntimeWrapper.DataType.DT_WSTR, width, 0, 0, 0);
            added.ExternalMetadataColumnID = sourceExternal.ID;
            var destinationExternal = input.ExternalMetadataColumnCollection.New();
            destinationExternal.Name = columnName;
            destinationExternal.DataType = RuntimeWrapper.DataType.DT_WSTR;
            destinationExternal.Length = width;
            var design = destination.Instantiate();
            var selected = design.SetUsageType(input.ID, input.GetVirtualInput(), added.LineageID, DTSUsageType.UT_READONLY);
            design.MapInputColumn(input.ID, selected.ID, destinationExternal.ID);
        }

        public static void Remove(IDTSPipeline130 pipeline, ConnectionManager sourceConnection,
            IDTSComponentMetaData100 source, IDTSComponentMetaData100 destination, string columnName,
            bool acknowledgeDataLoss, ConnectionManager destinationConnection = null)
        {
            if (!acknowledgeDataLoss) { throw new InvalidOperationException("flow.column.remove_acknowledgement_required"); }
            var output = DirectOutput(pipeline, source, destination);
            var sourceFile = UnicodeDelimitedFile(sourceConnection);
            var sourceFileColumn = FindFileColumn(sourceConnection, columnName);
            var destinationFile = destinationConnection == null ? null : UnicodeDelimitedFile(destinationConnection);
            var destinationFileColumn = destinationConnection == null ? null : FindFileColumn(destinationConnection, columnName);
            var removed = output.OutputColumnCollection.Cast<IDTSOutputColumn100>().Single(column => column.Name == columnName);
            var input = destination.InputCollection[0];
            var selected = input.InputColumnCollection.Cast<IDTSInputColumn100>().Single(column => column.LineageID == removed.LineageID);
            var external = input.ExternalMetadataColumnCollection.Cast<IDTSExternalMetadataColumn100>()
                .Single(column => column.ID == selected.ExternalMetadataColumnID);
            var sourceExternal = output.ExternalMetadataColumnCollection.Cast<IDTSExternalMetadataColumn100>()
                .SingleOrDefault(column => column.ID == removed.ExternalMetadataColumnID);
            if (sourceFile.Columns.Count < 2 || output.OutputColumnCollection.Count < 2 ||
                (destinationFile != null && destinationFile.Columns.Count < 2) ||
                input.InputColumnCollection.Cast<IDTSInputColumn100>().Any(column =>
                    column.ID != selected.ID && column.ExternalMetadataColumnID == external.ID))
            { throw new InvalidOperationException("flow.column.remove_invalid_schema"); }

            var design = destination.Instantiate();
            design.SetUsageType(input.ID, input.GetVirtualInput(), removed.LineageID, DTSUsageType.UT_IGNORED);
            input.ExternalMetadataColumnCollection.RemoveObjectByID(external.ID);
            if (sourceExternal != null) { output.ExternalMetadataColumnCollection.RemoveObjectByID(sourceExternal.ID); }
            output.OutputColumnCollection.RemoveObjectByID(removed.ID);
            RemoveFileColumn(sourceFile, sourceFileColumn);
            if (destinationFile != null) { RemoveFileColumn(destinationFile, destinationFileColumn); }
        }

        private static IDTSOutput100 DirectOutput(IDTSPipeline130 pipeline, IDTSComponentMetaData100 source,
            IDTSComponentMetaData100 destination)
        {
            if (pipeline == null) { throw new ArgumentNullException(nameof(pipeline)); }
            if (source == null) { throw new ArgumentNullException(nameof(source)); }
            if (destination == null) { throw new ArgumentNullException(nameof(destination)); }
            if (!pipeline.ComponentMetaDataCollection.Cast<IDTSComponentMetaData100>().Any(component => component.ID == source.ID) ||
                !pipeline.ComponentMetaDataCollection.Cast<IDTSComponentMetaData100>().Any(component => component.ID == destination.ID) ||
                destination.InputCollection.Count != 1)
            { throw new InvalidOperationException("flow.column.unsupported_topology"); }
            var output = source.OutputCollection.Cast<IDTSOutput100>().Single(port => !port.IsErrorOut);
            var paths = pipeline.PathCollection.Cast<IDTSPath100>().Where(path => path.StartPoint.ID == output.ID).ToArray();
            if (paths.Length != 1 || paths[0].EndPoint.ID != destination.InputCollection[0].ID)
            { throw new InvalidOperationException("flow.column.unsupported_topology"); }
            return output;
        }

        private static RuntimeWrapper.IDTSConnectionManagerFlatFile100 UnicodeDelimitedFile(ConnectionManager connection)
        {
            if (connection == null) { throw new ArgumentNullException(nameof(connection)); }
            var file = (RuntimeWrapper.IDTSConnectionManagerFlatFile100)connection.InnerObject;
            if (!file.Unicode || file.Format != "Delimited")
            { throw new InvalidOperationException("flow.column.unsupported_file_format"); }
            return file;
        }

        private static RuntimeWrapper.IDTSConnectionManagerFlatFileColumn100[] FileColumns(RuntimeWrapper.IDTSConnectionManagerFlatFile100 file) =>
            file.Columns.Cast<RuntimeWrapper.IDTSConnectionManagerFlatFileColumn100>().ToArray();

        private static void AppendFileColumn(RuntimeWrapper.IDTSConnectionManagerFlatFile100 file, string name, int width)
        {
            var previous = FileColumns(file).Last();
            var rowDelimiter = previous.ColumnDelimiter;
            var fieldDelimiter = FileColumns(file).Length > 1 ? FileColumns(file)[0].ColumnDelimiter : ",";
            previous.ColumnDelimiter = fieldDelimiter;
            var added = file.Columns.Add();
            added.ColumnType = "Delimited";
            added.ColumnDelimiter = rowDelimiter;
            added.DataType = RuntimeWrapper.DataType.DT_WSTR;
            added.MaximumWidth = width;
            ((RuntimeWrapper.IDTSName100)added).Name = name;
        }

        private static void RemoveFileColumn(RuntimeWrapper.IDTSConnectionManagerFlatFile100 file,
            RuntimeWrapper.IDTSConnectionManagerFlatFileColumn100 removed)
        {
            var columns = FileColumns(file);
            var index = Array.FindIndex(columns, column => ((RuntimeWrapper.IDTSName100)column).Name == ((RuntimeWrapper.IDTSName100)removed).Name);
            if (index == columns.Length - 1) { columns[index - 1].ColumnDelimiter = removed.ColumnDelimiter; }
            file.Columns.Remove(index);
        }

        private static RuntimeWrapper.IDTSConnectionManagerFlatFileColumn100 FindFileColumn(ConnectionManager connection, string name)
        {
            var flatFile = (RuntimeWrapper.IDTSConnectionManagerFlatFile100)connection.InnerObject;
            return flatFile.Columns.Cast<RuntimeWrapper.IDTSConnectionManagerFlatFileColumn100>()
                .Single(column => ((RuntimeWrapper.IDTSName100)column).Name == name);
        }
    }
}