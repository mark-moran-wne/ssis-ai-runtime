using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.SqlServer.Dts.Pipeline.Wrapper;
using Microsoft.SqlServer.Dts.Runtime;
using SsisAiRuntime.MutationHost;

namespace SsisAiRuntime.Ssis16IntegrationTests
{
    internal static class DesignerLayoutFixtureGenerator
    {
        private sealed class FixtureDefinition
        {
            public FixtureDefinition(string fileName, int taskCount, int componentCount, int pathCount,
                Action<Package> build)
            { FileName = fileName; TaskCount = taskCount; ComponentCount = componentCount; PathCount = pathCount; Build = build; }
            public string FileName { get; }
            public int TaskCount { get; }
            public int ComponentCount { get; }
            public int PathCount { get; }
            public Action<Package> Build { get; }
        }

        private static readonly FixtureDefinition[] Fixtures =
        {
            new FixtureDefinition("SingleComponent.dtsx", 1, 1, 0, SingleComponent),
            new FixtureDefinition("TwoComponentHorizontal.dtsx", 1, 2, 1, TwoComponent),
            new FixtureDefinition("TwoComponentVertical.dtsx", 1, 2, 1, TwoComponent),
            new FixtureDefinition("Branch.dtsx", 1, 4, 3, Branch),
            new FixtureDefinition("Merge.dtsx", 1, 4, 3, Merge),
            new FixtureDefinition("CrossedPaths.dtsx", 1, 4, 2, CrossedPaths),
            new FixtureDefinition("NestedDataFlows.dtsx", 2, 2, 0, NestedDataFlows)
        };

        public static IReadOnlyList<string> Create(string outputDirectory)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory)) { throw new ArgumentException("An output directory is required.", nameof(outputDirectory)); }
            var directory = Path.GetFullPath(outputDirectory);
            Directory.CreateDirectory(directory);
            var paths = Fixtures.Select(fixture => Path.Combine(directory, fixture.FileName)).ToArray();
            if (paths.Any(File.Exists)) { throw new IOException("designer.fixtures.destination_exists"); }

            var created = new List<string>();
            try
            {
                for (var index = 0; index < Fixtures.Length; index++)
                {
                    created.Add(paths[index]);
                    using (var package = new Package
                    {
                        Name = Path.GetFileNameWithoutExtension(Fixtures[index].FileName),
                        ProtectionLevel = DTSProtectionLevel.DontSaveSensitive
                    })
                    {
                        Fixtures[index].Build(package);
                        new Application().SaveToXml(paths[index], package, null);
                    }
                }
                return paths;
            }
            catch
            {
                foreach (var path in created)
                {
                    try { File.Delete(path); }
                    catch { }
                }
                throw;
            }
        }

        public static void Verify(string directory)
        {
            foreach (var fixture in Fixtures)
            {
                var path = Path.Combine(directory, fixture.FileName);
                var package = new Application().LoadPackage(path, null);
                if (package == null) { throw new InvalidOperationException("designer.fixtures.reload_failed"); }
                try
                {
                    var tasks = TaskHosts(package).ToArray();
                    var components = tasks.SelectMany(task => ((IDTSPipeline130)task.InnerObject)
                        .ComponentMetaDataCollection.Cast<IDTSComponentMetaData100>()).ToArray();
                    var paths = tasks.SelectMany(task => ((IDTSPipeline130)task.InnerObject)
                        .PathCollection.Cast<IDTSPath100>()).ToArray();
                    if (tasks.Length != fixture.TaskCount || components.Length != fixture.ComponentCount || paths.Length != fixture.PathCount)
                    { throw new InvalidOperationException("designer.fixtures.native_roundtrip_mismatch:" + fixture.FileName); }
                }
                finally { package.Dispose(); }
            }
        }

        public static void Inspect(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            { throw new DirectoryNotFoundException("designer.fixtures.directory_missing"); }
            foreach (var fixture in Fixtures)
            {
                var path = Path.Combine(directory, fixture.FileName);
                if (!File.Exists(path)) { throw new FileNotFoundException("designer.fixtures.file_missing", fixture.FileName); }
                var package = new Application().LoadPackage(path, null);
                if (package == null) { throw new InvalidOperationException("designer.fixtures.reload_failed"); }
                try
                {
                    var snapshot = new DesignerLayoutExtractor().Extract(package);
                    var positioned = snapshot.Nodes.Count(node => node.HasPosition);
                    var routed = snapshot.Connections.Count(connection => connection.RoutePoints.Count > 0);
                    Console.WriteLine(fixture.FileName + ": nodes=" + snapshot.Nodes.Count + ", positioned=" + positioned +
                        ", paths=" + snapshot.Connections.Count + ", routed=" + routed + ", diagnostics=" +
                        (snapshot.Diagnostics.Count == 0 ? "none" : string.Join(",", snapshot.Diagnostics)));
                    foreach (var node in snapshot.Nodes)
                    {
                        Console.WriteLine("  node owner=" + node.OwnerNativeId + " id=" + node.NativeId +
                            (node.HasPosition ? " position=" + node.Position.Left + "," + node.Position.Top + "," +
                                node.Position.Width + "," + node.Position.Height : " position=unavailable"));
                    }
                    foreach (var connection in snapshot.Connections)
                    {
                        Console.WriteLine("  path id=" + connection.NativeId + " from=" + connection.SourceNativeId + "/" +
                            connection.SourcePortNativeId + " to=" + connection.DestinationNativeId + "/" +
                            connection.DestinationPortNativeId + " routePoints=" + connection.RoutePoints.Count);
                    }
                }
                finally { package.Dispose(); }
            }
        }

        private static void SingleComponent(Package package)
        {
            var pipeline = AddPipeline(package, "SingleComponentFlow");
            AddComponent(pipeline, "DTSTransform.DerivedColumn.", "Derived Column");
        }

        private static void TwoComponent(Package package)
        {
            var pipeline = AddPipeline(package, "TwoComponentFlow");
            var source = AddComponent(pipeline, "DTSAdapter.FlatFileSource.", "Source");
            var destination = AddComponent(pipeline, "DTSAdapter.FlatFileDestination.", "Destination");
            Connect(pipeline, source.OutputCollection.Cast<IDTSOutput100>().First(output => !output.IsErrorOut), destination.InputCollection[0]);
        }

        private static void Branch(Package package)
        {
            var pipeline = AddPipeline(package, "BranchFlow");
            var source = AddComponent(pipeline, "DTSAdapter.FlatFileSource.", "Source");
            var multicast = AddComponent(pipeline, "DTSTransform.Multicast.", "Multicast");
            var destinationA = AddComponent(pipeline, "DTSAdapter.FlatFileDestination.", "Destination A");
            var destinationB = AddComponent(pipeline, "DTSAdapter.FlatFileDestination.", "Destination B");
            var sourceOutput = source.OutputCollection.Cast<IDTSOutput100>().First(output => !output.IsErrorOut);
            Connect(pipeline, sourceOutput, multicast.InputCollection[0]);
            var primaryOutput = multicast.OutputCollection.Cast<IDTSOutput100>().First(output => !output.IsErrorOut);
            var branchOutput = multicast.OutputCollection.New();
            branchOutput.Name = "Output 2";
            branchOutput.SynchronousInputID = multicast.InputCollection[0].ID;
            branchOutput.ExclusionGroup = primaryOutput.ExclusionGroup;
            Connect(pipeline, primaryOutput, destinationA.InputCollection[0]);
            Connect(pipeline, branchOutput, destinationB.InputCollection[0]);
        }

        private static void Merge(Package package)
        {
            var pipeline = AddPipeline(package, "MergeFlow");
            var sourceA = AddComponent(pipeline, "DTSAdapter.FlatFileSource.", "Source A");
            var sourceB = AddComponent(pipeline, "DTSAdapter.FlatFileSource.", "Source B");
            var union = AddComponent(pipeline, "DTSTransform.UnionAll.", "Union All");
            var destination = AddComponent(pipeline, "DTSAdapter.FlatFileDestination.", "Destination");
            Connect(pipeline, sourceA.OutputCollection.Cast<IDTSOutput100>().First(output => !output.IsErrorOut), union.InputCollection[0]);
            Connect(pipeline, sourceB.OutputCollection.Cast<IDTSOutput100>().First(output => !output.IsErrorOut), union.InputCollection[1]);
            Connect(pipeline, union.OutputCollection.Cast<IDTSOutput100>().First(output => !output.IsErrorOut), destination.InputCollection[0]);
        }

        private static void CrossedPaths(Package package)
        {
            var pipeline = AddPipeline(package, "CrossedPathsFlow");
            var sourceA = AddComponent(pipeline, "DTSAdapter.FlatFileSource.", "Source A");
            var sourceB = AddComponent(pipeline, "DTSAdapter.FlatFileSource.", "Source B");
            var destinationA = AddComponent(pipeline, "DTSAdapter.FlatFileDestination.", "Destination A");
            var destinationB = AddComponent(pipeline, "DTSAdapter.FlatFileDestination.", "Destination B");
            Connect(pipeline, sourceA.OutputCollection.Cast<IDTSOutput100>().First(output => !output.IsErrorOut), destinationB.InputCollection[0]);
            Connect(pipeline, sourceB.OutputCollection.Cast<IDTSOutput100>().First(output => !output.IsErrorOut), destinationA.InputCollection[0]);
        }

        private static void NestedDataFlows(Package package)
        {
            var sequenceExecutable = package.Executables.Add("STOCK:Sequence");
            ((DtsContainer)sequenceExecutable).Name = "NestedSequence";
            var sequence = (IDTSSequence)sequenceExecutable;
            AddNestedPipeline(sequence, "NestedFlowA", "Derived Column");
            AddNestedPipeline(sequence, "NestedFlowB", "Data Conversion");
        }

        private static IDTSPipeline130 AddNestedPipeline(IDTSSequence sequence, string taskName, string componentName)
        {
            var task = (TaskHost)sequence.Executables.Add("STOCK:PipelineTask");
            task.Name = taskName;
            var pipeline = (IDTSPipeline130)task.InnerObject;
            AddComponent(pipeline, componentName == "Derived Column" ? "DTSTransform.DerivedColumn." : "DTSTransform.DataConvert.", componentName);
            return pipeline;
        }

        private static IDTSPipeline130 AddPipeline(Package package, string taskName)
        {
            var task = (TaskHost)package.Executables.Add("STOCK:PipelineTask");
            task.Name = taskName;
            return (IDTSPipeline130)task.InnerObject;
        }

        private static IDTSComponentMetaData100 AddComponent(IDTSPipeline130 pipeline, string creationNamePrefix, string name)
        {
            var creationName = new Application().PipelineComponentInfos.Cast<PipelineComponentInfo>()
                .First(info => info.CreationName.StartsWith(creationNamePrefix, StringComparison.OrdinalIgnoreCase)).CreationName;
            var component = pipeline.ComponentMetaDataCollection.New();
            component.ComponentClassID = creationName;
            component.Instantiate().ProvideComponentProperties();
            component.Name = name;
            return component;
        }

        private static void Connect(IDTSPipeline130 pipeline, IDTSOutput100 output, IDTSInput100 input) =>
            pipeline.PathCollection.New().AttachPathAndPropagateNotifications(output, input);

        private static IEnumerable<TaskHost> TaskHosts(DtsContainer container)
        {
            if (container is TaskHost task) { yield return task; }
            if (container is IDTSSequence sequence)
            {
                foreach (Executable executable in sequence.Executables)
                {
                    foreach (var child in TaskHosts((DtsContainer)executable)) { yield return child; }
                }
            }
        }
    }
}