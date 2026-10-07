#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SsisAiRuntime.FlowRunner
{
    internal sealed class FlowProbeRequest
    {
        private FlowProbeRequest(int[] values, string expression, int[] expectedValues,
            string recipe = "derived-column", string conversionType = "")
        {
            Values = new ReadOnlyCollection<int>(values);
            Expression = expression;
            ExpectedValues = new ReadOnlyCollection<int>(expectedValues);
            Recipe = recipe;
            ConversionType = conversionType;
        }

        public IReadOnlyList<int> Values { get; }
        public string Expression { get; }
        public IReadOnlyList<int> ExpectedValues { get; }
        public string Recipe { get; }
        public string ConversionType { get; }
        public IReadOnlyList<string> TextValues { get; } = Array.Empty<string>();
        public IReadOnlyList<string> ExpectedTextValues { get; } = Array.Empty<string>();
        public int SourceWidth { get; }
        public int DestinationWidth { get; }
        public int? WidenTo { get; }

        private FlowProbeRequest(string[] values, string[] expectedValues, int sourceWidth,
            int destinationWidth, int? widenTo) : this(Array.Empty<int>(), string.Empty, Array.Empty<int>(), "flat-file-text")
        {
            TextValues = new ReadOnlyCollection<string>(values);
            ExpectedTextValues = new ReadOnlyCollection<string>(expectedValues);
            SourceWidth = sourceWidth;
            DestinationWidth = destinationWidth;
            WidenTo = widenTo;
        }

        public static FlowProbeRequest Demo() => new FlowProbeRequest(
            new[] { -2, 0, 3 }, "Value + 1", new[] { -1, 1, 4 });

        public static FlowProbeRequest Read(TextReader reader)
        {
            var text = new StringBuilder();
            var buffer = new char[1024];
            int count;
            while ((count = reader.Read(buffer, 0, buffer.Length)) != 0)
            {
                if (text.Length + count > 65536) { throw new ArgumentException("flow.request.too_large"); }
                text.Append(buffer, 0, count);
            }
            var document = JObject.Parse(text.ToString(), new JsonLoadSettings
            {
                DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
            });
            var version = (string?)document["schemaVersion"];
            var recipe = version == "1.0" ? "derived-column" : (string?)document["recipe"];
            var conversion = recipe == "data-conversion";
            var textRecipe = recipe == "flat-file-text";
            var allowed = version == "1.0"
                ? new[] { "schemaVersion", "values", "expression", "expectedValues" }
                : textRecipe ? new[] { "schemaVersion", "recipe", "values", "expectedValues", "sourceWidth", "destinationWidth" }
                : new[] { "schemaVersion", "recipe", "values", conversion ? "conversionType" : "expression", "expectedValues" };
            if (textRecipe && document["widenTo"] != null) { allowed = allowed.Concat(new[] { "widenTo" }).ToArray(); }
            if (document.Count != allowed.Length || document.Properties().Any(property => !allowed.Contains(property.Name)) ||
                document["schemaVersion"]?.Type != JTokenType.String || (version != "1.0" && version != "1.1") ||
                (recipe != "derived-column" && recipe != "data-conversion" && !textRecipe) ||
                (version == "1.1" && document["recipe"]?.Type != JTokenType.String) ||
                (!conversion && !textRecipe && document["expression"]?.Type != JTokenType.String))
            { throw new ArgumentException("flow.request.invalid"); }
            if (textRecipe)
            {
                var sourceWidth = Width(document["sourceWidth"]);
                var destinationWidth = Width(document["destinationWidth"]);
                int? widenTo = document["widenTo"] == null ? (int?)null : Width(document["widenTo"]);
                if (widenTo.HasValue && widenTo.Value < Math.Max(sourceWidth, destinationWidth))
                { throw new ArgumentException("flow.request.width_invalid"); }
                var textValues = Strings(document["values"]);
                var expectedText = Strings(document["expectedValues"]);
                if (textValues.Length != expectedText.Length) { throw new ArgumentException("flow.request.row_count_mismatch"); }
                return new FlowProbeRequest(textValues, expectedText, sourceWidth, destinationWidth, widenTo);
            }
            var expression = document["expression"]?.Value<string>() ?? string.Empty;
            if (!conversion && (string.IsNullOrWhiteSpace(expression) || expression.Length > 4096))
            { throw new ArgumentException("flow.request.expression_invalid"); }
            var conversionType = (string?)document["conversionType"] ?? string.Empty;
            if (conversion && (document["conversionType"]?.Type != JTokenType.String ||
                (conversionType != "Int16" && conversionType != "Int64")))
            { throw new ArgumentException("flow.request.conversion_invalid"); }
            var values = Integers(document["values"]);
            var expected = Integers(document["expectedValues"]);
            if (values.Length != expected.Length) { throw new ArgumentException("flow.request.row_count_mismatch"); }
            return new FlowProbeRequest(values, expression, expected, recipe!, conversionType);
        }

        public string ToJson()
        {
            if (Recipe == "flat-file-text")
            {
                var text = new JObject
                {
                    ["schemaVersion"] = "1.1", ["recipe"] = Recipe,
                    ["values"] = new JArray(TextValues), ["expectedValues"] = new JArray(ExpectedTextValues),
                    ["sourceWidth"] = SourceWidth, ["destinationWidth"] = DestinationWidth
                };
                if (WidenTo.HasValue) { text["widenTo"] = WidenTo.Value; }
                return text.ToString(Formatting.None);
            }
            var document = new JObject
            {
                ["schemaVersion"] = "1.1",
                ["recipe"] = Recipe,
                ["values"] = new JArray(Values),
                ["expectedValues"] = new JArray(ExpectedValues)
            };
            if (Recipe == "data-conversion") { document["conversionType"] = ConversionType; }
            else { document["expression"] = Expression; }
            return document.ToString(Formatting.None);
        }

        private static int Width(JToken? token)
        {
            if (token?.Type != JTokenType.Integer) { throw new ArgumentException("flow.request.width_invalid"); }
            var width = checked((int)token);
            if (width < 1 || width > 4000) { throw new ArgumentException("flow.request.width_invalid"); }
            return width;
        }

        private static string[] Strings(JToken? token)
        {
            if (!(token is JArray array) || array.Count == 0 || array.Count > 1000 || array.Any(value =>
                value.Type != JTokenType.String || value.Value<string>()!.Length > 4000 ||
                value.Value<string>()!.Any(character => char.IsControl(character) || character == ',' || character == '"')))
            { throw new ArgumentException("flow.request.text_invalid"); }
            return array.Select(value => value.Value<string>()!).ToArray();
        }

        private static int[] Integers(JToken? token)
        {
            if (!(token is JArray array) || array.Count == 0 || array.Count > 1000 ||
                array.Any(value => value.Type != JTokenType.Integer))
            { throw new ArgumentException("flow.request.rows_invalid"); }
            return array.Select(value => checked((int)value)).ToArray();
        }
    }
}