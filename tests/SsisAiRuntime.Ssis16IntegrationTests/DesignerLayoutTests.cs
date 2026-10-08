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