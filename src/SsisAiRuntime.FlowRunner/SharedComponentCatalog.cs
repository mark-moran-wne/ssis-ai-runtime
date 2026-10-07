#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace SsisAiRuntime.FlowRunner
{
    internal static class SharedComponentCatalog
    {
        private const string ResourcePrefix = "SsisAiRuntime.ComponentCatalog.";
        private static readonly string[] RequiredFields = { "id", "vendor", "name", "creationName", "componentType" };
        private static readonly string[] OptionalFields = { "evidence", "recipe", "prerequisites", "inputs", "outputs", "properties" };
        private static readonly string[] Evidence = { "documented", "registration-verified", "metadata-probed", "execution-tested" };

        public static IReadOnlyList<JObject> Load()
        {
            var assembly = typeof(SharedComponentCatalog).Assembly;
            var definitions = new List<JObject>();
            foreach (var name in assembly.GetManifestResourceNames().Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
                .OrderBy(name => name, StringComparer.Ordinal))
            {
                using (var stream = assembly.GetManifestResourceStream(name))
                using (var reader = new StreamReader(stream ?? throw new InvalidDataException("catalog.resource.missing")))
                {
                    definitions.AddRange(Parse(reader.ReadToEnd()));
                }
            }
            if (definitions.Count == 0) { throw new InvalidDataException("catalog.definitions.missing"); }
            RejectDuplicates(definitions);
            return definitions.AsReadOnly();
        }

        public static IReadOnlyList<JObject> Parse(string json)
        {
            var document = JObject.Parse(json, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            if (document.Count != 3 || (string?)document["schemaVersion"] != "1.0" ||
                document["ssisMajorVersion"]?.Type != JTokenType.Integer ||
                !(document["components"] is JArray components) || components.Count == 0)
            { throw new InvalidDataException("catalog.document.invalid"); }
            var version = checked((int)document["ssisMajorVersion"]!);
            if (version < 1) { throw new InvalidDataException("catalog.version.invalid"); }
            var definitions = new List<JObject>();
            foreach (var token in components)
            {
                if (!(token is JObject entry) || entry.Properties().Any(property =>
                    !RequiredFields.Contains(property.Name) && !OptionalFields.Contains(property.Name)))
                { throw new InvalidDataException("catalog.entry.invalid"); }
                foreach (var field in RequiredFields)
                {
                    if (entry[field]?.Type != JTokenType.String || string.IsNullOrWhiteSpace((string?)entry[field]))
                    { throw new InvalidDataException("catalog.field.invalid"); }
                }
                if (!new[] { "SourceAdapter", "DestinationAdapter", "Transform" }.Contains((string?)entry["componentType"]))
                { throw new InvalidDataException("catalog.type.invalid"); }
                foreach (var field in new[] { "evidence", "prerequisites", "inputs", "outputs", "properties" })
                {
                    var value = entry[field];
                    if (value != null && value.Type != JTokenType.Null && (!(value is JArray array) ||
                        array.Any(item => item.Type != JTokenType.String || string.IsNullOrWhiteSpace((string?)item))))
                    { throw new InvalidDataException("catalog.list.invalid"); }
                }
                var evidence = entry["evidence"] as JArray;
                if (evidence != null && evidence.Any(item => !Evidence.Contains((string?)item)))
                { throw new InvalidDataException("catalog.evidence.invalid"); }
                if (entry["recipe"] != null && entry["recipe"]!.Type != JTokenType.Null &&
                    (entry["recipe"]!.Type != JTokenType.String || string.IsNullOrWhiteSpace((string?)entry["recipe"])))
                { throw new InvalidDataException("catalog.recipe.invalid"); }
                if (evidence != null && evidence.Values<string>().Contains("execution-tested") &&
                    (entry["recipe"]?.Type != JTokenType.String || string.IsNullOrWhiteSpace((string?)entry["recipe"])))
                { throw new InvalidDataException("catalog.execution.recipe_missing"); }
                var definition = (JObject)entry.DeepClone();
                definition["ssisMajorVersion"] = version;
                definition["evidence"] = evidence?.DeepClone() ?? new JArray("documented");
                definitions.Add(definition);
            }
            RejectDuplicates(definitions);
            return definitions.AsReadOnly();
        }

        private static void RejectDuplicates(IEnumerable<JObject> definitions)
        {
            if (definitions.GroupBy(entry => (int)entry["ssisMajorVersion"]! + ":" + (string?)entry["id"], StringComparer.OrdinalIgnoreCase)
                    .Any(group => group.Count() > 1) ||
                definitions.GroupBy(entry => (int)entry["ssisMajorVersion"]! + ":" + (string?)entry["creationName"], StringComparer.OrdinalIgnoreCase)
                    .Any(group => group.Count() > 1))
            { throw new InvalidDataException("catalog.identity.duplicate"); }
        }
    }
}