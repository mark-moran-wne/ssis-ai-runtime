using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using SsisAiRuntime.Inspectors;

namespace SsisAiRuntime.AI
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
            int currentMajor;
            int minor;
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

    public sealed class CorpusDiffResult
    {
        public CorpusDiffResult(IEnumerable<string> addedNodes, IEnumerable<string> removedNodes,
            IEnumerable<string> addedEdges, IEnumerable<string> removedEdges,
            IEnumerable<string> changedNodes, bool packageChanged,
            IEnumerable<CorpusCoverageGap> addedCoverageGaps, IEnumerable<CorpusCoverageGap> removedCoverageGaps)
        {
            AddedNodes = ReadOnly(addedNodes, nameof(addedNodes));
            RemovedNodes = ReadOnly(removedNodes, nameof(removedNodes));
            AddedEdges = ReadOnly(addedEdges, nameof(addedEdges));
            RemovedEdges = ReadOnly(removedEdges, nameof(removedEdges));
            ChangedNodes = ReadOnly(changedNodes, nameof(changedNodes));
            PackageChanged = packageChanged;
            AddedCoverageGaps = ReadOnly(addedCoverageGaps, nameof(addedCoverageGaps));
            RemovedCoverageGaps = ReadOnly(removedCoverageGaps, nameof(removedCoverageGaps));
        }

        public IReadOnlyList<string> AddedNodes { get; }
        public IReadOnlyList<string> RemovedNodes { get; }
        public IReadOnlyList<string> AddedEdges { get; }
        public IReadOnlyList<string> RemovedEdges { get; }
        public IReadOnlyList<string> ChangedNodes { get; }
        public bool PackageChanged { get; }
        public IReadOnlyList<CorpusCoverageGap> AddedCoverageGaps { get; }
        public IReadOnlyList<CorpusCoverageGap> RemovedCoverageGaps { get; }
        public bool IsMatch => !PackageChanged && AddedNodes.Count == 0 && RemovedNodes.Count == 0 && ChangedNodes.Count == 0 &&
            AddedEdges.Count == 0 && RemovedEdges.Count == 0 &&
            AddedCoverageGaps.Count == 0 && RemovedCoverageGaps.Count == 0;

        private static IReadOnlyList<T> ReadOnly<T>(IEnumerable<T> values, string name)
        {
            if (values == null) { throw new ArgumentNullException(name); }
            return new ReadOnlyCollection<T>(new List<T>(values));
        }
    }

    public sealed class CorpusDiffEngine
    {
        public CorpusDiffResult Diff(CorpusSnapshot baseline, CorpusSnapshot candidate)
        {
            if (baseline == null) { throw new ArgumentNullException(nameof(baseline)); }
            if (candidate == null) { throw new ArgumentNullException(nameof(candidate)); }

            var baselineNodes = baseline.Nodes.GroupBy(node => node.Key, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            var candidateNodes = candidate.Nodes.GroupBy(node => node.Key, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            var baselineEdges = new HashSet<string>(baseline.Edges.Select(Signature), StringComparer.Ordinal);
            var candidateEdges = new HashSet<string>(candidate.Edges.Select(Signature), StringComparer.Ordinal);
            var baselineGaps = baseline.CoverageGaps.ToDictionary(gap => gap.ReasonCode, gap => gap.Count, StringComparer.Ordinal);
            var candidateGaps = candidate.CoverageGaps.ToDictionary(gap => gap.ReasonCode, gap => gap.Count, StringComparer.Ordinal);

            var addedNodes = candidateNodes.Keys.Except(baselineNodes.Keys, StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal);
            var removedNodes = baselineNodes.Keys.Except(candidateNodes.Keys, StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal);
            var changedNodes = baselineNodes.Keys.Intersect(candidateNodes.Keys, StringComparer.Ordinal)
                .Where(key => !Equivalent(baselineNodes[key], candidateNodes[key]))
                .OrderBy(item => item, StringComparer.Ordinal);
            var addedEdges = candidateEdges.Except(baselineEdges, StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal);
            var removedEdges = baselineEdges.Except(candidateEdges, StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal);

            var addedGaps = new List<CorpusCoverageGap>();
            var removedGaps = new List<CorpusCoverageGap>();
            foreach (var code in candidateGaps.Keys.Union(baselineGaps.Keys, StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal))
            {
                var baselineCount = baselineGaps.ContainsKey(code) ? baselineGaps[code] : 0;
                var candidateCount = candidateGaps.ContainsKey(code) ? candidateGaps[code] : 0;
                if (candidateCount > baselineCount) { addedGaps.Add(new CorpusCoverageGap(code, candidateCount - baselineCount)); }
                if (baselineCount > candidateCount) { removedGaps.Add(new CorpusCoverageGap(code, baselineCount - candidateCount)); }
            }

            var packageChanged = !string.Equals(baseline.PackageId, candidate.PackageId, StringComparison.Ordinal) ||
                !string.Equals(baseline.PackageName, candidate.PackageName, StringComparison.Ordinal);
            return new CorpusDiffResult(addedNodes, removedNodes, addedEdges, removedEdges, changedNodes,
                packageChanged, addedGaps, removedGaps);
        }

        private static bool Equivalent(CorpusNode left, CorpusNode right) => left.Kind == right.Kind &&
            string.Equals(left.Name, right.Name, StringComparison.Ordinal) &&
            string.Equals(left.NativeId, right.NativeId, StringComparison.Ordinal) &&
            string.Equals(left.ParentId, right.ParentId, StringComparison.Ordinal);

        private static string Signature(CorpusEdge edge) => edge.From + "\u001f" + edge.To + "\u001f" + edge.Kind + "\u001f" + edge.Evidence;
    }
}