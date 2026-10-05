using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SsisAiRuntime.Inspectors.Expressions
{
    public enum ExpressionScopeKind { Package, Sequence, ForLoop, ForeachLoop, Task, EventHandler, Unknown }
    public enum ExpressionSymbolKind { Variable, SystemVariable, PackageParameter, ProjectParameter }

    public sealed class ExpressionSymbol
    {
        public ExpressionSymbol(string nativeId, string scopeId, ExpressionSymbolKind kind, string namespaceName, string name)
        {
            if (string.IsNullOrWhiteSpace(nativeId)) { throw new ArgumentException("A native symbol ID is required.", nameof(nativeId)); }
            if (string.IsNullOrWhiteSpace(scopeId)) { throw new ArgumentException("A scope ID is required.", nameof(scopeId)); }
            if (string.IsNullOrWhiteSpace(name)) { throw new ArgumentException("A symbol name is required.", nameof(name)); }
            NativeId = nativeId; ScopeId = scopeId; Kind = kind; NamespaceName = namespaceName ?? string.Empty; Name = name;
        }
        public string NativeId { get; }
        public string ScopeId { get; }
        public ExpressionSymbolKind Kind { get; }
        public string NamespaceName { get; }
        public string Name { get; }
        public string QualifiedName => NamespaceName.Length == 0 ? Name : NamespaceName + "::" + Name;
    }

    public sealed class ExpressionScope
    {
        public ExpressionScope(string id, string parentId, string name, ExpressionScopeKind kind, IEnumerable<ExpressionSymbol> symbols)
        {
            if (string.IsNullOrWhiteSpace(id)) { throw new ArgumentException("A scope ID is required.", nameof(id)); }
            Id = id; ParentId = parentId ?? string.Empty; Name = name ?? string.Empty; Kind = kind;
            Symbols = new ReadOnlyCollection<ExpressionSymbol>((symbols ?? throw new ArgumentNullException(nameof(symbols))).ToList());
        }
        public string Id { get; }
        public string ParentId { get; }
        public string Name { get; }
        public ExpressionScopeKind Kind { get; }
        public IReadOnlyList<ExpressionSymbol> Symbols { get; }
    }

    public sealed class ExpressionScopeCatalogException : ArgumentException
    {
        public ExpressionScopeCatalogException(string reasonCode) : base("The expression scope catalog is invalid.") { ReasonCode = reasonCode; }
        public string ReasonCode { get; }
    }

    public sealed class ExpressionScopeCatalog
    {
        private readonly IReadOnlyDictionary<string, ExpressionScope> scopes;
        public ExpressionScopeCatalog(IEnumerable<ExpressionScope> scopes, bool projectContextAvailable = false)
        {
            var materialized = (scopes ?? throw new ArgumentNullException(nameof(scopes))).ToArray();
            if (materialized.Any(scope => scope == null) || materialized.GroupBy(scope => scope.Id, StringComparer.Ordinal).Any(group => group.Count() != 1))
            { throw new ExpressionScopeCatalogException("expression.owner_scope_missing"); }
            var byId = materialized.ToDictionary(scope => scope.Id, StringComparer.Ordinal);
            var packages = materialized.Where(scope => scope.Kind == ExpressionScopeKind.Package).ToArray();
            if (packages.Length != 1 || packages[0].ParentId.Length != 0 || materialized.Any(scope =>
                scope.Kind != ExpressionScopeKind.Package && (scope.ParentId.Length == 0 || !byId.ContainsKey(scope.ParentId))))
            { throw new ExpressionScopeCatalogException("expression.owner_scope_missing"); }
            foreach (var scope in materialized)
            {
                var visited = new HashSet<string>(StringComparer.Ordinal);
                var current = scope;
                while (current != null)
                {
                    if (!visited.Add(current.Id)) { throw new ExpressionScopeCatalogException("expression.scope_cycle"); }
                    current = current.ParentId.Length == 0 ? null : byId[current.ParentId];
                }
                if (scope.Symbols.Any(symbol => symbol == null || symbol.ScopeId != scope.Id ||
                    (symbol.Kind == ExpressionSymbolKind.PackageParameter && scope.Kind != ExpressionScopeKind.Package)))
                { throw new ExpressionScopeCatalogException("expression.target_not_projected"); }
            }
            var symbols = materialized.SelectMany(scope => scope.Symbols).ToArray();
            if (symbols.GroupBy(symbol => (symbol.Kind == ExpressionSymbolKind.Variable || symbol.Kind == ExpressionSymbolKind.SystemVariable ? "Variable:" : "Parameter:") + symbol.NativeId,
                StringComparer.Ordinal).Any(group => group.Count() != 1) ||
                (!projectContextAvailable && symbols.Any(symbol => symbol.Kind == ExpressionSymbolKind.ProjectParameter)))
            { throw new ExpressionScopeCatalogException("expression.target_not_projected"); }
            this.scopes = new ReadOnlyDictionary<string, ExpressionScope>(byId);
            Scopes = new ReadOnlyCollection<ExpressionScope>(materialized);
            ProjectContextAvailable = projectContextAvailable;
        }
        public IReadOnlyCollection<ExpressionScope> Scopes { get; }
        public bool ProjectContextAvailable { get; }
        public bool TryGet(string scopeId, out ExpressionScope scope) => scopes.TryGetValue(scopeId ?? string.Empty, out scope);
    }
}