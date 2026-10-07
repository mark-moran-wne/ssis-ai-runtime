using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.SqlServer.Dts.Runtime;
using Newtonsoft.Json.Linq;

namespace SsisAiRuntime.FlowRunner
{
    internal static class InstalledComponentCatalog
    {
        private static readonly string[] RecipePrefixes =
        {
            "DTSAdapter.FlatFileSource.",
            "DTSTransform.DerivedColumn.",
            "DTSTransform.DataConvert.",
            "DTSAdapter.FlatFileDestination."
        };

        public static JObject Discover()
        {
            var application = new Application();
            var registrations = application.PipelineComponentInfos.Cast<PipelineComponentInfo>().ToArray();
            var definitions = SharedComponentCatalog.Load().Where(entry => (int)entry["ssisMajorVersion"] == 16).ToArray();
            var configurable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prefix in RecipePrefixes)
            {
                var selected = registrations.FirstOrDefault(info =>
                    info.CreationName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
                if (selected != null) { configurable.Add(selected.CreationName); }
            }

            var components = new JArray(registrations.OrderBy(info => info.CreationName, StringComparer.Ordinal)
                .ThenBy(info => info.Name, StringComparer.Ordinal)
                .Select(info => new JObject
                {
                    ["name"] = info.Name,
                    ["creationName"] = info.CreationName,
                    ["componentType"] = info.ComponentType.ToString(),
                    ["discovered"] = true,
                    ["configurable"] = configurable.Contains(info.CreationName),
                    ["executionTestAvailable"] = configurable.Contains(info.CreationName),
                    ["executionTestedThisInvocation"] = false,
                    ["recipe"] = configurable.Contains(info.CreationName)
                        ? (info.CreationName.StartsWith("DTSTransform.DataConvert.", StringComparison.OrdinalIgnoreCase)
                            ? "synthetic-data-conversion-int32" : "synthetic-derived-int32") : null,
                    ["sharedDefinition"] = definitions.SingleOrDefault(entry => string.Equals(
                        (string)entry["creationName"], info.CreationName, StringComparison.OrdinalIgnoreCase))?.DeepClone()
                }));
            return new JObject
            {
                ["schemaVersion"] = "1.0",
                ["command"] = "flow.components",
                ["succeeded"] = true,
                ["code"] = "flow.components.discovered",
                ["exitCode"] = 0,
                ["componentCount"] = components.Count,
                ["configurableCount"] = registrations.Count(info => configurable.Contains(info.CreationName)),
                ["components"] = components,
                ["sharedDefinitionCount"] = definitions.Length,
                ["uninstalledDefinitions"] = new JArray(definitions.Where(entry => !registrations.Any(info =>
                    string.Equals(info.CreationName, (string)entry["creationName"], StringComparison.OrdinalIgnoreCase)))
                    .OrderBy(entry => (string)entry["id"], StringComparer.Ordinal).Select(entry => entry.DeepClone())),
                ["packageExecuted"] = false,
                ["databaseConnections"] = false
            };
        }
    }
}