using System;
using System.Linq;
using System.Xml.Linq;
using Microsoft.SqlServer.Dts.Pipeline.Wrapper;
using Microsoft.SqlServer.Dts.Runtime;
using SsisAiRuntime.MutationHost;
using RuntimeWrapper = Microsoft.SqlServer.Dts.Runtime.Wrapper;

namespace SsisAiRuntime.Ssis16IntegrationTests
{
    internal static class InMemoryPipelineEditTests
    {
        public static void Run(Func<string, string> findComponent)
        {
            using (var package = new Package { Name = "InMemoryEditFixture", ProtectionLevel = DTSProtectionLevel.DontSaveSensitive })
            {
                var task = (TaskHost)package.Executables.Add("STOCK:PipelineTask");
                task.Name = "ExistingFlow";
                var pipeline = (IDTSPipeline130)task.InnerObject;
                var source = pipeline.ComponentMetaDataCollection.New();
                source.ComponentClassID = findComponent("DTSAdapter.OleDbSource.");
                source.Instantiate().ProvideComponentProperties();
                source.Name = "Source";
                var sourceOutput = source.OutputCollection.Cast<IDTSOutput100>().First(port => !port.IsErrorOut);
                var first = sourceOutput.OutputColumnCollection.New();
                first.Name = "FirstNumber";
                first.SetDataTypeProperties(RuntimeWrapper.DataType.DT_I4, 0, 0, 0, 0);
                var second = sourceOutput.OutputColumnCollection.New();
                second.Name = "SecondNumber";
                second.SetDataTypeProperties(RuntimeWrapper.DataType.DT_I4, 0, 0, 0, 0);

                var destination = pipeline.ComponentMetaDataCollection.New();
                destination.ComponentClassID = findComponent("DTSAdapter.OleDbDestination.");
                var destinationDesign = destination.Instantiate();
                destinationDesign.ProvideComponentProperties();
                destination.Name = "Destination";
                var destinationInput = destination.InputCollection[0];
                var external = destinationInput.ExternalMetadataColumnCollection.New();
                external.Name = "Value";
                external.DataType = RuntimeWrapper.DataType.DT_I4;
                var directPath = pipeline.PathCollection.New();
                directPath.AttachPathAndPropagateNotifications(sourceOutput, destinationInput);
                var selected = destinationDesign.SetUsageType(destinationInput.ID, destinationInput.GetVirtualInput(),
                    first.LineageID, DTSUsageType.UT_READONLY);
                destinationDesign.MapInputColumn(destinationInput.ID, selected.ID, external.ID);

                package.DesignTimeProperties = "<Objects Version='8'><Package design-time-name='Package'><LayoutInfo><GraphLayout>" +
                    "<NodeLayout Id='Package\\ExistingFlow' TopLeft='20,20' Size='180,42'/>" +
                    "</GraphLayout></LayoutInfo></Package><TaskHost design-time-name='Package\\ExistingFlow'><LayoutInfo><GraphLayout>" +
                    "<NodeLayout Id='Package\\ExistingFlow\\Source' TopLeft='100,100' Size='114,42'/>" +
                    "<NodeLayout Id='Package\\ExistingFlow\\Destination' TopLeft='350,100' Size='134,42'/>" +
                    "</GraphLayout></LayoutInfo></TaskHost></Objects>";
                var layoutBeforeInsertion = new DesignerLayoutExtractor().Extract(package);
                var replacedConnectionKey = layoutBeforeInsertion.Connections.Single().Key;
                var placementPreview = new DesignerPlacementPreviewer().Preview(package,
                    new ComponentPlacementIntent(task.ID, ComponentPlacementRelation.RightOf,
                        destination.ID.ToString(), 140, 42));
                Require(placementPreview.IsValid, "derived.layout.placement_intent_resolved");
                var derivedPlacement = placementPreview.Placement;

                var original = Serialize(package);
                var originalSource = Component(original, "Source");
                AssertMapping(original, "FirstNumber", "Value");

                destinationDesign.SetUsageType(destinationInput.ID, destinationInput.GetVirtualInput(),
                    first.LineageID, DTSUsageType.UT_IGNORED);
                selected = destinationDesign.SetUsageType(destinationInput.ID, destinationInput.GetVirtualInput(),
                    second.LineageID, DTSUsageType.UT_READONLY);
                destinationDesign.MapInputColumn(destinationInput.ID, selected.ID, external.ID);
                var remapped = Serialize(package);
                AssertMapping(remapped, "SecondNumber", "Value");
                Require(JointCount(remapped, "component") == 2 && JointCount(remapped, "path") == 1, "remap.structure");
                Require(XNode.DeepEquals(originalSource, Component(remapped, "Source")), "remap.source.unchanged");

                var insertionPlan = DerivedColumnInsertionPlan.Create(package, new string('a', 64), task.ID,
                    source.ID.ToString(), destination.ID.ToString(), second.LineageID, "AddCalculatedValue",
                    "CalculatedNumber", "#" + second.LineageID + " + 1",
                    new ComponentPlacementIntent(task.ID, ComponentPlacementRelation.RightOf,
                        destination.ID.ToString(), 140, 42));
                Require(insertionPlan.MatchesCurrent(package, new string('a', 64)), "derived.plan.current");
                insertionPlan.Apply(package);
                Require(insertionPlan.Verify(package), "derived.plan.verify_immediately_after_insert");
                var derived = pipeline.ComponentMetaDataCollection.Cast<IDTSComponentMetaData100>()
                    .Single(component => component.Name == insertionPlan.ComponentName);
                var derivedDesign = derived.Instantiate();
                var derivedOutput = derived.OutputCollection.Cast<IDTSOutput100>().First(port => !port.IsErrorOut);
                var calculated = derivedOutput.OutputColumnCollection.Cast<IDTSOutputColumn100>()
                    .Single(column => column.Name == insertionPlan.OutputName);
                selected = destinationDesign.SetUsageType(destinationInput.ID, destinationInput.GetVirtualInput(),
                    second.LineageID, DTSUsageType.UT_READONLY);
                destinationDesign.MapInputColumn(destinationInput.ID, selected.ID, external.ID);
                var calculatedExternal = destinationInput.ExternalMetadataColumnCollection.New();
                calculatedExternal.Name = "CalculatedValue";
                calculatedExternal.DataType = RuntimeWrapper.DataType.DT_I4;
                var calculatedInput = destinationDesign.SetUsageType(destinationInput.ID, destinationInput.GetVirtualInput(),
                    calculated.LineageID, DTSUsageType.UT_READONLY);
                destinationDesign.MapInputColumn(destinationInput.ID, calculatedInput.ID, calculatedExternal.ID);

                var edited = Serialize(package);
                Require(JointCount(edited, "component") == 3 && JointCount(edited, "path") == 2, "derived.structure");
                Require(XNode.DeepEquals(originalSource, Component(edited, "Source")), "derived.source.unchanged");
                var derivedXml = Component(edited, "AddCalculatedValue");
                var outputXml = derivedXml.Descendants().Single(element => element.Name.LocalName == "outputColumn" &&
                    (string)element.Attribute("name") == "CalculatedNumber");
                Require((string)outputXml.Attribute("dataType") == "i4", "derived.output.type");
                var expression = outputXml.Descendants().Single(element => element.Name.LocalName == "property" &&
                    (string)element.Attribute("name") == "Expression");
                var secondXml = Component(edited, "Source").Descendants().Single(element =>
                    element.Name.LocalName == "outputColumn" && (string)element.Attribute("name") == "SecondNumber");
                var serializedLineage = (string)secondXml.Attribute("lineageId");
                Require(expression.Value == "#" + second.LineageID + " + 1" ||
                    expression.Value == "#{" + serializedLineage + "} + 1" ||
                    expression.Value == "#" + serializedLineage + " + 1", "derived.expression");
                AssertMapping(edited, "SecondNumber", "Value");
                AssertMapping(edited, "CalculatedNumber", "CalculatedValue");
                Require(Component(edited, "Destination").Descendants().Count(element =>
                    element.Name.LocalName == "inputColumn") == 2, "destination.input.inventory");
                AssertPath(edited, Component(edited, "Source"), derivedXml);
                AssertPath(edited, derivedXml, Component(edited, "Destination"));
                Require(package.Name == "InMemoryEditFixture" && task.Name == "ExistingFlow", "package.identity");
                var layoutAfterInsertion = new DesignerLayoutExtractor().Extract(package);
                var insertedLayoutNode = layoutAfterInsertion.Nodes.Single(node => node.OwnerNativeId == task.ID &&
                    node.NativeId == derived.ID.ToString());
                Require(insertedLayoutNode.Position != null && insertedLayoutNode.Position.Left == derivedPlacement.Position.Left &&
                    insertedLayoutNode.Position.Top == derivedPlacement.Position.Top, "derived.layout.placement_applied");
                Require(new DesignerLayoutComparer().ComparePreservingExisting(layoutBeforeInsertion,
                    layoutAfterInsertion, explicitlyReplacedConnectionKeys: new[] { replacedConnectionKey }).Matches,
                    "derived.layout.existing_nodes_and_routes_preserved");

                using (var reloaded = new Package())
                {
                    reloaded.LoadFromXML(edited.ToString(), null);
                    var roundTrip = Serialize(reloaded);
                    Require(reloaded.ID == package.ID && reloaded.Name == package.Name, "reload.identity");
                    Require(JointCount(roundTrip, "component") == 3 && JointCount(roundTrip, "path") == 2,
                        "reload.structure");
                    AssertMapping(roundTrip, "SecondNumber", "Value");
                    AssertMapping(roundTrip, "CalculatedNumber", "CalculatedValue");
                    var roundTripDerived = Component(roundTrip, "AddCalculatedValue");
                    AssertPath(roundTrip, Component(roundTrip, "Source"), roundTripDerived);
                    AssertPath(roundTrip, roundTripDerived, Component(roundTrip, "Destination"));
                    var roundTripLayout = new DesignerLayoutExtractor().Extract(reloaded);
                    var roundTripSourceLayout = roundTripLayout.Nodes.Single(node =>
                        node.LayoutId == "Package\\ExistingFlow\\Source");
                    var roundTripDestinationLayout = roundTripLayout.Nodes.Single(node =>
                        node.LayoutId == "Package\\ExistingFlow\\Destination");
                    var roundTripDerivedLayout = roundTripLayout.Nodes.Single(node =>
                        node.LayoutId == "Package\\ExistingFlow\\AddCalculatedValue");
                    Require(roundTripSourceLayout.Position.Equals(layoutBeforeInsertion.Nodes.Single(node =>
                        node.LayoutId == "Package\\ExistingFlow\\Source").Position) &&
                        roundTripDestinationLayout.Position.Equals(layoutBeforeInsertion.Nodes.Single(node =>
                            node.LayoutId == "Package\\ExistingFlow\\Destination").Position) &&
                        roundTripDerivedLayout.Position.Left == derivedPlacement.Position.Left &&
                        roundTripDerivedLayout.Position.Top == derivedPlacement.Position.Top,
                        "derived.layout.positions_reloaded");
                }
            }
            Console.WriteLine("In-memory pipeline edits: PASS; destination remapping, positioned Derived Column insertion, layout/path preservation and reload; no files, database connection, execution, or validation.");
        }

        private static XDocument Serialize(Package package)
        {
            string xml;
            package.SaveToXML(out xml, null);
            return XDocument.Parse(xml);
        }

        private static XElement Component(XDocument xml, string name) => xml.Descendants()
            .Single(element => element.Name.LocalName == "component" && (string)element.Attribute("name") == name);

        private static int JointCount(XDocument xml, string elementName) =>
            xml.Descendants().Count(element => element.Name.LocalName == elementName);

        private static void AssertMapping(XDocument xml, string inputName, string externalName)
        {
            var destination = Component(xml, "Destination");
            var input = destination.Descendants().Single(element => element.Name.LocalName == "inputColumn" &&
                (string)element.Attribute("cachedName") == inputName);
            var external = destination.Descendants().Single(element => element.Name.LocalName == "externalMetadataColumn" &&
                (string)element.Attribute("name") == externalName);
            Require((string)input.Attribute("externalMetadataColumnId") == (string)external.Attribute("refId") &&
                input.Attribute("externalMetadataColumnId") != null, "mapping.external");
            var output = xml.Descendants().Single(element => element.Name.LocalName == "outputColumn" &&
                (string)element.Attribute("name") == inputName);
            Require((string)input.Attribute("lineageId") == (string)output.Attribute("lineageId") &&
                input.Attribute("lineageId") != null, "mapping.lineage");
        }

        private static void AssertPath(XDocument xml, XElement source, XElement destination)
        {
            var outputs = source.Descendants().Where(element => element.Name.LocalName == "output")
                .Select(element => (string)element.Attribute("refId")).ToArray();
            var inputs = destination.Descendants().Where(element => element.Name.LocalName == "input")
                .Select(element => (string)element.Attribute("refId")).ToArray();
            Require(xml.Descendants().Any(element => element.Name.LocalName == "path" &&
                outputs.Contains((string)element.Attribute("startId")) && inputs.Contains((string)element.Attribute("endId"))), "path.endpoints");
        }

        private static void Require(bool condition, string code)
        {
            if (!condition) { throw new InvalidOperationException("pipeline.edit." + code); }
        }
    }
}