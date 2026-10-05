using System;
using System.Collections.Generic;
using System.Linq;

namespace SsisAiRuntime.Corpus
{
    public sealed class CorpusComparer
    {
        public CorpusDiff Compare(CorpusSnapshot baseline, CorpusSnapshot candidate)
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
            return new CorpusDiff(addedNodes, removedNodes, addedEdges, removedEdges, changedNodes,
                packageChanged, addedGaps, removedGaps);
        }

        private static bool Equivalent(CorpusNode left, CorpusNode right) => left.Kind == right.Kind &&
            string.Equals(left.Name, right.Name, StringComparison.Ordinal) &&
            string.Equals(left.NativeId, right.NativeId, StringComparison.Ordinal) &&
            string.Equals(left.ParentId, right.ParentId, StringComparison.Ordinal);

        private static string Signature(CorpusEdge edge) => edge.From + "\u001f" + edge.To + "\u001f" + edge.Kind + "\u001f" + edge.Evidence;
    }
}