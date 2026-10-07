using System;
using System.Globalization;
using System.Linq;
using Microsoft.SqlServer.Dts.Pipeline.Wrapper;
using Microsoft.SqlServer.Dts.Runtime;
using Newtonsoft.Json.Linq;

namespace SsisAiRuntime.FlowRunner
{
    internal sealed class NativeComponentDescriber
    {
        public string Stage { get; private set; } = "resolve";

        public JObject Describe(string selector)
        {
            var definition = SharedComponentCatalog.Load().SingleOrDefault(entry =>
                (int)entry["ssisMajorVersion"] == 16 && string.Equals((string)entry["id"], selector, StringComparison.OrdinalIgnoreCase));
            var creationName = definition == null ? selector : (string)definition["creationName"];
            var registered = new Application().PipelineComponentInfos.Cast<PipelineComponentInfo>()
                .Where(info => string.Equals(info.CreationName, creationName, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (registered.Length != 1)
            {
                throw new ArgumentException("Select exactly one installed creationName or shared catalog ID; the component was missing or ambiguous.");
            }
            using (var package = new Package { Name = "ComponentDescription", ProtectionLevel = DTSProtectionLevel.DontSaveSensitive })
            {
                var host = (TaskHost)package.Executables.Add("STOCK:PipelineTask");
                var pipeline = (IDTSPipeline130)host.InnerObject;
                var component = pipeline.ComponentMetaDataCollection.New();
                component.ComponentClassID = registered[0].CreationName;
                Stage = "instantiate";
                var design = component.Instantiate();
                Stage = "provide-properties";
                design.ProvideComponentProperties();
                Stage = "project";
                JObject columnTemplate = null;
                if (registered[0].CreationName.StartsWith("DTSTransform.DerivedColumn.", StringComparison.OrdinalIgnoreCase) ||
                    registered[0].CreationName.StartsWith("DTSTransform.DataConvert.", StringComparison.OrdinalIgnoreCase))
                {
                    Stage = "column-template";
                    var port = component.OutputCollection.Cast<IDTSOutput100>().Single(output => !output.IsErrorOut);
                    var column = design.InsertOutputColumnAt(port.ID, 0, "ProbeColumn", string.Empty);
                    columnTemplate = new JObject
                    {
                        ["evidence"] = "SyntheticOutputColumnDefaults", ["dataType"] = column.DataType.ToString(),
                        ["properties"] = Properties(column.CustomPropertyCollection)
                    };
                    port.OutputColumnCollection.RemoveObjectByID(column.ID);
                    Stage = "project";
                }
                return new JObject
                {
                    ["name"] = registered[0].Name, ["creationName"] = registered[0].CreationName,
                    ["componentType"] = registered[0].ComponentType.ToString(),
                    ["evidence"] = "NativeInitializedMetadata", ["requiresVerifiedConfiguration"] = true,
                    ["properties"] = Properties(component.CustomPropertyCollection),
                    ["outputColumnTemplate"] = columnTemplate,
                    ["inputs"] = new JArray(component.InputCollection.Cast<IDTSInput100>().Select(input => new JObject
                    {
                        ["name"] = input.Name, ["properties"] = Properties(input.CustomPropertyCollection),
                        ["columns"] = new JArray(input.InputColumnCollection.Cast<IDTSInputColumn100>().Select(column => new JObject
                        {
                            ["name"] = column.Name, ["dataType"] = column.DataType.ToString(), ["length"] = column.Length,
                            ["properties"] = Properties(column.CustomPropertyCollection)
                        }))
                    })),
                    ["outputs"] = new JArray(component.OutputCollection.Cast<IDTSOutput100>().Select(output => new JObject
                    {
                        ["name"] = output.Name, ["isErrorOutput"] = output.IsErrorOut,
                        ["synchronousInput"] = output.SynchronousInputID == 0 ? null : component.InputCollection
                            .Cast<IDTSInput100>().Single(input => input.ID == output.SynchronousInputID).Name,
                        ["properties"] = Properties(output.CustomPropertyCollection),
                        ["columns"] = new JArray(output.OutputColumnCollection.Cast<IDTSOutputColumn100>().Select(column => new JObject
                        {
                            ["name"] = column.Name, ["dataType"] = column.DataType.ToString(), ["length"] = column.Length,
                            ["properties"] = Properties(column.CustomPropertyCollection)
                        }))
                    })),
                    ["connectionSlots"] = new JArray(component.RuntimeConnectionCollection.Cast<IDTSRuntimeConnection100>()
                        .Select(connection => new JObject { ["name"] = connection.Name })),
                    ["supportedDataTypes"] = null,
                    ["limitations"] = "Defaults and initialized columns are observations, not a complete supported-type or required-property specification. No connections, validation or execution were requested."
                };
            }
        }

        private static JArray Properties(IDTSCustomPropertyCollection100 properties) => new JArray(properties
            .Cast<IDTSCustomProperty100>().Select(property =>
            {
                var value = property.Value;
                var scalar = value == null || value is string || value is bool || value is char ||
                    value is byte || value is sbyte || value is short || value is ushort || value is int || value is uint ||
                    value is long || value is ulong || value is float || value is double || value is decimal;
                return new JObject
                {
                    ["name"] = property.Name, ["description"] = property.Description,
                    ["defaultType"] = value?.GetType().FullName,
                    ["defaultValue"] = value == null || !scalar ? null : JToken.FromObject(value),
                    ["defaultValueAvailable"] = scalar,
                    ["expressionType"] = property.ExpressionType.ToString(), ["containsId"] = property.ContainsID
                };
            }));
    }
}