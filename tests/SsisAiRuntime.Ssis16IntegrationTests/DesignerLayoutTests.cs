using System;
using System.IO;
using System.Linq;
using Microsoft.SqlServer.Dts.Pipeline.Wrapper;
using Microsoft.SqlServer.Dts.Runtime;
using SsisAiRuntime.MutationHost;

namespace SsisAiRuntime.Ssis16IntegrationTests
{
    internal static class DesignerLayoutTests
    {
        public static int Run()
        {
            try
            {
                VerifyLayoutParsing();
                VerifyPlacementAndComparison();
                VerifyNativeExtraction();
                VerifyNativeLayoutPropertyRoundTrip();
                VerifyFixtureGeneration();
                Console.WriteLine("Designer layout probe: PASS; native identity/topology, coordinate and route parsing, deterministic placement, refusal and layout comparison.");
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("Designer layout probe failed: " + error.Message);
                return 1;
            }
        }

        private static void VerifyLayoutParsing()
        {
            const string xml = "<DTS:DesignTimeProperties xmlns:DTS='www.microsoft.com/SqlServer/Dts'><DTS:LayoutInfo><DTS:GraphLayout>" +
                "<DTS:NodeLayout DTS:Id='101' DTS:TopLeft='100,120' DTS:Size='140,50'/>" +
                "<DTS:NodeLayout DTS:Id='102' DTS:Left='300' DTS:Top='120' DTS:Width='80' DTS:Height='50'/>" +
                "<DTS:EdgeLayout DTS:Id='path-1' DTS:Points='240,145;280,145'/>" +
                "</DTS:GraphLayout></DTS:LayoutInfo></DTS:DesignTimeProperties>";
            var nodes = DesignerLayoutExtractor.ParseLayout(xml);
            Require(nodes.Count == 2 && nodes["101"].Left == 100 && nodes["101"].Top == 120 &&
                nodes["101"].Width == 140 && nodes["101"].Height == 50, "parser.position");
            Require(nodes["102"].Left == 300 && nodes["102"].Height == 50, "parser.individual_coordinates");
            var ssdtNodes = DesignerLayoutExtractor.ParseLayout(
                "<Objects><Package><LayoutInfo><GraphLayout><NodeLayout Id='Package\\TwoComponentFlow' " +
                "TopLeft='28.5,23.5' Size='180,42'/></GraphLayout></LayoutInfo></Package></Objects>");
            Require(ssdtNodes.Count == 1 && ssdtNodes["Package\\TwoComponentFlow"].Left == 28.5m &&
                ssdtNodes["Package\\TwoComponentFlow"].Top == 23.5m, "parser.ssdt_reference_id_and_fractional_coordinates");
            const string dataFlowLayout = "<Objects><TaskHost design-time-name='Package\\Flow'><LayoutInfo><GraphLayout>" +
                "<NodeLayout Id='Package\\Flow\\Source' TopLeft='286.5,40.5' Size='114,42'/>" +
                "<EdgeLayout Id='Package\\Flow.Paths[Output]' TopLeft='100,200'><EdgeLayout.Curve>" +
                "<Curve Start='0,0'><Curve.Segments><SegmentCollection>" +
                "<CubicBezierSegment Point1='0,0' Point2='0,10' Point3='10,10'/>" +
                "</SegmentCollection></Curve.Segments></Curve></EdgeLayout.Curve></EdgeLayout>" +
                "</GraphLayout></LayoutInfo></TaskHost></Objects>";
            var dataFlowNodes = DesignerLayoutExtractor.ParseLayout(dataFlowLayout);
            Require(dataFlowNodes.Count == 1 && dataFlowNodes["Package\\Flow\\Source"].Left == 286.5m,
                "parser.taskhost_component_layout");
            var dataFlowRoutes = DesignerLayoutExtractor.ParseRoutePoints(dataFlowLayout);
            var sampledRoute = dataFlowRoutes["Package\\Flow.Paths[Output]"];
            Require(sampledRoute.Count == 9 && sampledRoute[0].Equals(new DesignerPoint(100m, 200m)) &&
                sampledRoute[8].Equals(new DesignerPoint(110m, 210m)), "parser.edge_curve_samples");
            var routes = DesignerLayoutExtractor.ParseRoutePoints(xml);
            Require(routes.Count == 1 && routes["path-1"].SequenceEqual(new[]
            {
                new DesignerPoint(240, 145), new DesignerPoint(280, 145)
            }), "parser.route_points");
            RequireThrows(() => DesignerLayoutExtractor.ParseLayout("<!DOCTYPE x [<!ENTITY e SYSTEM 'file:///c:/secret'>]><x>&e;</x>"),
                "parser.dtd_refused");
            RequireThrows(() => DesignerLayoutExtractor.ParseLayout("<x><NodeLayout Id='dup' TopLeft='1,2' Size='10,10'/><NodeLayout Id='dup' TopLeft='3,4' Size='10,10'/></x>"),
                "parser.duplicate_id_refused");
        }

        private static void VerifyPlacementAndComparison()
        {
            var owner = "flow-native-id";
            var first = new DesignerNode(owner, "101", "101", new DesignerNodePosition(100, 100, 100, 50));
            var second = new DesignerNode(owner, "102", "102", new DesignerNodePosition(300, 100, 80, 50));
            var otherFlowNode = new DesignerNode("other-flow", "201", "201", new DesignerNodePosition(412, 100, 60, 40));
            var path = new DesignerConnection(owner, "path-1", first.NativeId, "output-1", second.NativeId, "input-1",
                new[] { new DesignerPoint(200, 125), new DesignerPoint(280, 125) });
            var snapshot = new DesignerLayoutSnapshot(new[] { first, second, otherFlowNode }, new[] { path });
            var service = new DesignerLayoutService();
            var right = service.PlaceRightOf(snapshot, owner, second.NativeId, 60, 40);
            Require(right.Position.Left == 412 && right.Position.Top == 100, "placement.right_of");
            var below = service.PlaceBelow(snapshot, owner, first.NativeId, 60, 40);
            Require(below.Position.Left == 100 && below.Position.Top == 182, "placement.below");
            var openA = service.FindOpenSpace(snapshot, owner, 60, 40);
            var openB = service.FindOpenSpace(snapshot, owner, 60, 40);
            Require(openA.Position.Equals(openB.Position), "placement.deterministic");
            Require(!RectanglesOverlap(openA.Position, first.Position) && !RectanglesOverlap(openA.Position, second.Position),
                "placement.open_space");
            Require(RectanglesOverlap(openA.Position, otherFlowNode.Position), "placement.other_canvas_isolated");
            RequireThrows(() => service.PlaceRightOf(snapshot, owner, first.NativeId, 60, 40), "placement.route_collision");

            var same = new DesignerLayoutSnapshot(new[] { first, second, otherFlowNode }, new[] { path });
            Require(new DesignerLayoutComparer().Compare(snapshot, same).Matches, "compare.equal");
            var moved = new DesignerNode(owner, first.NativeId, first.LayoutId, new DesignerNodePosition(120, 100, 100, 50));
            var afterMove = new DesignerLayoutSnapshot(new[] { moved, second }, new[] { path });
            Require(!new DesignerLayoutComparer().Compare(snapshot, afterMove).Matches, "compare.position_change");
            Require(!new DesignerLayoutComparer().ComparePreservingExisting(snapshot, afterMove).Matches,
                "compare.existing_move_refused");

            var rerouted = new DesignerConnection(owner, path.NativeId, path.SourceNativeId, path.SourcePortNativeId,
                path.DestinationNativeId, path.DestinationPortNativeId, new[] { new DesignerPoint(210, 135) });
            var afterReroute = new DesignerLayoutSnapshot(new[] { first, second, otherFlowNode }, new[] { rerouted });
            Require(!new DesignerLayoutComparer().ComparePreservingExisting(snapshot, afterReroute).Matches,
                "compare.existing_route_change_refused");
            var afterDisconnect = new DesignerLayoutSnapshot(new[] { first, second, otherFlowNode }, Array.Empty<DesignerConnection>());
            Require(!new DesignerLayoutComparer().ComparePreservingExisting(snapshot, afterDisconnect).Matches,
                "compare.existing_connection_removal_refused");
            var addedNode = new DesignerNode(owner, "103", "103", new DesignerNodePosition(420, 100, 60, 40));
            var addedConnection = new DesignerConnection(owner, "path-2", second.NativeId, "output-2",
                addedNode.NativeId, "input-2");
            var afterInsertion = new DesignerLayoutSnapshot(new[] { first, second, otherFlowNode, addedNode },
                new[] { path, addedConnection });
            Require(new DesignerLayoutComparer().ComparePreservingExisting(snapshot, afterInsertion).Matches,
                "compare.new_node_and_connection_allowed");

            var unknown = new DesignerLayoutSnapshot(new[] { first,
                new DesignerNode(owner, "103", "103", null) }, Array.Empty<DesignerConnection>());
            RequireThrows(() => service.FindOpenSpace(unknown, owner, 20, 20), "placement.unknown_position_refused");
            var unknownOtherCanvas = new DesignerLayoutSnapshot(new[] { first, second,
                new DesignerNode("other-flow", "unknown", "unknown", null) }, new[] { path });
            Require(service.PlaceRightOf(unknownOtherCanvas, owner, second.NativeId, 20, 20).Position.Left == 412,
                "placement.unrelated_missing_position_ignored");
            var withUnexpectedNode = new DesignerLayoutSnapshot(new[] { first, second, otherFlowNode,
                new DesignerNode(owner, "extra", "extra", new DesignerNodePosition(500, 100, 20, 20)) }, new[] { path });
            Require(!new DesignerLayoutComparer().Compare(snapshot, withUnexpectedNode).Matches, "compare.extra_node");
            var normalized = service.Normalize(new DesignerLayoutSnapshot(new[]
            {
                new DesignerNode(owner, "104", "104", new DesignerNodePosition(-20, -10, 10, 10)), otherFlowNode
            }, new[] { path }), owner);
            Require(normalized.Nodes.Single(node => node.NativeId == "104").Position.Left == 32 &&
                normalized.Nodes.Single(node => node.NativeId == "104").Position.Top == 32,
                "placement.explicit_normalize");
            Require(normalized.Nodes.Single(node => node.NativeId == otherFlowNode.NativeId).Position.Equals(otherFlowNode.Position) &&
                normalized.Connections[0].RoutePoints[0].Equals(new DesignerPoint(252, 167)), "placement.normalize_scope_and_routes");
        }

        private static void VerifyNativeExtraction()
        {
            using (var package = new Package { Name = "DesignerLayoutFixture", ProtectionLevel = DTSProtectionLevel.DontSaveSensitive })
            {
                var task = (TaskHost)package.Executables.Add("STOCK:PipelineTask");
                var pipeline = (IDTSPipeline130)task.InnerObject;
                var source = NewComponent(pipeline, "DTSAdapter.FlatFileSource.");
                var destination = NewComponent(pipeline, "DTSAdapter.FlatFileDestination.");
                pipeline.PathCollection.New().AttachPathAndPropagateNotifications(
                    source.OutputCollection.Cast<IDTSOutput100>().First(output => !output.IsErrorOut),
                    destination.InputCollection[0]);

                var snapshot = new DesignerLayoutExtractor().Extract(package);
                var components = snapshot.Nodes.Where(node => node.OwnerNativeId == task.ID).ToArray();
                Require(components.Length == 2 && components.Select(node => node.NativeId).OrderBy(id => id)
                    .SequenceEqual(new[] { source.ID.ToString(), destination.ID.ToString() }.OrderBy(id => id)),
                    "extractor.native_component_ids");
                Require(snapshot.Connections.Count == 1 && snapshot.Connections[0].OwnerNativeId == task.ID &&
                    snapshot.Connections[0].SourceNativeId == source.ID.ToString() &&
                    snapshot.Connections[0].DestinationNativeId == destination.ID.ToString(), "extractor.native_path_endpoints");
                Require(components.All(node => !node.HasPosition) && snapshot.Diagnostics.Contains("designer.layout.position_unavailable"),
                    "extractor.no_fabricated_coordinates");
                RequireThrows(() => new DesignerLayoutService().FindOpenSpace(snapshot, task.ID, 40, 40),
                    "extractor.placement_blocked_without_layout");
                var preview = new DesignerPlacementPreviewer().Preview(package,
                    new ComponentPlacementIntent(task.ID, ComponentPlacementRelation.RightOf,
                        components[0].NativeId, 120, 42));
                Require(!preview.IsValid && preview.Placement == null &&
                    preview.RefusalCode == "designer.layout.position_unavailable",
                    "preview.unavailable_layout_refused");

            }
        }

        private static void VerifyNativeLayoutPropertyRoundTrip()
        {
            var designTimeProperty = typeof(Package).GetProperty("DesignTimeProperties");
            Require(designTimeProperty != null && designTimeProperty.PropertyType == typeof(string) &&
                designTimeProperty.GetMethod?.IsPublic == true && designTimeProperty.SetMethod?.IsPublic == true,
                "native_writer.public_package_property");
            using (var package = new Package { Name = "NativeLayoutPropertyFixture", ProtectionLevel = DTSProtectionLevel.DontSaveSensitive })
            {
                var task = (TaskHost)package.Executables.Add("STOCK:PipelineTask");
                task.Name = "LayoutFlow";
                var pipeline = (IDTSPipeline130)task.InnerObject;
                var source = NewComponent(pipeline, "DTSAdapter.FlatFileSource.");
                source.Name = "Source";
                var destination = NewComponent(pipeline, "DTSAdapter.FlatFileDestination.");
                destination.Name = "Destination";
                var output = source.OutputCollection.Cast<IDTSOutput100>().First(port => !port.IsErrorOut);
                pipeline.PathCollection.New().AttachPathAndPropagateNotifications(output, destination.InputCollection[0]);

                package.DesignTimeProperties = "<Objects Version='8'><Package design-time-name='Package'><LayoutInfo><GraphLayout>" +
                    "<NodeLayout Id='Package\\LayoutFlow' TopLeft='20.5,30.5' Size='180,42'/>" +
                    "</GraphLayout></LayoutInfo></Package><TaskHost design-time-name='Package\\LayoutFlow'><LayoutInfo><GraphLayout>" +
                    "<NodeLayout Id='Package\\LayoutFlow\\Source' TopLeft='80.5,50.5' Size='114,42'/>" +
                    "<NodeLayout Id='Package\\LayoutFlow\\Destination' TopLeft='250.5,50.5' Size='134,42'/>" +
                    "<EdgeLayout Id='Package\\LayoutFlow.Paths[Flat File Source Output]' TopLeft='194.5,71.5'>" +
                    "<EdgeLayout.Curve><Curve Start='0,0'><Curve.Segments><SegmentCollection>" +
                    "<LineSegment End='40,0'/></SegmentCollection></Curve.Segments></Curve></EdgeLayout.Curve>" +
                    "</EdgeLayout></GraphLayout></LayoutInfo></TaskHost></Objects>";

                string initialSerialization;
                package.SaveToXML(out initialSerialization, null);
                using (var baselinePackage = new Package())
                {
                    baselinePackage.LoadFromXML(initialSerialization, null);
                    var before = new DesignerLayoutExtractor().Extract(baselinePackage);
                    var flowTask = (TaskHost)baselinePackage.Executables[0];
                    var components = before.Nodes.Where(node => node.OwnerNativeId == flowTask.ID).ToArray();
                    Require(components.Length == 2 && components.All(node => node.HasPosition),
                        "native_writer.component_positions_reloaded");
                    Require(components.Single(node => node.LayoutId.EndsWith("\\Source", StringComparison.Ordinal)).Position.Left == 80.5m &&
                        components.Single(node => node.LayoutId.EndsWith("\\Destination", StringComparison.Ordinal)).Position.Left == 250.5m,
                        "native_writer.fractional_positions_preserved");
                    Require(before.Connections.Single().RoutePoints.Count == 2,
                        "native_writer.edge_curve_reloaded");

                    var sourceNode = components.Single(node => node.LayoutId.EndsWith("\\Source", StringComparison.Ordinal));
                    var sourceComponent = ((IDTSPipeline130)flowTask.InnerObject).ComponentMetaDataCollection
                        .Cast<IDTSComponentMetaData100>().Single(component => component.Name == "Source");
                    var placementPreview = new DesignerPlacementPreviewer().Preview(baselinePackage,
                        new ComponentPlacementIntent(flowTask.ID, ComponentPlacementRelation.RightOf,
                            components.Single(node => node.LayoutId.EndsWith("\\Destination", StringComparison.Ordinal)).NativeId,
                            (int)sourceNode.Position.Width, (int)sourceNode.Position.Height));
                    Require(placementPreview.IsValid, "native_writer.placement_intent_resolved");
                    var placement = placementPreview.Placement;
                    new DesignerLayoutWriter().SetComponentPosition(baselinePackage, flowTask.ID,
                        sourceComponent.ID.ToString(), placement);
                    var beforeSave = new DesignerLayoutExtractor().Extract(baselinePackage);
                    var movedSource = beforeSave.Nodes.Single(node => node.NativeId == sourceNode.NativeId);
                    var unchangedDestination = beforeSave.Nodes.Single(node => node.LayoutId.EndsWith("\\Destination", StringComparison.Ordinal));
                    Require(movedSource.HasPosition && unchangedDestination.HasPosition,
                        "native_writer.positions_extracted_after_write");
                    Require(placement.Position.Left == 416.5m && placement.Position.Top == 50.5m &&
                        movedSource.Position.Left == placement.Position.Left && movedSource.Position.Top == placement.Position.Top,
                        "native_writer.component_position_applied");
                    Require(unchangedDestination.Position.Left == 250.5m &&
                        new DesignerLayoutComparer().ComparePreservingExisting(before, beforeSave,
                            new[] { sourceNode.Key }).Matches, "native_writer.existing_layout_and_route_preserved");

                    string serialized;
                    baselinePackage.SaveToXML(out serialized, null);
                    using (var reloaded = new Package())
                    {
                        reloaded.LoadFromXML(serialized, null);
                        var after = new DesignerLayoutExtractor().Extract(reloaded);
                        var comparison = new DesignerLayoutComparer().Compare(beforeSave, after);
                        if (!comparison.Matches)
                        { throw new InvalidOperationException("native_writer.layout_roundtrip_matches:" + string.Join(",", comparison.Differences)); }
                    }
                }
            }
        }

        private static void VerifyFixtureGeneration()
        {
            var directory = Path.Combine(Path.GetTempPath(), "SsisLayoutFixtures-" + Guid.NewGuid().ToString("N"));
            try
            {
                var paths = DesignerLayoutFixtureGenerator.Create(directory);
                Require(paths.Count == 7 && paths.All(File.Exists), "fixtures.created");
                DesignerLayoutFixtureGenerator.Verify(directory);
                var original = File.ReadAllBytes(paths[0]);
                var roundTripRefused = false;
                try { DesignerLayoutFixtureGenerator.VerifyRoundTrip(directory); }
                catch (InvalidOperationException error)
                { roundTripRefused = error.Message.StartsWith("designer.fixtures.layout_missing:", StringComparison.Ordinal); }
                Require(roundTripRefused && File.ReadAllBytes(paths[0]).SequenceEqual(original),
                    "fixtures.roundtrip_requires_layout_and_preserves_source");
                var refused = false;
                try { DesignerLayoutFixtureGenerator.Create(directory); }
                catch (IOException) { refused = true; }
                Require(refused && File.ReadAllBytes(paths[0]).SequenceEqual(original), "fixtures.overwrite_refused");
            }
            finally
            {
                if (Directory.Exists(directory)) { Directory.Delete(directory, true); }
            }
        }

        private static IDTSComponentMetaData100 NewComponent(IDTSPipeline130 pipeline, string creationNamePrefix)
        {
            var creationName = new Application().PipelineComponentInfos.Cast<PipelineComponentInfo>()
                .First(info => info.CreationName.StartsWith(creationNamePrefix, StringComparison.OrdinalIgnoreCase)).CreationName;
            var component = pipeline.ComponentMetaDataCollection.New();
            component.ComponentClassID = creationName;
            component.Instantiate().ProvideComponentProperties();
            return component;
        }

        private static bool RectanglesOverlap(DesignerNodePosition first, DesignerNodePosition second) =>
            first.Left < second.Right && first.Right > second.Left && first.Top < second.Bottom && first.Bottom > second.Top;

        private static void RequireThrows(Action action, string code)
        {
            try { action(); }
            catch (InvalidOperationException) { return; }
            throw new InvalidOperationException(code);
        }

        private static void Require(bool condition, string code)
        {
            if (!condition) { throw new InvalidOperationException(code); }
        }
    }
}