using System;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using Microsoft.SqlServer.Dts.Pipeline.Wrapper;
using Microsoft.SqlServer.Dts.Runtime;
using RuntimeWrapper = Microsoft.SqlServer.Dts.Runtime.Wrapper;

namespace SsisAiRuntime.FlowRunner
{
    internal sealed class NativeFlatFileTextProbe
    {
        public string Stage { get; private set; } = "text.initialization";
        public int RowCount { get; private set; }
        public JArray Diagnostics { get; } = new JArray();

        public void Run(string directory, FlowProbeRequest request)
        {
            var inputPath = Path.Combine(directory, "input.csv");
            var outputPath = Path.Combine(directory, "output.csv");
            File.WriteAllLines(inputPath, request.TextValues, Encoding.Unicode);
            var application = new Application();
            using (var package = new Package { Name = "SyntheticTextProbe", ProtectionLevel = DTSProtectionLevel.DontSaveSensitive })
            {
                Stage = "text.connections";
                var sourceConnection = Connection(package, inputPath, request.SourceWidth);
                var destinationConnection = Connection(package, outputPath, request.DestinationWidth);
                var host = (TaskHost)package.Executables.Add("STOCK:PipelineTask");
                var pipeline = (IDTSPipeline130)host.InnerObject;
                Stage = "text.source";
                var source = pipeline.ComponentMetaDataCollection.New();
                source.ComponentClassID = Find(application, "DTSAdapter.FlatFileSource.");
                var sourceDesign = source.Instantiate();
                sourceDesign.ProvideComponentProperties();
                Connect(source, sourceConnection);
                sourceDesign.AcquireConnections(null);
                try { sourceDesign.ReinitializeMetaData(); }
                finally { sourceDesign.ReleaseConnections(); }
                var output = source.OutputCollection.Cast<IDTSOutput100>().First(port => !port.IsErrorOut);
                var column = output.OutputColumnCollection.Cast<IDTSOutputColumn100>().Single();
                column.TruncationRowDisposition = DTSRowDisposition.RD_FailComponent;
                column.ErrorRowDisposition = DTSRowDisposition.RD_FailComponent;

                Stage = "text.destination";
                var destination = pipeline.ComponentMetaDataCollection.New();
                destination.ComponentClassID = Find(application, "DTSAdapter.FlatFileDestination.");
                var destinationDesign = destination.Instantiate();
                destinationDesign.ProvideComponentProperties();
                Connect(destination, destinationConnection);
                var input = destination.InputCollection[0];
                pipeline.PathCollection.New().AttachPathAndPropagateNotifications(output, input);
                destinationDesign.AcquireConnections(null);
                try { destinationDesign.ReinitializeMetaData(); }
                finally { destinationDesign.ReleaseConnections(); }
                var selected = destinationDesign.SetUsageType(input.ID, input.GetVirtualInput(), column.LineageID, DTSUsageType.UT_READONLY);
                var external = input.ExternalMetadataColumnCollection.Cast<IDTSExternalMetadataColumn100>().Single();
                destinationDesign.MapInputColumn(input.ID, selected.ID, external.ID);

                if (request.WidenTo.HasValue)
                {
                    Stage = "text.widen";
                    NativeFlatFileColumnEditor.Widen(sourceConnection, source, destination, "Value", request.WidenTo.Value, destinationConnection);
                }
                Stage = "text.width_verification";
                if (column.Length > external.Length)
                { throw new InvalidOperationException("Source output width " + column.Length + " exceeds destination metadata width " + external.Length + ". Use widenTo or increase destinationWidth."); }
                Stage = "text.execution";
                if (package.Execute(null, null, new NativeProbeEvents(Diagnostics), null, null) != DTSExecResult.Success)
                { throw new InvalidOperationException("Native SSIS text execution failed; see component diagnostics."); }
                Stage = "text.assertions";
                var actual = File.ReadAllLines(outputPath, Encoding.Unicode);
                if (actual.Length != request.ExpectedTextValues.Count)
                { throw new InvalidOperationException("Expected " + request.ExpectedTextValues.Count + " rows; received " + actual.Length + "."); }
                for (var index = 0; index < actual.Length; index++)
                {
                    if (!string.Equals(actual[index], request.ExpectedTextValues[index], StringComparison.Ordinal))
                    {
                        Diagnostics.Add(new JObject
                        {
                            ["code"] = "flow.assertion.mismatch", ["stage"] = "assertions", ["rowIndex"] = index,
                            ["expected"] = request.ExpectedTextValues[index], ["actual"] = actual[index]
                        });
                        throw new InvalidOperationException("Text output mismatch at zero-based row " + index + ".");
                    }
                }
                RowCount = actual.Length;
                Stage = "completed";
            }
        }

        private static ConnectionManager Connection(Package package, string path, int width)
        {
            var connection = package.Connections.Add("FLATFILE");
            connection.ConnectionString = path;
            var file = (RuntimeWrapper.IDTSConnectionManagerFlatFile100)connection.InnerObject;
            file.Format = "Delimited";
            file.Unicode = true;
            file.ColumnNamesInFirstDataRow = false;
            var column = file.Columns.Add();
            column.ColumnType = "Delimited";
            column.ColumnDelimiter = "\r\n";
            column.DataType = RuntimeWrapper.DataType.DT_WSTR;
            column.MaximumWidth = width;
            ((RuntimeWrapper.IDTSName100)column).Name = "Value";
            return connection;
        }

        private static string Find(Application application, string prefix) => application.PipelineComponentInfos
            .Cast<PipelineComponentInfo>().First(info => info.CreationName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).CreationName;

        private static void Connect(IDTSComponentMetaData100 component, ConnectionManager connection)
        {
            component.RuntimeConnectionCollection[0].ConnectionManagerID = connection.ID;
            component.RuntimeConnectionCollection[0].ConnectionManager = DtsConvert.GetExtendedInterface(connection);
        }
    }
}