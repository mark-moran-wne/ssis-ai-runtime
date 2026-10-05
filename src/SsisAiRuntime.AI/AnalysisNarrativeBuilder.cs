using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace SsisAiRuntime.AI
{
    public sealed class AnalysisNarrative
    {
        public AnalysisNarrative(string summary, string coverage, IEnumerable<string> observations)
        {
            Summary = summary ?? string.Empty;
            Coverage = coverage ?? string.Empty;
            Observations = new ReadOnlyCollection<string>(new List<string>(
                observations ?? throw new ArgumentNullException(nameof(observations))));
        }

        public string Summary { get; }
        public string Coverage { get; }
        public IReadOnlyList<string> Observations { get; }
        public string RedactionPolicy => "metadata-only";
    }

    public sealed class AnalysisNarrativeBuilder
    {
        public AnalysisNarrative Build(RichImpactAnalysisResult result)
        {
            if (result == null) { throw new ArgumentNullException(nameof(result)); }
            var direct = result.Impacts.Count(item => item.Distance == 1);
            var indirect = result.Impacts.Count(item => item.Distance > 1);
            var observations = result.Impacts.GroupBy(item => item.Category).OrderBy(group => group.Key)
                .Select(group => group.Key + ": " + group.Count().ToString(CultureInfo.InvariantCulture)).ToArray();
            var summary = result.Root.Name + " has " + direct.ToString(CultureInfo.InvariantCulture) +
                " direct and " + indirect.ToString(CultureInfo.InvariantCulture) + " indirect projected impacts.";
            var coverage = result.IsComplete
                ? "Coverage is complete for the projected dependency graph used by this analysis."
                : "Coverage is incomplete; unsupported projected relationships remain.";
            return new AnalysisNarrative(summary, coverage, observations);
        }

        public AnalysisNarrative Build(DependencySelectorResolution resolution)
        {
            if (resolution == null) { throw new ArgumentNullException(nameof(resolution)); }
            switch (resolution.Status)
            {
                case DependencySelectorResolutionStatus.Resolved:
                    return new AnalysisNarrative("The selector resolved to one dependency node.",
                        "No dependency traversal was performed.",
                        new[] { resolution.ResolvedNode.Kind + ": " + resolution.ResolvedNode.Name });
                case DependencySelectorResolutionStatus.Ambiguous:
                    return new AnalysisNarrative("The selector is ambiguous.", "No candidate was selected automatically.",
                        resolution.Candidates.Select(item => item.Kind + ": " + item.Name));
                default:
                    return new AnalysisNarrative("No dependency node matched the selector.",
                        "No dependency traversal was performed.", Array.Empty<string>());
            }
        }
    }
}