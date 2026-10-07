using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Microsoft.SqlServer.Dts.Pipeline.Wrapper;
using Microsoft.SqlServer.Dts.Runtime;
using RuntimeWrapper = Microsoft.SqlServer.Dts.Runtime.Wrapper;

namespace SsisAiRuntime.FlowRunner
{
    internal sealed class NativeSyntheticFlowProbe
    {
        public int RowCount { get; private set; }
        public string Stage { get; private set; } = "initialization";
        public JArray Diagnostics { get; } = new JArray();

        public void Run(string directory, FlowProbeRequest request)
        {
            if (!Environment.Is64BitProcess) { throw new InvalidOperationException(); }
            if (request.Recipe == "flat-file-text")
            {
                var text = new NativeFlatFileTextProbe();
                try { text.Run(directory, request); RowCount = text.RowCount; Stage = text.Stage; }
                catch { Stage = text.Stage; throw; }
                finally { foreach (var diagnostic in text.Diagnostics) { Diagnostics.Add(diagnostic.DeepClone()); } }
                return;
            }
            var inputPath = Path.Combine(directory, "input.csv");
            var outputPath = Path.Combine(directory, "output.csv");
            var values = request.Values;
            File.WriteAllLines(inputPath, values.Select(value => value.ToString(CultureInfo.InvariantCulture)));
            var application = new Application();
            using (var package = new Package { Name = "SyntheticDerivedProbe", ProtectionLevel = DTSProtectionLevel.DontSaveSensitive })
            {
                Stage = "connections";
                var inputConnection = FlatFile(package, inputPath, new[] { "Value" });
                var conversion = request.Recipe == "data-conversion";
                var outputType = conversion ? (request.ConversionType == "Int16"
                    ? RuntimeWrapper.DataType.DT_I2 : RuntimeWrapper.DataType.DT_I8) : RuntimeWrapper.DataType.DT_I4;
                var outputConnection = FlatFile(package, outputPath, new[] { "Value", "CalculatedValue" }, outputType);
                var task = (TaskHost)package.Executables.Add("STOCK:PipelineTask");
                task.Name = "SyntheticFlow";
                var pipeline = (IDTSPipeline130)task.InnerObject;

                Stage = "source";
                var source = pipeline.ComponentMetaDataCollection.New();
                source.ComponentClassID = FindComponent(application, "DTSAdapter.FlatFileSource.");
                var sourceDesign = source.Instantiate();
                sourceDesign.ProvideComponentProperties();
                source.Name = "SyntheticSource";
                Connect(source, inputConnection);
                sourceDesign.AcquireConnections(null);
                try { sourceDesign.ReinitializeMetaData(); }
                finally { sourceDesign.ReleaseConnections(); }
                var sourceOutput = source.OutputCollection.Cast<IDTSOutput100>().First(port => !port.IsErrorOut);
                var sourceColumn = sourceOutput.OutputColumnCollection.Cast<IDTSOutputColumn100>().Single();

                Stage = conversion ? "conversion" : "derived";
                var derived = pipeline.ComponentMetaDataCollection.New();
                derived.ComponentClassID = FindComponent(application, conversion ? "DTSTransform.DataConvert." : "DTSTransform.DerivedColumn.");
                var derivedDesign = derived.Instantiate();
                derivedDesign.ProvideComponentProperties();
                derived.Name = conversion ? "ConvertValue" : "CalculateValue";
                var derivedInput = derived.InputCollection[0];
                pipeline.PathCollection.New().AttachPathAndPropagateNotifications(sourceOutput, derivedInput);
                derivedDesign.SetUsageType(derivedInput.ID, derivedInput.GetVirtualInput(),
                    sourceColumn.LineageID, DTSUsageType.UT_READONLY);
                var derivedOutput = derived.OutputCollection.Cast<IDTSOutput100>().First(port => !port.IsErrorOut);
                var calculated = derivedDesign.InsertOutputColumnAt(derivedOutput.ID, 0, "CalculatedValue", "");
                derivedDesign.SetOutputColumnDataTypeProperties(derivedOutput.ID, calculated.ID,
                    outputType, 0, 0, 0, 0);
                if (conversion)
                {
                    derivedDesign.SetOutputColumnProperty(derivedOutput.ID, calculated.ID,
                        "SourceInputColumnLineageID", sourceColumn.LineageID);
                }
                else { derivedDesign.SetOutputColumnProperty(derivedOutput.ID, calculated.ID, "Expression", request.Expression); }

                Stage = "destination";
                var destination = pipeline.ComponentMetaDataCollection.New();
                destination.ComponentClassID = FindComponent(application, "DTSAdapter.FlatFileDestination.");
                var destinationDesign = destination.Instantiate();
                destinationDesign.ProvideComponentProperties();
                destination.Name = "DisposableDestination";
                Connect(destination, outputConnection);
                var destinationInput = destination.InputCollection[0];
                pipeline.PathCollection.New().AttachPathAndPropagateNotifications(derivedOutput, destinationInput);
                destinationDesign.AcquireConnections(null);
                try { destinationDesign.ReinitializeMetaData(); }
                finally { destinationDesign.ReleaseConnections(); }
                foreach (var mapping in new[]
                {
                    new { Name = "Value", Lineage = sourceColumn.LineageID },
                    new { Name = "CalculatedValue", Lineage = calculated.LineageID }
                })
                {
                    var selected = destinationDesign.SetUsageType(destinationInput.ID, destinationInput.GetVirtualInput(),
                        mapping.Lineage, DTSUsageType.UT_READONLY);
                    var external = destinationInput.ExternalMetadataColumnCollection.Cast<IDTSExternalMetadataColumn100>()
                        .Single(column => column.Name == mapping.Name);
                    destinationDesign.MapInputColumn(destinationInput.ID, selected.ID, external.ID);
                }

                Stage = "execution";
                if (package.Execute(null, null, new NativeProbeEvents(Diagnostics), null, null) != DTSExecResult.Success)
                { throw new InvalidOperationException("Native SSIS execution failed; see component diagnostics."); }

                Stage = "assertions";
                var actual = File.ReadAllLines(outputPath).Select(line => line.Split(','))
                    .Select(parts => parts.Select(part => int.Parse(part, CultureInfo.InvariantCulture)).ToArray()).ToArray();
                if (actual.Length != values.Count || actual.Any(row => row.Length != 2))
                { throw new InvalidOperationException("Expected " + values.Count + " two-column rows; received " + actual.Length + " rows with possibly inconsistent columns."); }
                for (var index = 0; index < values.Count; index++)
                {
                    if (actual[index][0] != values[index] || actual[index][1] != request.ExpectedValues[index])
                    {
                        Diagnostics.Add(new JObject
                        {
                            ["code"] = "flow.assertion.mismatch", ["stage"] = "assertions", ["rowIndex"] = index,
                            ["expected"] = new JArray(values[index], request.ExpectedValues[index]),
                            ["actual"] = new JArray(actual[index])
                        });
                        throw new InvalidOperationException("Output mismatch at zero-based row " + index + ".");
                    }
                }
                RowCount = actual.Length;
                Stage = "completed";
            }
        }

        private static ConnectionManager FlatFile(Package package, string path, string[] names,
            RuntimeWrapper.DataType calculatedType = RuntimeWrapper.DataType.DT_I4)
        {
            var connection = package.Connections.Add("FLATFILE");
            connection.Name = "Fixture" + package.Connections.Count;
            connection.ConnectionString = path;
            var flatFile = (RuntimeWrapper.IDTSConnectionManagerFlatFile100)connection.InnerObject;
            flatFile.Format = "Delimited";
            flatFile.Unicode = false;
            flatFile.CodePage = 1252;
            flatFile.ColumnNamesInFirstDataRow = false;
            for (var index = 0; index < names.Length; index++)
            {
                var column = flatFile.Columns.Add();
                column.ColumnType = "Delimited";
                column.ColumnDelimiter = index == names.Length - 1 ? "\r\n" : ",";
                column.DataType = index == 0 ? RuntimeWrapper.DataType.DT_I4 : calculatedType;
                ((RuntimeWrapper.IDTSName100)column).Name = names[index];
            }
            return connection;
        }

        private static void Connect(IDTSComponentMetaData100 component, ConnectionManager connection)
        {
            component.RuntimeConnectionCollection[0].ConnectionManagerID = connection.ID;
            component.RuntimeConnectionCollection[0].ConnectionManager = DtsConvert.GetExtendedInterface(connection);
        }

        private static string FindComponent(Application application, string prefix) =>
            application.PipelineComponentInfos.Cast<PipelineComponentInfo>()
                .First(info => info.CreationName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).CreationName;
    }
}