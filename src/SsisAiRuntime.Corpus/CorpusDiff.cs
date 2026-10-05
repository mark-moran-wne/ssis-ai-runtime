using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SsisAiRuntime.Corpus
{
    public sealed class CorpusDiff
    {
        public CorpusDiff(IEnumerable<string> addedNodes, IEnumerable<string> removedNodes,
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
            Differences = BuildDifferences();
        }

        public IReadOnlyList<string> AddedNodes { get; }
        public IReadOnlyList<string> RemovedNodes { get; }
        public IReadOnlyList<string> AddedEdges { get; }
        public IReadOnlyList<string> RemovedEdges { get; }
        public IReadOnlyList<string> ChangedNodes { get; }
        public bool PackageChanged { get; }
        public IReadOnlyList<CorpusCoverageGap> AddedCoverageGaps { get; }
        public IReadOnlyList<CorpusCoverageGap> RemovedCoverageGaps { get; }
        public IReadOnlyList<string> Differences { get; }
        public bool Changed => PackageChanged || AddedNodes.Count != 0 || RemovedNodes.Count != 0 ||
            ChangedNodes.Count != 0 || AddedEdges.Count != 0 || RemovedEdges.Count != 0 ||
            AddedCoverageGaps.Count != 0 || RemovedCoverageGaps.Count != 0;

        private IReadOnlyList<string> BuildDifferences()
        {
            var differences = new List<string>();
            if (PackageChanged) { differences.Add("PackageIdentity"); }
            differences.AddRange(AddedNodes.Select(item => "NodeAdded:" + item));
            differences.AddRange(RemovedNodes.Select(item => "NodeRemoved:" + item));
            differences.AddRange(ChangedNodes.Select(item => "NodeChanged:" + item));
            differences.AddRange(AddedEdges.Select(item => "EdgeAdded:" + item));
            differences.AddRange(RemovedEdges.Select(item => "EdgeRemoved:" + item));
            differences.AddRange(AddedCoverageGaps.Select(item => "CoverageAdded:" + item.ReasonCode + ":" + item.Count));
            differences.AddRange(RemovedCoverageGaps.Select(item => "CoverageRemoved:" + item.ReasonCode + ":" + item.Count));
            return new ReadOnlyCollection<string>(differences);
        }

        private static IReadOnlyList<T> ReadOnly<T>(IEnumerable<T> values, string name)
        {
            if (values == null) { throw new ArgumentNullException(name); }
            return new ReadOnlyCollection<T>(new List<T>(values));
        }
    }
}