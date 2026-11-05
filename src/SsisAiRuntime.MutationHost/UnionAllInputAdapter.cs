using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.SqlServer.Dts.Pipeline.Wrapper;
using Microsoft.SqlServer.Dts.Runtime;
using RuntimeWrapper = Microsoft.SqlServer.Dts.Runtime.Wrapper;

namespace SsisAiRuntime.MutationHost
{
    public sealed class UnionAllInputAdapter
    {
        private sealed class ColumnSnapshot
        {
            public ColumnSnapshot(string name, int lineageId, RuntimeWrapper.DataType dataType, int length,
                int precision, int scale, int codePage)
            {
                Name = name;
                LineageId = lineageId;
                DataType = dataType;
                Length = length;
                Precision = precision;
                Scale = scale;
                CodePage = codePage;
            }

            public string Name { get; }
            public int LineageId { get; }
            public RuntimeWrapper.DataType DataType { get; }
            public int Length { get; }
            public int Precision { get; }
            public int Scale { get; }
            public int CodePage { get; }

            public string Signature => string.Join("|", Name, LineageId.ToString(CultureInfo.InvariantCulture),
                DataType.ToString(), Length.ToString(CultureInfo.InvariantCulture),
                Precision.ToString(CultureInfo.InvariantCulture), Scale.ToString(CultureInfo.InvariantCulture),
                CodePage.ToString(CultureInfo.InvariantCulture));
        }

        private sealed class MappingSnapshot
        {
            public MappingSnapshot(string column, int outputLineageId)
            { Column = column; OutputLineageId = outputLineageId; }
            public string Column { get; }
            public int OutputLineageId { get; }
        }

        private sealed class InputSnapshot
        {
            public InputSnapshot(IDTSInput100 input)
            {
                Name = input.Name;
                HasSideEffects = input.HasSideEffects;
                ErrorRowDisposition = input.ErrorRowDisposition;
                TruncationRowDisposition = input.TruncationRowDisposition;
                Columns = input.InputColumnCollection.Cast<IDTSInputColumn100>()
                    .Select(column => new ColumnSnapshot(column.Name, column.LineageID, column.DataType,
                        column.Length, column.Precision, column.Scale, column.CodePage))
                    .ToArray();
                Mappings = input.InputColumnCollection.Cast<IDTSInputColumn100>()
                    .Select(column => new MappingSnapshot(column.Name, OutputLineageId(column)))
                    .ToArray();
            }

            public string Name { get; }
            public bool HasSideEffects { get; }
            public DTSRowDisposition ErrorRowDisposition { get; }
            public DTSRowDisposition TruncationRowDisposition { get; }
            public ColumnSnapshot[] Columns { get; }
            public MappingSnapshot[] Mappings { get; }

            public string Signature => string.Join(";", Name, HasSideEffects.ToString(),
                ErrorRowDisposition.ToString(), TruncationRowDisposition.ToString(),
                string.Join(",", Columns.Select(column => column.Signature)),
                string.Join(",", Mappings.Select(mapping => mapping.Column + "=" +
                    ((int)mapping.OutputLineageId).ToString(CultureInfo.InvariantCulture))));
        }

        private readonly IDTSComponentMetaData100 union;
        private readonly IDTSDesigntimeComponent100 design;
        private readonly IDTSInput100 originalInput;
        private readonly InputSnapshot selectedBefore;
        private readonly InputSnapshot[] otherInputsBefore;
        private readonly ColumnSnapshot[] outputColumnsBefore;
        private IDTSInput100 replacementInput;
        private bool prepared;
        private bool completed;

        private UnionAllInputAdapter(IDTSPipeline130 pipeline, IDTSPath100 path)
        {
            union = path.EndPoint.Component;
            originalInput = (IDTSInput100)path.EndPoint;
            selectedBefore = new InputSnapshot(originalInput);
            otherInputsBefore = union.InputCollection.Cast<IDTSInput100>()
                .Where(input => input.ID != originalInput.ID).Select(input => new InputSnapshot(input)).ToArray();
            outputColumnsBefore = union.OutputCollection.Cast<IDTSOutput100>().Where(output => !output.IsErrorOut)
                .SelectMany(output => output.OutputColumnCollection.Cast<IDTSOutputColumn100>())
                .Select(column => new ColumnSnapshot(column.Name, column.LineageID, column.DataType,
                    column.Length, column.Precision, column.Scale, column.CodePage)).ToArray();
            design = union.Instantiate();
        }

        public static UnionAllInputAdapter CreateIfRequired(IDTSPipeline130 pipeline, IDTSPath100 path)
        {
            if (pipeline == null) { throw new ArgumentNullException(nameof(pipeline)); }
            if (path == null) { throw new ArgumentNullException(nameof(path)); }
            var destination = path.EndPoint.Component;
            return IsUnionAll(destination) ? new UnionAllInputAdapter(pipeline, path) : null;
        }

        public IDTSInput100 Prepare()
        {
            if (prepared) { return replacementInput; }
            if (selectedBefore.Columns.Length == 0 || selectedBefore.Mappings.Any(mapping =>
                mapping.OutputLineageId <= 0 || !outputColumnsBefore.Any(column => column.LineageId == mapping.OutputLineageId)))
            { throw new InvalidOperationException("mutation.insertion.union_all.mapping_snapshot_invalid"); }

            foreach (var column in originalInput.InputColumnCollection.Cast<IDTSInputColumn100>().ToArray())
            { design.SetUsageType(originalInput.ID, originalInput.GetVirtualInput(), column.LineageID, DTSUsageType.UT_IGNORED); }

            replacementInput = union.InputCollection.New();
            replacementInput.Name = selectedBefore.Name;
            prepared = true;
            return replacementInput;
        }

        public IDTSInput100 ResolveReplacementInput()
        {
            var matches = union.InputCollection.Cast<IDTSInput100>()
                .Where(input => input.ID == replacementInput.ID).ToArray();
            if (matches.Length != 1)
            { throw new InvalidOperationException("mutation.insertion.union_all.selected_input_identity_changed"); }
            replacementInput = matches[0];
            return replacementInput;
        }

        public void Complete(string outputName)
        {
            if (!prepared || completed || replacementInput == null)
            { throw new InvalidOperationException("mutation.insertion.union_all.adapter_state_invalid"); }
            if (string.IsNullOrWhiteSpace(outputName) || outputColumnsBefore.Any(column =>
                string.Equals(column.Name, outputName, StringComparison.OrdinalIgnoreCase)))
            { throw new InvalidOperationException("mutation.insertion.union_all.output_name_conflict"); }

            var virtualInput = replacementInput.GetVirtualInput();
            foreach (var mapping in selectedBefore.Mappings)
            {
                var virtualColumn = FindVirtualColumn(virtualInput, mapping.Column);
                design.SetUsageType(replacementInput.ID, virtualInput, virtualColumn.LineageID, DTSUsageType.UT_READONLY);
            }
            var insertedVirtualColumn = FindVirtualColumn(virtualInput, outputName);
            design.SetUsageType(replacementInput.ID, virtualInput, insertedVirtualColumn.LineageID, DTSUsageType.UT_READONLY);

            var selectedColumns = replacementInput.InputColumnCollection.Cast<IDTSInputColumn100>().ToArray();
            foreach (var mapping in selectedBefore.Mappings)
            {
                var inputColumn = selectedColumns.Single(column => column.Name == mapping.Column);
                design.SetInputColumnProperty(replacementInput.ID, inputColumn.ID, "OutputColumnLineageID",
                    mapping.OutputLineageId);
            }

            var output = union.OutputCollection.Cast<IDTSOutput100>().Single(port => !port.IsErrorOut);
            var unionOutputColumn = design.InsertOutputColumnAt(output.ID, output.OutputColumnCollection.Count,
                outputName, string.Empty);
            design.SetOutputColumnDataTypeProperties(output.ID, unionOutputColumn.ID, insertedVirtualColumn.DataType,
                insertedVirtualColumn.Length, insertedVirtualColumn.Precision, insertedVirtualColumn.Scale,
                insertedVirtualColumn.CodePage);
            var insertedInputColumn = replacementInput.InputColumnCollection.Cast<IDTSInputColumn100>()
                .Single(column => column.Name == outputName);
            design.SetInputColumnProperty(replacementInput.ID, insertedInputColumn.ID, "OutputColumnLineageID",
                unionOutputColumn.LineageID);
            design.ReinitializeMetaData();
            completed = true;
        }

        public string Verify(string outputName)
        {
            try
            {
                var inputs = union.InputCollection.Cast<IDTSInput100>().ToArray();
                foreach (var expected in otherInputsBefore)
                {
                    var actual = inputs.SingleOrDefault(input => input.Name == expected.Name);
                    if (actual == null || new InputSnapshot(actual).Signature != expected.Signature)
                    { return "mutation.insertion.union_all.other_input_changed"; }
                }

                var selected = inputs.SingleOrDefault(input => input.Name == selectedBefore.Name);
                if (selected == null) { return "mutation.insertion.union_all.selected_input_missing"; }
                var selectedSnapshot = new InputSnapshot(selected);
                foreach (var mapping in selectedBefore.Mappings)
                {
                    var actualMapping = selectedSnapshot.Mappings.SingleOrDefault(item => item.Column == mapping.Column);
                    if (actualMapping == null || actualMapping.OutputLineageId != mapping.OutputLineageId)
                    { return "mutation.insertion.union_all.selected_mapping_changed"; }
                }

                var outputs = union.OutputCollection.Cast<IDTSOutput100>().Where(port => !port.IsErrorOut)
                    .SelectMany(port => port.OutputColumnCollection.Cast<IDTSOutputColumn100>()).ToArray();
                foreach (var expected in outputColumnsBefore)
                {
                    var actual = outputs.SingleOrDefault(column => column.Name == expected.Name);
                    if (actual == null || new ColumnSnapshot(actual.Name, actual.LineageID, actual.DataType,
                        actual.Length, actual.Precision, actual.Scale, actual.CodePage).Signature != expected.Signature)
                    { return "mutation.insertion.union_all.output_metadata_changed"; }
                }
                var insertedOutput = outputs.SingleOrDefault(column => column.Name == outputName);
                if (insertedOutput == null || selectedSnapshot.Mappings.SingleOrDefault(mapping =>
                    mapping.Column == outputName)?.OutputLineageId != insertedOutput.LineageID)
                { return "mutation.insertion.union_all.inserted_output_unmapped"; }
                if (outputs.Length != outputColumnsBefore.Length + 1)
                { return "mutation.insertion.union_all.output_inventory_changed"; }
                return string.Empty;
            }
            catch (Exception)
            { return "mutation.insertion.union_all.verification_failed"; }
        }

        public void Rollback()
        {
            if (!prepared || replacementInput == null) { return; }
            foreach (var column in originalInput.InputColumnCollection.Cast<IDTSInputColumn100>().ToArray())
            { design.SetUsageType(originalInput.ID, originalInput.GetVirtualInput(), column.LineageID, DTSUsageType.UT_IGNORED); }
            if (union.InputCollection.Cast<IDTSInput100>().Any(input => input.ID == replacementInput.ID))
            { union.InputCollection.RemoveObjectByID(replacementInput.ID); }
            foreach (var mapping in selectedBefore.Mappings)
            {
                var virtualInput = originalInput.GetVirtualInput();
                var virtualColumn = FindVirtualColumn(virtualInput, mapping.Column);
                design.SetUsageType(originalInput.ID, virtualInput, virtualColumn.LineageID, DTSUsageType.UT_READONLY);
                var inputColumn = originalInput.InputColumnCollection.Cast<IDTSInputColumn100>()
                    .Single(column => column.Name == mapping.Column);
                design.SetInputColumnProperty(originalInput.ID, inputColumn.ID, "OutputColumnLineageID",
                    mapping.OutputLineageId);
            }
            prepared = false;
            completed = false;
        }

        private static bool IsUnionAll(IDTSComponentMetaData100 component)
        {
            if (string.Equals(component.ComponentClassID, "Microsoft.UnionAll", StringComparison.OrdinalIgnoreCase) ||
                component.ComponentClassID.StartsWith("DTSTransform.UnionAll.", StringComparison.OrdinalIgnoreCase))
            { return true; }
            return new Application().PipelineComponentInfos.Cast<PipelineComponentInfo>().Any(info =>
                string.Equals(info.ID, component.ComponentClassID, StringComparison.OrdinalIgnoreCase) &&
                info.CreationName.StartsWith("DTSTransform.UnionAll.", StringComparison.OrdinalIgnoreCase));
        }

        private static int OutputLineageId(IDTSInputColumn100 column)
        {
            var property = column.CustomPropertyCollection.Cast<IDTSCustomProperty100>()
                .SingleOrDefault(item => item.Name == "OutputColumnLineageID");
            return property == null ? 0 : Convert.ToInt32(property.Value, CultureInfo.InvariantCulture);
        }

        private static IDTSVirtualInputColumn100 FindVirtualColumn(IDTSVirtualInput100 virtualInput, string name)
        {
            var matches = virtualInput.VirtualInputColumnCollection.Cast<IDTSVirtualInputColumn100>()
                .Where(column => column.Name == name).ToArray();
            if (matches.Length != 1) { throw new InvalidOperationException("mutation.insertion.union_all.virtual_column_ambiguous"); }
            return matches[0];
        }
    }
}