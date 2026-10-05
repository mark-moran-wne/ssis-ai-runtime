#nullable enable
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using SsisAiRuntime.Inspectors;

namespace SsisAiRuntime.Corpus
{
    public static class CorpusBaselineStore
    {
        public static bool TryRead(string path, out CorpusSnapshot snapshot, out string errorCode, out string errorMessage,
            out string baselineVersion)
        {
            snapshot = null!;
            errorCode = string.Empty;
            errorMessage = string.Empty;
            baselineVersion = string.Empty;
            try
            {
                var root = JObject.Parse(File.ReadAllText(path));
                if (!string.Equals((string?)root["kind"], CorpusSchema.Kind, StringComparison.Ordinal))
                {
                    errorCode = "corpus.baseline.invalid";
                    errorMessage = "The baseline file is not a recognized corpus baseline document.";
                    return false;
                }

                baselineVersion = (string?)root["schemaVersion"] ?? string.Empty;
                if (!CorpusSchema.IsCompatible(baselineVersion))
                {
                    errorCode = "corpus.baseline.incompatible";
                    errorMessage = "The baseline schema version is incompatible with this CLI.";
                    return false;
                }

                if (!(root["snapshot"] is JObject snapshotObject))
                {
                    errorCode = "corpus.baseline.invalid";
                    errorMessage = "The baseline file does not contain a snapshot payload.";
                    return false;
                }

                var embeddedVersion = (string?)snapshotObject["schemaVersion"] ?? string.Empty;
                if (!CorpusSchema.IsCompatible(embeddedVersion))
                {
                    errorCode = "corpus.baseline.incompatible";
                    errorMessage = "The embedded baseline schema version is incompatible with this CLI.";
                    return false;
                }

                snapshot = ParseSnapshot(snapshotObject, baselineVersion);
                return true;
            }
            catch (IOException)
            {
                errorCode = "corpus.baseline.read_failed";
                errorMessage = "The baseline file could not be read.";
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                errorCode = "corpus.baseline.read_failed";
                errorMessage = "The baseline file could not be read.";
                return false;
            }
            catch (Exception)
            {
                errorCode = "corpus.baseline.invalid";
                errorMessage = "The baseline file is not valid JSON for this command.";
                return false;
            }
        }

        public static bool TryWrite(string path, CorpusSnapshot snapshot, string command, string packagePath,
            string previousVersion, bool upgraded, out string errorCode, out string errorMessage)
        {
            errorCode = string.Empty;
            errorMessage = string.Empty;
            var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory)) { Directory.CreateDirectory(directory); }
                File.WriteAllText(temporaryPath, CreateDocument(snapshot, command, packagePath, previousVersion, upgraded)
                    .ToString(Newtonsoft.Json.Formatting.Indented));
                if (File.Exists(path)) { File.Replace(temporaryPath, path, null); }
                else { File.Move(temporaryPath, path); }
                return true;
            }
            catch (IOException)
            {
                errorCode = "corpus.baseline.write_failed";
                errorMessage = "The baseline file could not be written.";
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                errorCode = "corpus.baseline.write_failed";
                errorMessage = "The baseline file could not be written.";
                return false;
            }
            finally
            {
                try { if (File.Exists(temporaryPath)) { File.Delete(temporaryPath); } }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        public static JObject CreateDocument(CorpusSnapshot snapshot, string command, string packagePath,
            string previousVersion, bool upgraded)
        {
            if (snapshot == null) { throw new ArgumentNullException(nameof(snapshot)); }
            return new JObject
            {
                ["kind"] = CorpusSchema.Kind,
                ["schemaVersion"] = CorpusSchema.CurrentVersion,
                ["generatedUtc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                ["generator"] = "SsisAiRuntime.Cli",
                ["command"] = command,
                ["packagePath"] = Path.GetFileName(packagePath),
                ["upgraded"] = upgraded,
                ["previousSchemaVersion"] = previousVersion,
                ["snapshot"] = ProjectSnapshot(snapshot)
            };
        }

        public static JObject ProjectSnapshot(CorpusSnapshot snapshot)
        {
            if (snapshot == null) { throw new ArgumentNullException(nameof(snapshot)); }
            return new JObject
            {
                ["schemaVersion"] = snapshot.SchemaVersion,
                ["package"] = new JObject { ["id"] = snapshot.PackageId, ["name"] = snapshot.PackageName },
                ["isComplete"] = snapshot.IsComplete,
                ["nodeCount"] = snapshot.Nodes.Count,
                ["edgeCount"] = snapshot.Edges.Count,
                ["coverageGapCount"] = snapshot.CoverageGaps.Sum(gap => gap.Count),
                ["nodes"] = new JArray(snapshot.Nodes.Select(node => new JObject
                {
                    ["key"] = node.Key, ["kind"] = node.Kind.ToString(), ["name"] = node.Name,
                    ["nativeId"] = node.NativeId, ["parentId"] = node.ParentId
                })),
                ["edges"] = new JArray(snapshot.Edges.Select(edge => new JObject
                {
                    ["from"] = edge.From, ["to"] = edge.To, ["kind"] = edge.Kind.ToString(), ["evidence"] = edge.Evidence
                })),
                ["coverageGaps"] = new JArray(snapshot.CoverageGaps.Select(gap => new JObject
                {
                    ["reasonCode"] = gap.ReasonCode, ["count"] = gap.Count
                }))
            };
        }

        private static CorpusSnapshot ParseSnapshot(JObject snapshot, string documentVersion)
        {
            var schemaVersion = RequiredString(snapshot, "schemaVersion");
            if (!CorpusSchema.IsCompatible(schemaVersion) || !string.Equals(schemaVersion, documentVersion, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The document and snapshot schema versions do not agree.");
            }
            if (!(snapshot["package"] is JObject package)) { throw new InvalidDataException("The package identity is missing."); }
            var packageId = RequiredString(package, "id");
            var packageName = RequiredString(package, "name", true);

            var nodes = RequiredArray(snapshot, "nodes").Select(token =>
            {
                if (!(token is JObject node)) { throw new InvalidDataException("A node entry is invalid."); }
                return new CorpusNode(RequiredString(node, "key"), RequiredEnum<SemanticObjectKind>(node, "kind"),
                    RequiredString(node, "name", true), RequiredString(node, "nativeId", true), RequiredString(node, "parentId", true));
            }).ToArray();
            if (nodes.Select(node => node.Key).Distinct(StringComparer.Ordinal).Count() != nodes.Length)
            { throw new InvalidDataException("Node keys must be unique."); }

            var edges = RequiredArray(snapshot, "edges").Select(token =>
            {
                if (!(token is JObject edge)) { throw new InvalidDataException("An edge entry is invalid."); }
                var evidence = RequiredString(edge, "evidence", true);
                if (!string.Equals(evidence, DependencyEvidence.Safe(evidence), StringComparison.Ordinal))
                { throw new InvalidDataException("The edge evidence value is unsupported."); }
                return new CorpusEdge(RequiredString(edge, "from"), RequiredString(edge, "to"),
                    RequiredEnum<DependencyKind>(edge, "kind"), evidence);
            }).ToArray();
            var nodeKeys = new System.Collections.Generic.HashSet<string>(nodes.Select(node => node.Key), StringComparer.Ordinal);
            if (edges.Any(edge => !nodeKeys.Contains(edge.From) || !nodeKeys.Contains(edge.To)))
            { throw new InvalidDataException("An edge endpoint is missing from the node inventory."); }

            var coverage = RequiredArray(snapshot, "coverageGaps").Select(token =>
            {
                if (!(token is JObject gap)) { throw new InvalidDataException("A coverage entry is invalid."); }
                var reasonCode = RequiredString(gap, "reasonCode");
                var countToken = gap["count"];
                if (countToken == null || countToken.Type != JTokenType.Integer ||
                    !int.TryParse(countToken.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count <= 0)
                { throw new InvalidDataException("A coverage count is invalid."); }
                new UnsupportedItem(string.Empty, string.Empty, string.Empty, string.Empty, reasonCode);
                return new CorpusCoverageGap(reasonCode, count);
            }).ToArray();
            if (coverage.Select(gap => gap.ReasonCode).Distinct(StringComparer.Ordinal).Count() != coverage.Length)
            { throw new InvalidDataException("Coverage reason codes must be unique."); }

            RequireCount(snapshot, "nodeCount", nodes.Length);
            RequireCount(snapshot, "edgeCount", edges.Length);
            RequireCount(snapshot, "coverageGapCount", coverage.Sum(gap => gap.Count));
            if (snapshot["isComplete"] == null || snapshot["isComplete"]!.Type != JTokenType.Boolean ||
                snapshot["isComplete"]!.Value<bool>() != (coverage.Length == 0))
            { throw new InvalidDataException("The snapshot completeness summary is inconsistent."); }

            return new CorpusSnapshot(schemaVersion, packageId, packageName, nodes, edges, coverage);
        }

        private static JArray RequiredArray(JObject value, string property)
        {
            if (!(value[property] is JArray array)) { throw new InvalidDataException("A required baseline collection is missing."); }
            return array;
        }

        private static string RequiredString(JObject value, string property, bool allowEmpty = false)
        {
            var token = value[property];
            if (token == null || token.Type != JTokenType.String) { throw new InvalidDataException("A required baseline string is missing."); }
            var text = token.Value<string>() ?? string.Empty;
            if (!allowEmpty && string.IsNullOrWhiteSpace(text)) { throw new InvalidDataException("A required baseline string is empty."); }
            return text;
        }

        private static T RequiredEnum<T>(JObject value, string property) where T : struct
        {
            var text = RequiredString(value, property);
            if (!Enum.TryParse(text, false, out T result) || !Enum.IsDefined(typeof(T), result))
            { throw new InvalidDataException("A baseline enum value is unsupported."); }
            return result;
        }

        private static void RequireCount(JObject value, string property, int expected)
        {
            var token = value[property];
            if (token == null || token.Type != JTokenType.Integer ||
                !int.TryParse(token.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var actual) || actual != expected)
            { throw new InvalidDataException("A snapshot count is inconsistent."); }
        }
    }
}