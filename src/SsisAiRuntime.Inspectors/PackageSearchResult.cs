using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SsisAiRuntime.Inspectors
{
    public sealed class PackageSearchResult
    {
        public PackageSearchResult(PackageOverview package, SemanticHandleCatalog catalog, string query, SemanticObjectKind? kind = null)
        {
            Package = package ?? throw new ArgumentNullException(nameof(package));
            if (catalog == null) { throw new ArgumentNullException(nameof(catalog)); }
            if (catalog.SessionId != package.SessionId) { throw new ArgumentException("The catalog must belong to the package session.", nameof(catalog)); }
            var matches = catalog.Search(query, kind);
            TotalMatches = matches.Count;
            Matches = new ReadOnlyCollection<PackageSearchMatch>(matches.Take(50).Select(item =>
                new PackageSearchMatch(item, catalog.GetNativeIds(item.Handle))).ToList());
        }

        public PackageOverview Package { get; }
        public int TotalMatches { get; }
        public int MatchesOmitted => TotalMatches - Matches.Count;
        public IReadOnlyList<PackageSearchMatch> Matches { get; }
    }

    public sealed class PackageSearchMatch
    {
        public PackageSearchMatch(SemanticObjectReference reference, IEnumerable<string> nativeIds)
        {
            Reference = reference;
            NativeIds = new ReadOnlyCollection<string>(new List<string>(nativeIds));
        }

        public SemanticObjectReference Reference { get; }
        public IReadOnlyList<string> NativeIds { get; }
    }
}