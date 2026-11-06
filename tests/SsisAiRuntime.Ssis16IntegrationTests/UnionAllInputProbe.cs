using System;
using System.Globalization;
using System.Linq;
using Microsoft.SqlServer.Dts.Pipeline.Wrapper;
using Microsoft.SqlServer.Dts.Runtime;
using RuntimeWrapper = Microsoft.SqlServer.Dts.Runtime.Wrapper;

namespace SsisAiRuntime.Ssis16IntegrationTests
{
    internal static class UnionAllInputProbe
    {
        public static void Run()
        {
            Probe("no_refresh");
            Probe("after_remove");
            Probe("after_upstream");
        }

        private static void Probe(string refreshStage)
        {
            var fixturePath = System.IO.Path.Combine(FindRepositoryRoot(), "tests", "LayoutFixtures", "Merge.dtsx");
            var stage = "load";
            try
            {
                using (var package = new Application().LoadPackage(fixturePath, null))
                {
                    var task = FindPipelineTask(package, "MergeFlow");
                    var pipeline = (IDTSPipeline130)task.InnerObject;
                    var sourceA = FindComponent(pipeline, "Source A");
                    var sourceB = FindComponent(pipeline, "Source B");
                    var union = FindComponent(pipeline, "Union All");
                    var sourceAOutput = sourceA.OutputCollection.Cast<IDTSOutput100>().First(output => !output.IsErrorOut);
                    var sourceBOutput = sourceB.OutputCollection.Cast<IDTSOutput100>().First(output => !output.IsErrorOut);
                    var sourceAColumn = AddColumn(sourceAOutput, "SharedValue");
                    AddColumn(sourceBOutput, "SharedValue");
                    var unionDesign = union.Instantiate();
                    var incomingPaths = pipeline.PathCollection.Cast<IDTSPath100>()
                        .Where(path => path.EndPoint.Component.ID == union.ID).ToArray();
                    foreach (var incomingPath in incomingPaths)
                    {
                        var output = (IDTSOutput100)incomingPath.StartPoint;
                        var input = (IDTSInput100)incomingPath.EndPoint;
                        pipeline.PathCollection.RemoveObjectByID(incomingPath.ID);
                        pipeline.PathCollection.New().AttachPathAndPropagateNotifications(output, input);
                    }
                    Console.WriteLine("UnionAll " + refreshStage + " after_path_refresh: " + State(union));
                    Console.WriteLine("UnionAll " + refreshStage + " setup: sourceColumns=" + sourceAOutput.OutputColumnCollection.Count +
                        "/" + sourceBOutput.OutputColumnCollection.Count + "; inputs=" + union.InputCollection.Count +
                        "; paths=" + pipeline.PathCollection.Count);
                    stage = "baseline_refresh";
                    unionDesign.ReinitializeMetaData();
                    Console.WriteLine("UnionAll " + refreshStage + " baseline: " + State(union));

                    var selectedPath = pipeline.PathCollection.Cast<IDTSPath100>().Single(path =>
                        path.StartPoint.Component.ID == sourceA.ID && path.EndPoint.Component.ID == union.ID);
                    var unionInput = (IDTSInput100)selectedPath.EndPoint;
                    var pathId = selectedPath.ID.ToString(CultureInfo.InvariantCulture);
                    var component = AddDerivedColumn(pipeline, "ProbeDerived", sourceAColumn.LineageID);
                    var derivedDesign = component.Instantiate();
                    var derivedOutput = component.OutputCollection.Cast<IDTSOutput100>().Single(output => !output.IsErrorOut);
                    var insertedColumn = derivedDesign.InsertOutputColumnAt(derivedOutput.ID, 0, "ProbeValue", string.Empty);
                    derivedDesign.SetOutputColumnDataTypeProperties(derivedOutput.ID, insertedColumn.ID,
                        RuntimeWrapper.DataType.DT_I4, 0, 0, 0, 0);
                    derivedDesign.SetOutputColumnProperty(derivedOutput.ID, insertedColumn.ID, "Expression",
                        "#" + sourceAColumn.LineageID.ToString(CultureInfo.InvariantCulture) + " + 1");
                    stage = "remove_selected_path";
                    pipeline.PathCollection.RemoveObjectByID(selectedPath.ID);
                    if (refreshStage == "after_remove")
                    {
                        stage = "refresh_after_remove";
                        unionDesign.ReinitializeMetaData();
                    }
                    stage = "attach_upstream";
                    pipeline.PathCollection.New().AttachPathAndPropagateNotifications(sourceAOutput, component.InputCollection[0]);
                    derivedDesign.SetUsageType(component.InputCollection[0].ID,
                        component.InputCollection[0].GetVirtualInput(), sourceAColumn.LineageID, DTSUsageType.UT_READONLY);
                    if (refreshStage == "after_upstream")
                    {
                        stage = "refresh_after_upstream";
                        unionDesign.ReinitializeMetaData();
                    }
                    stage = "attach_downstream";
                    pipeline.PathCollection.New().AttachPathAndPropagateNotifications(derivedOutput, unionInput);
                    stage = "refresh_after_downstream";
                    unionDesign.ReinitializeMetaData();
                    Console.WriteLine("UnionAll " + refreshStage + " after: " + State(union) +
                        "; paths=" + pipeline.PathCollection.Count + "; pathId=" + pathId);
                }
            }
            catch (Exception exception)
            {
                var code = exception is System.Runtime.InteropServices.COMException comException
                    ? "com_" + comException.ErrorCode.ToString("X8", CultureInfo.InvariantCulture)
                    : exception.GetType().Name;
                Console.WriteLine("UnionAll " + refreshStage + " failed at " + stage + " (" + code + ").");
            }
        }

        private static string State(IDTSComponentMetaData100 component)
        {
            var inputs = component.InputCollection.Cast<IDTSInput100>().Select(input => input.Name + "=" +
                string.Join(",", input.InputColumnCollection.Cast<IDTSInputColumn100>().Select(column =>
                    column.Name + ":" + column.LineageID)));
            var outputs = component.OutputCollection.Cast<IDTSOutput100>().Select(output => output.Name + "=" +
                string.Join(",", output.OutputColumnCollection.Cast<IDTSOutputColumn100>().Select(column =>
                    column.Name + ":" + column.DataType + ":" + column.LineageID + ":" + string.Join(";",
                        column.CustomPropertyCollection.Cast<IDTSCustomProperty100>().Select(property =>
                            property.Name + "=" + Convert.ToString(property.Value, CultureInfo.InvariantCulture))))));
            return string.Join("|", inputs) + " => " + string.Join("|", outputs);
        }

        private static IDTSOutputColumn100 AddColumn(IDTSOutput100 output, string name)
        {
            var column = output.OutputColumnCollection.New();
            column.Name = name;
            column.SetDataTypeProperties(RuntimeWrapper.DataType.DT_I4, 0, 0, 0, 0);
            return column;
        }

        private static IDTSComponentMetaData100 AddDerivedColumn(IDTSPipeline130 pipeline, string name, int lineageId)
        {
            var creationName = new Application().PipelineComponentInfos.Cast<PipelineComponentInfo>()
                .Single(info => info.CreationName.StartsWith("DTSTransform.DerivedColumn.", StringComparison.OrdinalIgnoreCase))
                .CreationName;
            var component = pipeline.ComponentMetaDataCollection.New();
            component.ComponentClassID = creationName;
            component.Instantiate().ProvideComponentProperties();
            component.Name = name;
            return component;
        }

        private static TaskHost FindPipelineTask(Package package, string name)
        {
            return package.Executables.Cast<Executable>().OfType<TaskHost>().Single(task => task.Name == name);
        }

        private static IDTSComponentMetaData100 FindComponent(IDTSPipeline130 pipeline, string name) =>
            pipeline.ComponentMetaDataCollection.Cast<IDTSComponentMetaData100>().Single(component => component.Name == name);

        private static string FindRepositoryRoot()
        {
            var directory = new System.IO.DirectoryInfo(Environment.CurrentDirectory);
            while (directory != null && !System.IO.File.Exists(System.IO.Path.Combine(directory.FullName, "SsisAiRuntime.sln")))
            { directory = directory.Parent; }
            return directory?.FullName ?? throw new System.IO.DirectoryNotFoundException("ssis.repository.root_missing");
        }
    }
}