using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using SsisAiRuntime.Inspectors;

namespace SsisAiRuntime.AI
{
    public enum DependencySelectorResolutionStatus
    {
        Resolved,
        NotFound,
        Ambiguous
    }

    public sealed class DependencySelectorResolution
    {
        public DependencySelectorResolution(DependencySelectorResolutionStatus status, DependencyNode resolvedNode,
            IEnumerable<DependencyNode> candidates, int candidateCount)
        {
            Status = status;
            ResolvedNode = resolvedNode;
            Candidates = new ReadOnlyCollection<DependencyNode>(new List<DependencyNode>(
                candidates ?? throw new ArgumentNullException(nameof(candidates))));
            CandidateCount = candidateCount;
        }

        public DependencySelectorResolutionStatus Status { get; }
        public DependencyNode ResolvedNode { get; }
        public IReadOnlyList<DependencyNode> Candidates { get; }
        public int CandidateCount { get; }
        public int CandidatesOmitted => Math.Max(0, CandidateCount - Candidates.Count);
    }

    public sealed class DependencySelectorResolver
    {
        private const int CandidateLimit = 50;

        public DependencySelectorResolution Resolve(PackageDependencyGraph graph, string selector,
            SemanticObjectKind? kind = null)
        {
            if (graph == null) { throw new ArgumentNullException(nameof(graph)); }
            if (string.IsNullOrWhiteSpace(selector)) { return NotFound(); }

            var exactKey = graph.Nodes.Where(node => (!kind.HasValue || node.Kind == kind.Value) &&
                string.Equals(node.Key, selector, StringComparison.Ordinal)).ToArray();
            if (exactKey.Length == 1) { return Resolved(exactKey[0]); }
            if (exactKey.Length > 1) { return Ambiguous(exactKey); }

            var exactNativeId = graph.Nodes.Where(node => (!kind.HasValue || node.Kind == kind.Value) &&
                string.Equals(node.NativeId, selector, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (exactNativeId.Length == 1) { return Resolved(exactNativeId[0]); }
            if (exactNativeId.Length > 1) { return Ambiguous(exactNativeId); }

            var exactName = graph.Nodes.Where(node => (!kind.HasValue || node.Kind == kind.Value) &&
                string.Equals(node.Name, selector, StringComparison.OrdinalIgnoreCase))
                .OrderBy(node => node.Key, StringComparer.Ordinal).ToArray();
            if (exactName.Length == 1) { return Resolved(exactName[0]); }
            if (exactName.Length > 1) { return Ambiguous(exactName); }

            var partialName = graph.Nodes.Where(node => (!kind.HasValue || node.Kind == kind.Value) &&
                node.Name.IndexOf(selector, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(node => node.Key, StringComparer.Ordinal).ToArray();
            if (partialName.Length == 1) { return Resolved(partialName[0]); }
            if (partialName.Length > 1) { return Ambiguous(partialName); }
            return NotFound();
        }

        private static DependencySelectorResolution Resolved(DependencyNode node) =>
            new DependencySelectorResolution(DependencySelectorResolutionStatus.Resolved, node, new[] { node }, 1);

        private static DependencySelectorResolution Ambiguous(IEnumerable<DependencyNode> nodes)
        {
            var matches = nodes.ToArray();
            return new DependencySelectorResolution(DependencySelectorResolutionStatus.Ambiguous, null,
                matches.Take(CandidateLimit), matches.Length);
        }

        private static DependencySelectorResolution NotFound() =>
            new DependencySelectorResolution(DependencySelectorResolutionStatus.NotFound, null, Array.Empty<DependencyNode>(), 0);
    }
}