using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SsisAiRuntime.Inspectors
{
    public sealed class SemanticHandleCatalog
    {
        private readonly Dictionary<string, SemanticObjectReference> _byHandle;
        private readonly Dictionary<string, ReadOnlyCollection<SemanticObjectReference>> _byNativeId;

        public SemanticHandleCatalog(Guid sessionId, IEnumerable<SemanticObjectReference> entries)
            : this(sessionId, entries, null)
        {
        }

        internal SemanticHandleCatalog(
            Guid sessionId,
            IEnumerable<SemanticObjectReference> entries,
            IDictionary<string, List<SemanticHandle>> nativeHandles)
        {
            if (entries == null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            SessionId = sessionId;
            var entryList = new List<SemanticObjectReference>();
            _byHandle = new Dictionary<string, SemanticObjectReference>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                if (entry == null)
                {
                    throw new ArgumentException("Handle catalogs cannot contain null entries.", nameof(entries));
                }

                if (entry.Handle.SessionId != sessionId)
                {
                    throw new ArgumentException("All handles must belong to the catalog session.", nameof(entries));
                }

                if (_byHandle.ContainsKey(entry.Handle.Value))
                {
                    throw new ArgumentException("Semantic handle values must be unique within a session.", nameof(entries));
                }

                _byHandle.Add(entry.Handle.Value, entry);
                entryList.Add(entry);
            }

            Entries = new ReadOnlyCollection<SemanticObjectReference>(entryList);
            _byNativeId = new Dictionary<string, ReadOnlyCollection<SemanticObjectReference>>(StringComparer.Ordinal);
            if (nativeHandles != null)
            {
                foreach (var mapping in nativeHandles)
                {
                    var references = new List<SemanticObjectReference>();
                    foreach (var handle in mapping.Value)
                    {
                        if (handle.SessionId == sessionId && _byHandle.TryGetValue(handle.Value, out var reference))
                        {
                            references.Add(reference);
                        }
                    }

                    _byNativeId.Add(mapping.Key, new ReadOnlyCollection<SemanticObjectReference>(references));
                }
            }
        }

        public Guid SessionId { get; }

        public IReadOnlyList<SemanticObjectReference> Entries { get; }

        public SemanticHandleResolution Resolve(SemanticHandle handle)
        {
            if (handle == null)
            {
                throw new ArgumentNullException(nameof(handle));
            }

            if (handle.SessionId != SessionId)
            {
                return NotFound();
            }

            return Resolve(handle.Value);
        }

        public SemanticHandleResolution Resolve(string handleValue)
        {
            if (string.IsNullOrWhiteSpace(handleValue) || !_byHandle.TryGetValue(handleValue, out var entry))
            {
                return NotFound();
            }

            return Resolved(entry);
        }

        public SemanticHandleResolution FindByName(string name, SemanticObjectKind? kind = null)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return NotFound();
            }

            var candidates = Entries
                .Where(entry => (!kind.HasValue || entry.Handle.Kind == kind.Value)
                    && string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (candidates.Length == 0)
            {
                return NotFound();
            }

            if (candidates.Length == 1)
            {
                return Resolved(candidates[0]);
            }

            return new SemanticHandleResolution(SemanticHandleResolutionStatus.Ambiguous, null, candidates);
        }

        public IReadOnlyList<SemanticObjectReference> Search(string query, SemanticObjectKind? kind = null)
        {
            if (string.IsNullOrWhiteSpace(query) || query.Length > 256)
            {
                throw new ArgumentException("A metadata query of 1 to 256 characters is required.", nameof(query));
            }
            var nativeMatches = new HashSet<string>(_byNativeId.Where(mapping =>
                    mapping.Key.Substring(mapping.Key.IndexOf(':') + 1).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                .SelectMany(mapping => mapping.Value).Select(entry => entry.Handle.Value), StringComparer.Ordinal);
            return new ReadOnlyCollection<SemanticObjectReference>(Entries.Where(entry =>
                (!kind.HasValue || entry.Handle.Kind == kind.Value) &&
                (entry.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                 entry.CreationName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                 entry.Handle.Value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 || nativeMatches.Contains(entry.Handle.Value)))
                .OrderBy(entry => entry.Handle.Value, StringComparer.Ordinal).ToList());
        }

        public IReadOnlyList<string> GetNativeIds(SemanticHandle handle)
        {
            if (handle == null) { throw new ArgumentNullException(nameof(handle)); }
            return new ReadOnlyCollection<string>(_byNativeId.Where(mapping => mapping.Value.Any(entry => entry.Handle.Equals(handle)))
                .Select(mapping => mapping.Key.Substring(mapping.Key.IndexOf(':') + 1))
                .Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToList());
        }

        internal SemanticHandleResolution ResolveNativeId(SemanticObjectKind kind, string nativeId)
        {
            if (string.IsNullOrWhiteSpace(nativeId)
                || !_byNativeId.TryGetValue(GetNativeIdKey(kind, nativeId), out var references)
                || references.Count == 0)
            {
                return NotFound();
            }

            if (references.Count == 1)
            {
                return Resolved(references[0]);
            }

            return new SemanticHandleResolution(SemanticHandleResolutionStatus.Ambiguous, null, references);
        }

        internal static string GetNativeIdKey(SemanticObjectKind kind, string nativeId)
        {
            return ((int)kind).ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + nativeId;
        }

        private static SemanticHandleResolution NotFound()
        {
            return new SemanticHandleResolution(
                SemanticHandleResolutionStatus.NotFound,
                null,
                Array.Empty<SemanticObjectReference>());
        }

        private static SemanticHandleResolution Resolved(SemanticObjectReference entry)
        {
            return new SemanticHandleResolution(
                SemanticHandleResolutionStatus.Resolved,
                entry,
                new[] { entry });
        }
    }
}