using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SsisAiRuntime.Inspectors.Expressions
{
    public enum ExpressionReferenceResolutionStatus { Resolved, Ambiguous, NotFound, Unsupported, InvalidOwnerScope }
    public enum ExpressionResolutionEvidence { LexicalAndScopeResolved }

    public sealed class ExpressionReferenceResolution
    {
        public ExpressionReferenceResolution(ExpressionReference reference, ExpressionReferenceResolutionStatus status,
            IEnumerable<ExpressionSymbol> candidates, ExpressionResolutionEvidence evidence = ExpressionResolutionEvidence.LexicalAndScopeResolved)
        {
            Reference = reference ?? throw new ArgumentNullException(nameof(reference));
            Candidates = new ReadOnlyCollection<ExpressionSymbol>((candidates ?? throw new ArgumentNullException(nameof(candidates))).ToList());
            if ((status == ExpressionReferenceResolutionStatus.Resolved && Candidates.Count != 1) ||
                (status == ExpressionReferenceResolutionStatus.Ambiguous && Candidates.Count < 2) ||
                (status != ExpressionReferenceResolutionStatus.Resolved && status != ExpressionReferenceResolutionStatus.Ambiguous && Candidates.Count != 0))
            { throw new ArgumentException("Candidate count does not match resolution status.", nameof(candidates)); }
            Status = status; Evidence = evidence;
        }
        public ExpressionReference Reference { get; }
        public ExpressionReferenceResolutionStatus Status { get; }
        public IReadOnlyList<ExpressionSymbol> Candidates { get; }
        public ExpressionResolutionEvidence Evidence { get; }
        public ExpressionSymbol ResolvedSymbol => Status == ExpressionReferenceResolutionStatus.Resolved ? Candidates[0] : null;
    }

    public interface IExpressionScopeResolver
    {
        IReadOnlyList<ExpressionReferenceResolution> Resolve(string ownerScopeId, IReadOnlyList<ExpressionReference> references, ExpressionScopeCatalog catalog);
    }

    public sealed class ExpressionScopeResolver : IExpressionScopeResolver
    {
        public IReadOnlyList<ExpressionReferenceResolution> Resolve(string ownerScopeId, IReadOnlyList<ExpressionReference> references, ExpressionScopeCatalog catalog)
        {
            if (references == null) { throw new ArgumentNullException(nameof(references)); }
            if (catalog == null) { throw new ArgumentNullException(nameof(catalog)); }
            return references.Select(reference => catalog.TryGet(ownerScopeId, out var owner)
                ? ResolveReference(reference, owner, catalog)
                : Create(reference, ExpressionReferenceResolutionStatus.InvalidOwnerScope)).ToArray();
        }

        private static ExpressionReferenceResolution ResolveReference(ExpressionReference reference, ExpressionScope owner, ExpressionScopeCatalog catalog)
        {
            if (reference.Kind == ExpressionReferenceKind.PackageParameter)
            { return Match(reference, catalog.Scopes.Where(scope => scope.Kind == ExpressionScopeKind.Package).SelectMany(scope => scope.Symbols), ExpressionSymbolKind.PackageParameter, false); }
            if (reference.Kind == ExpressionReferenceKind.ProjectParameter)
            {
                return catalog.ProjectContextAvailable
                    ? Match(reference, catalog.Scopes.SelectMany(scope => scope.Symbols), ExpressionSymbolKind.ProjectParameter, false)
                    : Create(reference, ExpressionReferenceResolutionStatus.Unsupported);
            }
            if (reference.Kind != ExpressionReferenceKind.Variable && reference.Kind != ExpressionReferenceKind.SystemVariable)
            { return Create(reference, ExpressionReferenceResolutionStatus.Unsupported); }
            var expected = reference.Kind == ExpressionReferenceKind.SystemVariable ? ExpressionSymbolKind.SystemVariable : ExpressionSymbolKind.Variable;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var current = owner;
            while (current != null)
            {
                if (!visited.Add(current.Id)) { return Create(reference, ExpressionReferenceResolutionStatus.Unsupported); }
                var result = Match(reference, current.Symbols, expected, true);
                if (result.Status != ExpressionReferenceResolutionStatus.NotFound) { return result; }
                if (current.ParentId.Length == 0) { break; }
                if (!catalog.TryGet(current.ParentId, out current)) { return Create(reference, ExpressionReferenceResolutionStatus.Unsupported); }
            }
            return Create(reference, ExpressionReferenceResolutionStatus.NotFound);
        }

        private static ExpressionReferenceResolution Match(ExpressionReference reference, IEnumerable<ExpressionSymbol> symbols, ExpressionSymbolKind kind, bool namespaceRequired)
        {
            var candidates = symbols.Where(symbol => symbol.Kind == kind && string.Equals(symbol.Name, reference.Name, StringComparison.OrdinalIgnoreCase) &&
                (!namespaceRequired || reference.NamespaceName.Length == 0 || string.Equals(symbol.NamespaceName, reference.NamespaceName, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(symbol => symbol.NativeId, StringComparer.Ordinal).ToArray();
            return new ExpressionReferenceResolution(reference, candidates.Length == 1 ? ExpressionReferenceResolutionStatus.Resolved :
                candidates.Length == 0 ? ExpressionReferenceResolutionStatus.NotFound : ExpressionReferenceResolutionStatus.Ambiguous, candidates);
        }
        private static ExpressionReferenceResolution Create(ExpressionReference reference, ExpressionReferenceResolutionStatus status) =>
            new ExpressionReferenceResolution(reference, status, Array.Empty<ExpressionSymbol>());
    }
}