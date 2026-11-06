using System;
using System.Globalization;
using System.Linq;
using Microsoft.SqlServer.Dts.Pipeline.Wrapper;

namespace SsisAiRuntime.MutationHost
{
    public static class DataFlowPathSplicer
    {
        public static IDTSPath100 FindPath(IDTSPipeline130 pipeline, string pathNativeId)
        {
            if (pipeline == null) { throw new ArgumentNullException(nameof(pipeline)); }
            if (!int.TryParse(pathNativeId, NumberStyles.None, CultureInfo.InvariantCulture, out var pathId) || pathId <= 0)
            { throw new InvalidOperationException("mutation.insertion.path_id_invalid"); }
            var matches = pipeline.PathCollection.Cast<IDTSPath100>().Where(path => path.ID == pathId).ToArray();
            if (matches.Length != 1) { throw new InvalidOperationException("mutation.insertion.path_not_found_or_ambiguous"); }
            return matches[0];
        }

        public static void Splice(IDTSPipeline130 pipeline, string pathNativeId,
            IDTSComponentMetaData100 insertedComponent, Action<IDTSInput100> configureInput = null,
            string insertedOutputName = null, UnionAllInputAdapter preparedEndpointAdapter = null)
        {
            if (insertedComponent == null) { throw new ArgumentNullException(nameof(insertedComponent)); }
            var originalPath = FindPath(pipeline, pathNativeId);
            var sourceOutput = originalPath.StartPoint as IDTSOutput100;
            var originalDestinationInput = originalPath.EndPoint as IDTSInput100;
            var destinationInput = originalDestinationInput;
            var component = pipeline.ComponentMetaDataCollection.Cast<IDTSComponentMetaData100>()
                .SingleOrDefault(item => item.ID == insertedComponent.ID);
            var sourceComponentId = sourceOutput?.Component.ID ?? 0;
            var sourceOutputId = sourceOutput?.ID ?? 0;
            var insertedComponentId = insertedComponent.ID;
            var endpointAdapter = preparedEndpointAdapter ?? UnionAllInputAdapter.CreateIfRequired(pipeline, originalPath);
            var inputs = insertedComponent.InputCollection.Cast<IDTSInput100>().ToArray();
            var outputs = insertedComponent.OutputCollection.Cast<IDTSOutput100>().Where(output => !output.IsErrorOut).ToArray();
            if (component == null || sourceOutput == null || destinationInput == null ||
                originalPath.StartPoint.Component.ID == insertedComponent.ID ||
                originalPath.EndPoint.Component.ID == insertedComponent.ID || inputs.Length != 1 || outputs.Length != 1)
            { throw new InvalidOperationException("mutation.insertion.splice_contract_unsupported"); }

            var inputPath = default(IDTSPath100);
            var outputPath = default(IDTSPath100);
            var stage = "remove";
            try
            {
                if (endpointAdapter != null)
                {
                    stage = "endpoint_prepare";
                    endpointAdapter.ValidateOutputName(insertedOutputName);
                    destinationInput = endpointAdapter.Prepare();
                    sourceOutput = FindComponent(pipeline, sourceComponentId).OutputCollection.Cast<IDTSOutput100>()
                        .Single(output => output.ID == sourceOutputId);
                    insertedComponent = FindComponent(pipeline, insertedComponentId);
                    component = insertedComponent;
                    inputs = insertedComponent.InputCollection.Cast<IDTSInput100>().ToArray();
                    outputs = insertedComponent.OutputCollection.Cast<IDTSOutput100>()
                        .Where(output => !output.IsErrorOut).ToArray();
                }
                pipeline.PathCollection.RemoveObjectByID(originalPath.ID);
                stage = "upstream_attach";
                inputPath = pipeline.PathCollection.New();
                inputPath.AttachPathAndPropagateNotifications(sourceOutput, inputs[0]);
                stage = "component_configure";
                configureInput?.Invoke(inputs[0]);
                stage = "downstream_attach";
                if (endpointAdapter != null) { destinationInput = endpointAdapter.ResolveReplacementInput(); }
                outputPath = pipeline.PathCollection.New();
                outputPath.AttachPathAndPropagateNotifications(outputs[0], destinationInput);
                if (endpointAdapter != null)
                {
                    stage = "endpoint_refresh";
                    endpointAdapter.Complete(insertedOutputName);
                    var endpointFailure = endpointAdapter.Verify(insertedOutputName);
                    if (endpointFailure.Length != 0) { throw new InvalidOperationException(endpointFailure); }
                }
            }
            catch (Exception spliceException)
            {
                Exception rollbackFailure = null;
                try
                {
                    RemovePath(pipeline, outputPath);
                    RemovePath(pipeline, inputPath);
                    if (!pipeline.PathCollection.Cast<IDTSPath100>().Any(path =>
                        path.StartPoint.ID == sourceOutput.ID && path.EndPoint.ID == originalDestinationInput.ID))
                    {
                        var restoredPath = pipeline.PathCollection.New();
                        restoredPath.AttachPathAndPropagateNotifications(sourceOutput, originalDestinationInput);
                    }
                    endpointAdapter?.Rollback();
                }
                catch (Exception exception) { rollbackFailure = exception; }
                try
                {
                    if (!pipeline.PathCollection.Cast<IDTSPath100>().Any(path =>
                        path.StartPoint.Component.ID == insertedComponent.ID || path.EndPoint.Component.ID == insertedComponent.ID))
                    { pipeline.ComponentMetaDataCollection.RemoveObjectByID(insertedComponent.ID); }
                }
                catch (Exception exception) { rollbackFailure = rollbackFailure ?? exception; }
                var failureCode = rollbackFailure == null
                    ? "mutation.insertion.path_splice." + stage + "_failed"
                    : "mutation.insertion.path_splice.rollback_failed";
                var failure = rollbackFailure == null
                    ? (Exception)spliceException
                    : new AggregateException(spliceException, rollbackFailure);
                throw new InvalidOperationException(failureCode, failure);
            }
        }

        private static IDTSComponentMetaData100 FindComponent(IDTSPipeline130 pipeline, int componentId)
        {
            var matches = pipeline.ComponentMetaDataCollection.Cast<IDTSComponentMetaData100>()
                .Where(component => component.ID == componentId).ToArray();
            if (matches.Length != 1) { throw new InvalidOperationException("mutation.insertion.component_identity_changed"); }
            return matches[0];
        }

        private static void RemovePath(IDTSPipeline130 pipeline, IDTSPath100 path)
        {
            if (path != null && pipeline.PathCollection.Cast<IDTSPath100>().Any(item => item.ID == path.ID))
            { pipeline.PathCollection.RemoveObjectByID(path.ID); }
        }
    }
}