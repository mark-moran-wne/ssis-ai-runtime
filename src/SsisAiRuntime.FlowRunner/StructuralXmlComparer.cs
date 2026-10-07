#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;

namespace SsisAiRuntime.FlowRunner
{
    internal static class StructuralXmlComparer
    {
        public static JObject Compare(TextReader input)
        {
            var buffer = new char[4096];
            var text = new StringBuilder();
            int count;
            while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
            {
                if (text.Length + count > 4 * 1024 * 1024) { throw new InvalidDataException("Comparison request exceeds four million characters."); }
                text.Append(buffer, 0, count);
            }
            var request = JObject.Parse(text.ToString(), new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            if (request.Count != 3 || (string?)request["schemaVersion"] != "1.0" ||
                request["beforeXml"]?.Type != JTokenType.String || request["afterXml"]?.Type != JTokenType.String)
            { throw new InvalidDataException("Expected schemaVersion 1.0, beforeXml and afterXml only."); }
            var before = Flatten(ReadXml(request["beforeXml"]!.Value<string>()!));
            var after = Flatten(ReadXml(request["afterXml"]!.Value<string>()!));
            var changes = new JArray();
            var changeCount = 0;
            foreach (var path in before.Keys.Union(after.Keys, StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal))
            {
                var oldPresent = before.TryGetValue(path, out var oldValue);
                var newPresent = after.TryGetValue(path, out var newValue);
                if (oldPresent && newPresent && string.Equals(oldValue, newValue, StringComparison.Ordinal)) { continue; }
                changeCount++;
                if (changes.Count < 500)
                {
                    changes.Add(new JObject
                    {
                        ["path"] = path, ["kind"] = !oldPresent ? "Added" : !newPresent ? "Removed" : "Changed",
                        ["before"] = oldValue, ["after"] = newValue
                    });
                }
            }
            return new JObject
            {
                ["comparisonKind"] = "structural-xml", ["isMatch"] = changeCount == 0,
                ["changeCount"] = changeCount, ["changesOmitted"] = changeCount - changes.Count,
                ["changes"] = changes, ["semanticsVerified"] = false,
                ["generatedIdsIgnored"] = false
            };
        }

        private static XDocument ReadXml(string xml)
        {
            if (xml.Length > 2 * 1024 * 1024) { throw new InvalidDataException("Each XML document is limited to two million characters."); }
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2 * 1024 * 1024 };
            using (var reader = XmlReader.Create(new StringReader(xml), settings))
            {
                var nodes = 0;
                while (reader.Read())
                {
                    if (++nodes > 50000 || reader.Depth > 64) { throw new InvalidDataException("XML exceeds the node or depth limit."); }
                }
            }
            using (var reader = XmlReader.Create(new StringReader(xml), settings)) { return XDocument.Load(reader, LoadOptions.PreserveWhitespace); }
        }

        private static Dictionary<string, string> Flatten(XDocument document)
        {
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            var paths = new Dictionary<XElement, string>();
            foreach (var element in document.Root!.DescendantsAndSelf())
            {
                var ordinal = element.ElementsBeforeSelf(element.Name).Count();
                var path = (element.Parent == null ? string.Empty : paths[element.Parent]) + "/" + element.Name + "[" + ordinal + "]";
                paths.Add(element, path);
                fields.Add(path, "element");
                foreach (var attribute in element.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration))
                { fields.Add(path + "/@" + attribute.Name, attribute.Value); }
                var value = string.Concat(element.Nodes().OfType<XText>().Select(node => node.Value));
                if (!element.HasElements || !string.IsNullOrWhiteSpace(value)) { fields.Add(path + "/text()", value); }
            }
            return fields;
        }
    }
}