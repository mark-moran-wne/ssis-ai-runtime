using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using SsisAiRuntime.AI;
using SsisAiRuntime.Inspectors;

namespace SsisAiRuntime.Corpus
{
    public static class CorpusSchema
    {
        public const string Kind = "ssis-ai-runtime.corpus.baseline";
        public const string CurrentVersion = "1.0";

        public static bool IsCompatible(string version)
        {
            if (string.IsNullOrWhiteSpace(version)) { return false; }
            var parts = version.Split('.');
            var currentParts = CurrentVersion.Split('.');
            if (parts.Length != 2 || currentParts.Length != 2) { return false; }
            int major;
            int minor;
            int currentMajor;
            return int.TryParse(parts[0], out major) && int.TryParse(parts[1], out minor) &&
                int.TryParse(currentParts[0], out currentMajor) && major >= 0 && minor >= 0 && major == currentMajor;
        }
    }

    public sealed class CorpusCoverageGap
    {
        public CorpusCoverageGap(string reasonCode, int count)
        {
            if (string.IsNullOrWhiteSpace(reasonCode)) { throw new ArgumentException("A reason code is required.", nameof(reasonCode)); }
            if (count < 0) { throw new ArgumentOutOfRangeException(nameof(count)); }
            ReasonCode = reasonCode;
            Count = count;
        }

        public string ReasonCode { get; }
        public int Count { get; }
    }

    public sealed class CorpusNode
    {
        public CorpusNode(string key, SemanticObjectKind kind, string name, string nativeId, string parentId)
        {
            if (string.IsNullOrWhiteSpace(key)) { throw new ArgumentException("A node key is required.", nameof(key)); }
            Key = key;
            Kind = kind;
            Name = name ?? string.Empty;
            NativeId = nativeId ?? string.Empty;
            ParentId = parentId ?? string.Empty;
        }

        public string Key { get; }
        public SemanticObjectKind Kind { get; }
        public string Name { get; }
        public string NativeId { get; }
        public string ParentId { get; }
    }

    public sealed class CorpusEdge
    {
        public CorpusEdge(string from, string to, DependencyKind kind, string evidence)
        {
            if (string.IsNullOrWhiteSpace(from)) { throw new ArgumentException("An edge source is required.", nameof(from)); }
            if (string.IsNullOrWhiteSpace(to)) { throw new ArgumentException("An edge target is required.", nameof(to)); }
            From = from;
            To = to;
            Kind = kind;
            Evidence = DependencyEvidence.Safe(evidence);
        }

        public string From { get; }
        public string To { get; }
        public DependencyKind Kind { get; }
        public string Evidence { get; }
    }

    public sealed class CorpusSnapshot
    {
        public CorpusSnapshot(string schemaVersion, string packageId, string packageName,
            IEnumerable<CorpusNode> nodes, IEnumerable<CorpusEdge> edges, IEnumerable<CorpusCoverageGap> coverageGaps)
        {
            if (string.IsNullOrWhiteSpace(schemaVersion)) { throw new ArgumentException("A schema version is required.", nameof(schemaVersion)); }
            SchemaVersion = schemaVersion;
            PackageId = packageId ?? string.Empty;
            PackageName = packageName ?? string.Empty;
            Nodes = ReadOnly(nodes, nameof(nodes));
            Edges = ReadOnly(edges, nameof(edges));
            CoverageGaps = ReadOnly(coverageGaps, nameof(coverageGaps));
        }

        public string SchemaVersion { get; }
        public string PackageId { get; }
        public string PackageName { get; }
        public IReadOnlyList<CorpusNode> Nodes { get; }
        public IReadOnlyList<CorpusEdge> Edges { get; }
        public IReadOnlyList<CorpusCoverageGap> CoverageGaps { get; }
        public bool IsComplete => CoverageGaps.Count == 0;

        private static IReadOnlyList<T> ReadOnly<T>(IEnumerable<T> values, string name)
        {
            if (values == null) { throw new ArgumentNullException(name); }
            return new ReadOnlyCollection<T>(new List<T>(values));
        }
    }

    public sealed class CorpusSnapshotBuilder
    {
        public CorpusSnapshot Build(PackageAnalysisSnapshot snapshot)
        {
            if (snapshot == null) { throw new ArgumentNullException(nameof(snapshot)); }
            var keyMap = snapshot.Dependencies.Nodes.ToDictionary(node => node.Key,
                node => IsSystemVariable(node) ? StableSystemVariableKey(node) : node.Key, StringComparer.Ordinal);
            var nodes = snapshot.Dependencies.Nodes
                .Select(node => new CorpusNode(keyMap[node.Key], node.Kind, node.Name,
                    IsSystemVariable(node) ? string.Empty : node.NativeId, node.ParentId))
                .OrderBy(node => node.Key, StringComparer.Ordinal);
            var edges = snapshot.Dependencies.Edges
                .Select(edge => new CorpusEdge(keyMap[edge.From], keyMap[edge.To], edge.Kind, edge.Evidence))
                .OrderBy(edge => edge.From, StringComparer.Ordinal)
                .ThenBy(edge => edge.To, StringComparer.Ordinal)
                .ThenBy(edge => edge.Kind)
                .ThenBy(edge => edge.Evidence, StringComparer.Ordinal);
            var gaps = snapshot.UnsupportedItems
                .GroupBy(item => string.IsNullOrWhiteSpace(item.ReasonCode) ? UnsupportedItem.UnspecifiedCode : item.ReasonCode,
                    StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new CorpusCoverageGap(group.Key, group.Count()));
            return new CorpusSnapshot(CorpusSchema.CurrentVersion, snapshot.Package.PackageId, snapshot.Package.PackageName,
                nodes, edges, gaps);
        }

        private static bool IsSystemVariable(DependencyNode node) => node.Kind == SemanticObjectKind.Variable &&
            node.Key.StartsWith("Variable:SystemVariable:", StringComparison.Ordinal) &&
            node.Name.StartsWith("System::", StringComparison.Ordinal);

        private static string StableSystemVariableKey(DependencyNode node) =>
            "Variable:SystemVariable:" + Uri.EscapeDataString(node.ParentId) + ":" + Uri.EscapeDataString(node.Name);
    }
}