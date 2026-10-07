using System;
using System.Linq;
using System.Xml.Linq;
using Microsoft.SqlServer.Dts.Pipeline.Wrapper;
using Microsoft.SqlServer.Dts.Runtime;
using SsisAiRuntime.FlowRunner;
using RuntimeWrapper = Microsoft.SqlServer.Dts.Runtime.Wrapper;

namespace SsisAiRuntime.Ssis16IntegrationTests
{
    internal static class InMemorySchemaEditTests
    {
        public static void Run(Func<string, string> findComponent)
        {
            using (var initial = new Package { Name = "SchemaEditFixture", ProtectionLevel = DTSProtectionLevel.DontSaveSensitive })
            {
                var connection = initial.Connections.Add("FLATFILE");
                connection.Name = "ExistingFlatFile";
                var file = (RuntimeWrapper.IDTSConnectionManagerFlatFile100)connection.InnerObject;
                file.Format = "Delimited";
                file.Unicode = true;
                AddFileColumn(file, "Keep", ",");
                AddFileColumn(file, "Remove", "\r\n");
                var host = (TaskHost)initial.Executables.Add("STOCK:PipelineTask");
                var pipeline = (IDTSPipeline130)host.InnerObject;
                var source = pipeline.ComponentMetaDataCollection.New();
                source.ComponentClassID = findComponent("DTSAdapter.FlatFileSource.");
                source.Instantiate().ProvideComponentProperties();
                source.Name = "Source";
                source.RuntimeConnectionCollection[0].ConnectionManagerID = connection.ID;
                source.RuntimeConnectionCollection[0].ConnectionManager = DtsConvert.GetExtendedInterface(connection);
                var output = source.OutputCollection.Cast<IDTSOutput100>().First(port => !port.IsErrorOut);
                var destination = pipeline.ComponentMetaDataCollection.New();
                destination.ComponentClassID = findComponent("DTSAdapter.OleDbDestination.");
                var design = destination.Instantiate();
                design.ProvideComponentProperties();
                destination.Name = "Destination";
                design.SetComponentProperty("OpenRowset", "[dbo].[SyntheticDestination]");
                var input = destination.InputCollection[0];
                foreach (var name in new[] { "Keep", "Remove" }) { AddOutput(output, name); }
                pipeline.PathCollection.New().AttachPathAndPropagateNotifications(output, input);
                foreach (IDTSOutputColumn100 column in output.OutputColumnCollection) { Map(design, input, column); }

                using (var existing = new Package())
                {
                    existing.LoadFromXML(Serialize(initial).ToString(), null);
                    var existingConnection = existing.Connections.Cast<ConnectionManager>().Single();
                    var existingFile = (RuntimeWrapper.IDTSConnectionManagerFlatFile100)existingConnection.InnerObject;
                    var existingPipeline = (IDTSPipeline130)((TaskHost)existing.Executables[0]).InnerObject;
                    var existingSource = existingPipeline.ComponentMetaDataCollection.Cast<IDTSComponentMetaData100>().Single(component => component.Name == "Source");
                    var existingOutput = existingSource.OutputCollection.Cast<IDTSOutput100>().First(port => !port.IsErrorOut);
                    var existingDestination = existingPipeline.ComponentMetaDataCollection.Cast<IDTSComponentMetaData100>().Single(component => component.Name == "Destination");
                    NativeFlatFileColumnEditor.Remove(existingPipeline, existingConnection, existingSource, existingDestination, "Remove", true);
                    NativeFlatFileColumnEditor.Add(existingPipeline, existingConnection, existingSource, existingDestination, "Added", 4);
                    NativeFlatFileColumnEditor.Widen(existingConnection, existingSource, existingDestination, "Keep", 64);
                    NativeFlatFileColumnEditor.Shrink(existingConnection, existingSource, existingDestination, "Added", 2, true);

                    using (var reloaded = new Package())
                    {
                        reloaded.LoadFromXML(Serialize(existing).ToString(), null);
                        var reloadedConnection = reloaded.Connections.Cast<ConnectionManager>().Single();
                        var columns = ((RuntimeWrapper.IDTSConnectionManagerFlatFile100)reloadedConnection.InnerObject).Columns
                            .Cast<RuntimeWrapper.IDTSConnectionManagerFlatFileColumn100>().ToArray();
                        Require(columns.Select(column => ((RuntimeWrapper.IDTSName100)column).Name).SequenceEqual(new[] { "Keep", "Added" }), "flatfile.columns");
                        Require(columns[0].MaximumWidth == 64 && columns[1].MaximumWidth == 2, "flatfile.widths");
                        Require(reloaded.ID == initial.ID && reloadedConnection.ID == connection.ID, "identity.preserved");
                        var xml = Serialize(reloaded);
                        var sourceXml = Component(xml, "Source");
                        var destinationXml = Component(xml, "Destination");
                        foreach (var name in new[] { "Keep", "Added" })
                        {
                            var outputXml = sourceXml.Descendants().Single(element => element.Name.LocalName == "outputColumn" && (string)element.Attribute("name") == name);
                            var externalXml = destinationXml.Descendants().Single(element => element.Name.LocalName == "externalMetadataColumn" && (string)element.Attribute("name") == name);
                            var inputXml = destinationXml.Descendants().Single(element => element.Name.LocalName == "inputColumn" && (string)element.Attribute("cachedName") == name);
                            Require((string)inputXml.Attribute("externalMetadataColumnId") == (string)externalXml.Attribute("refId") &&
                                (string)inputXml.Attribute("lineageId") == (string)outputXml.Attribute("lineageId"), "mapping.preserved");
                            var expectedWidth = name == "Keep" ? 64 : 2;
                            Require((int)outputXml.Attribute("length") == expectedWidth && (int)externalXml.Attribute("length") == expectedWidth,
                                "source.destination.widths");
                        }
                        Require(destinationXml.Descendants().Count(element => element.Name.LocalName == "externalMetadataColumn") == 2, "removed.destination.column");
                        Require(destinationXml.Descendants().Any(element => element.Name.LocalName == "property" &&
                            (string)element.Attribute("name") == "OpenRowset" && element.Value == "[dbo].[SyntheticDestination]"), "table.identity");
                    }
                }
            }
            Console.WriteLine("In-memory schema edits: PASS; existing Flat File/source/destination add/remove mappings and string widths, XML reload; no database table DDL or connections.");
        }

        private static void AddFileColumn(RuntimeWrapper.IDTSConnectionManagerFlatFile100 file, string name, string delimiter)
        {
            var column = file.Columns.Add();
            column.ColumnType = "Delimited";
            column.ColumnDelimiter = delimiter;
            column.DataType = RuntimeWrapper.DataType.DT_WSTR;
            column.MaximumWidth = 4;
            ((RuntimeWrapper.IDTSName100)column).Name = name;
        }

        private static IDTSOutputColumn100 AddOutput(IDTSOutput100 output, string name)
        {
            var external = output.ExternalMetadataColumnCollection.New();
            external.Name = name;
            external.DataType = RuntimeWrapper.DataType.DT_WSTR;
            external.Length = 4;
            var column = output.OutputColumnCollection.New();
            column.Name = name;
            column.SetDataTypeProperties(RuntimeWrapper.DataType.DT_WSTR, 4, 0, 0, 0);
            column.ExternalMetadataColumnID = external.ID;
            return column;
        }

        private static void Map(CManagedComponentWrapper design, IDTSInput100 input, IDTSOutputColumn100 output)
        {
            var external = input.ExternalMetadataColumnCollection.New();
            external.Name = output.Name;
            external.DataType = output.DataType;
            external.Length = output.Length;
            var selected = design.SetUsageType(input.ID, input.GetVirtualInput(), output.LineageID, DTSUsageType.UT_READONLY);
            design.MapInputColumn(input.ID, selected.ID, external.ID);
        }

        private static XDocument Serialize(Package package)
        {
            string xml;
            package.SaveToXML(out xml, null);
            return XDocument.Parse(xml);
        }

        private static XElement Component(XDocument xml, string name) => xml.Descendants().Single(element =>
            element.Name.LocalName == "component" && (string)element.Attribute("name") == name);

        private static void Require(bool condition, string code)
        {
            if (!condition) { throw new InvalidOperationException("schema.edit." + code); }
        }
    }
}