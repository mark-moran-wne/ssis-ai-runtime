using System;
using System.Collections.Generic;
using System.Linq;
using SsisAiRuntime.Inspectors.Expressions;

namespace SsisAiRuntime.Inspectors
{
    public sealed class ExpressionDependencyGraphBuilder
    {
        public PackageDependencyGraph Enrich(PackageDependencyGraph graph, ExpressionScopeCatalog catalog,
            IEnumerable<ExpressionDependencyAnalysis> analyses, IEnumerable<UnsupportedItem> coverage)
        {
            if (graph == null) { throw new ArgumentNullException(nameof(graph)); }
            if (catalog == null) { throw new ArgumentNullException(nameof(catalog)); }
            if (analyses == null) { throw new ArgumentNullException(nameof(analyses)); }
            if (coverage == null) { throw new ArgumentNullException(nameof(coverage)); }
            var nodes = graph.Nodes.ToDictionary(node => node.Key, StringComparer.Ordinal);
            var edges = new List<DependencyEdge>(graph.Edges);
            var gaps = new List<UnsupportedItem>(graph.UnsupportedItems.Concat(coverage));
            var addedScopes = new List<ExpressionScope>();
            foreach (var scope in catalog.Scopes)
            {
                if (!nodes.Values.Any(node => node.NativeId == scope.Id &&
                    (node.Kind == SemanticObjectKind.Package || node.Kind == SemanticObjectKind.Executable)))
                {
                    var kind = scope.Kind == ExpressionScopeKind.Package ? SemanticObjectKind.Package : SemanticObjectKind.Executable;
                    AddNode(nodes, new DependencyNode(kind + ":" + scope.Id, kind, scope.Name, scope.Id, scope.ParentId));
                    addedScopes.Add(scope);
                }
                foreach (var symbol in scope.Symbols)
                {
                    var kind = SymbolKind(symbol);
                    var key = SymbolKey(symbol);
                    AddNode(nodes, new DependencyNode(key, kind, symbol.QualifiedName, symbol.NativeId, symbol.ScopeId));
                }
            }
            foreach (var scope in addedScopes.Where(scope => scope.ParentId.Length != 0))
            {
                var parents = nodes.Values.Where(node => node.NativeId == scope.ParentId &&
                    (node.Kind == SemanticObjectKind.Package || node.Kind == SemanticObjectKind.Executable)).ToArray();
                if (parents.Length == 1)
                { edges.Add(new DependencyEdge(parents[0].Key, SemanticObjectKind.Executable + ":" + scope.Id, DependencyKind.ContainsTask)); }
            }
            foreach (var analysis in analyses)
            {
                if (!analysis.Parsed)
                { gaps.Add(ExpressionGap(analysis, "expression.parse_failed")); continue; }
                if (analysis.PropertyName == "ConstraintExpression" && catalog.TryGet(analysis.OwnerScopeId, out var constraintScope))
                {
                    var ownerKey = "ExpressionOwner:Constraint:" + analysis.OwnerNativeId;
                    AddNode(nodes, new DependencyNode(ownerKey, SemanticObjectKind.ExpressionOwner, "ConstraintExpression", analysis.OwnerNativeId, constraintScope.Id));
                    var container = nodes.Values.SingleOrDefault(node => node.NativeId == constraintScope.Id &&
                        (node.Kind == SemanticObjectKind.Package || node.Kind == SemanticObjectKind.Executable));
                    if (container != null) { edges.Add(new DependencyEdge(container.Key, ownerKey, DependencyKind.HasExpression)); }
                }
                var owners = nodes.Values.Where(node => node.NativeId == analysis.OwnerNativeId &&
                    node.Kind != SemanticObjectKind.DataFlow &&
                    (node.Kind != SemanticObjectKind.ExpressionOwner || analysis.PropertyName == "ConstraintExpression") &&
                    (node.Kind != SemanticObjectKind.DataFlowComponent || node.ParentId == analysis.OwnerScopeId)).ToArray();
                if (owners.Length != 1)
                { gaps.Add(ExpressionGap(analysis, "expression.target_not_projected")); continue; }
                foreach (var resolution in analysis.Resolutions)
                {
                    if (resolution.Status != ExpressionReferenceResolutionStatus.Resolved)
                    { gaps.Add(ExpressionGap(analysis, ResolutionCode(resolution))); continue; }
                    var symbol = resolution.ResolvedSymbol;
                    if (!nodes.TryGetValue(SymbolKey(symbol), out var target) || target.NativeId != symbol.NativeId)
                    { gaps.Add(ExpressionGap(analysis, "expression.target_not_projected")); continue; }
                    edges.Add(new DependencyEdge(owners[0].Key, target.Key,
                        SymbolKind(symbol) == SemanticObjectKind.Variable ? DependencyKind.UsesVariable : DependencyKind.UsesParameter,
                        ExpressionResolutionEvidence.LexicalAndScopeResolved.ToString()));
                }
            }
            return new PackageDependencyGraph(nodes.Values.OrderBy(node => node.Key, StringComparer.Ordinal),
                edges.GroupBy(edge => edge.From + "\u001f" + edge.To + "\u001f" + edge.Kind + "\u001f" + edge.Evidence, StringComparer.Ordinal).Select(group => group.First()),
                gaps.GroupBy(gap => gap.Id + "\u001f" + gap.Name + "\u001f" + gap.ReasonCode, StringComparer.Ordinal).Select(group => group.First()));
        }

        public static string SymbolKey(ExpressionSymbol symbol) => SymbolKind(symbol) + ":" + symbol.Kind + ":" + Uri.EscapeDataString(symbol.NativeId);
        private static SemanticObjectKind SymbolKind(ExpressionSymbol symbol) =>
            symbol.Kind == ExpressionSymbolKind.Variable || symbol.Kind == ExpressionSymbolKind.SystemVariable ? SemanticObjectKind.Variable : SemanticObjectKind.Parameter;
        private static string ResolutionCode(ExpressionReferenceResolution resolution)
        {
            if (resolution.Status == ExpressionReferenceResolutionStatus.Ambiguous) { return "expression.reference_ambiguous"; }
            if (resolution.Status == ExpressionReferenceResolutionStatus.NotFound) { return "expression.reference_not_found"; }
            if (resolution.Status == ExpressionReferenceResolutionStatus.InvalidOwnerScope) { return "expression.owner_scope_missing"; }
            return resolution.Reference.Kind == Expressions.ExpressionReferenceKind.ProjectParameter
                ? "expression.project_context_unavailable" : "expression.target_not_projected";
        }
        private static UnsupportedItem ExpressionGap(ExpressionDependencyAnalysis analysis, string code) =>
            new UnsupportedItem(analysis.OwnerNativeId, SafePropertyCategory(analysis.PropertyName), "ExpressionDependency",
                "An expression dependency could not be resolved.", code);
        public static string SafePropertyCategory(string propertyName) =>
            propertyName == "VariableExpression" || propertyName == "LoopExpression" || propertyName == "ConstraintExpression" ||
            propertyName == "DataFlowExpression" || propertyName == "ConnectionExpression" ? propertyName : "PropertyExpression";

        public PackageDependencyGraph Enrich(PackageDependencyGraph graph, IEnumerable<VariableOverview> variables,
            IEnumerable<ParameterOverview> parameters, InspectionResult<ExpressionDependencyOverview> dependencies)
        {
            if (graph == null) { throw new ArgumentNullException(nameof(graph)); }
            if (variables == null) { throw new ArgumentNullException(nameof(variables)); }
            if (parameters == null) { throw new ArgumentNullException(nameof(parameters)); }
            if (dependencies == null) { throw new ArgumentNullException(nameof(dependencies)); }

            var nodes = graph.Nodes.ToDictionary(item => item.Key, StringComparer.Ordinal);
            var edges = new List<DependencyEdge>(graph.Edges);
            var unsupported = new List<UnsupportedItem>(graph.UnsupportedItems);
            unsupported.AddRange(dependencies.UnsupportedItems);

            foreach (var variable in variables)
            {
                var key = VariableKey(variable.Namespace, variable.Name);
                AddNode(nodes, new DependencyNode(key, SemanticObjectKind.Variable,
                    variable.Namespace + "::" + variable.Name, key));
            }

            foreach (var parameter in parameters)
            {
                AddNode(nodes, new DependencyNode(ParameterKey(parameter.Id), SemanticObjectKind.Parameter,
                    parameter.Name, parameter.Id));
            }

            foreach (var dependency in dependencies.Items)
            {
                var ownerKey = OwnerKey(dependency.OwnerType, dependency.OwnerId, dependency.PropertyName);
                AddNode(nodes, new DependencyNode(ownerKey, SemanticObjectKind.ExpressionOwner,
                    dependency.OwnerName, dependency.OwnerId));

                var owningObject = ResolveOwner(nodes.Values, dependency);
                if (owningObject == null)
                {
                    unsupported.Add(Gap(dependency.OwnerId, dependency.OwnerName,
                        "The expression owner could not be resolved to one dependency node."));
                }
                else
                {
                    AddEdge(nodes, edges, unsupported,
                        new DependencyEdge(owningObject.Key, ownerKey, DependencyKind.HasExpression),
                        dependency.OwnerId, dependency.OwnerName);
                }

                foreach (var reference in dependency.References)
                {
                    var target = reference.Kind == ExpressionReferenceKind.Parameter
                        ? ResolveParameter(nodes.Values, reference)
                        : ResolveVariable(nodes.Values, reference);
                    if (target == null)
                    {
                        unsupported.Add(Gap(dependency.OwnerId, dependency.OwnerName,
                            "An expression reference could not be resolved to one dependency node."));
                        continue;
                    }

                    AddEdge(nodes, edges, unsupported,
                        new DependencyEdge(ownerKey, target.Key, reference.Kind == ExpressionReferenceKind.Parameter
                            ? DependencyKind.UsesParameter
                            : DependencyKind.UsesVariable), dependency.OwnerId, dependency.OwnerName);
                }
            }

            var distinctEdges = edges
                .GroupBy(item => item.From + "\u001f" + item.To + "\u001f" + item.Kind, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(item => item.From, StringComparer.Ordinal)
                .ThenBy(item => item.To, StringComparer.Ordinal)
                .ThenBy(item => item.Kind);
            var distinctUnsupported = unsupported
                .GroupBy(item => item.Id + "\u001f" + item.Name + "\u001f" + item.CreationName + "\u001f" + item.ReasonCode,
                    StringComparer.Ordinal)
                .Select(group => group.First());
            return new PackageDependencyGraph(nodes.Values.OrderBy(item => item.Key, StringComparer.Ordinal),
                distinctEdges, distinctUnsupported);
        }

        public static string VariableKey(string namespaceName, string name) =>
            "Variable:" + Escape(namespaceName) + "::" + Escape(name);

        public static string ParameterKey(string nativeId) => "Parameter:" + (nativeId ?? string.Empty);

        public static string OwnerKey(string ownerType, string ownerId, string propertyName) =>
            "ExpressionOwner:" + Escape(ownerType) + ":" + Escape(ownerId) + ":" + Escape(propertyName);

        private static string Escape(string value) => Uri.EscapeDataString(value ?? string.Empty);

        private static DependencyNode ResolveVariable(IEnumerable<DependencyNode> nodes, ExpressionReferenceOverview reference)
        {
            if (!string.IsNullOrWhiteSpace(reference.NativeId))
            {
                var byNativeId = nodes.Where(item => item.Kind == SemanticObjectKind.Variable &&
                    string.Equals(item.NativeId, reference.NativeId, StringComparison.Ordinal)).ToArray();
                if (byNativeId.Length == 1) { return byNativeId[0]; }
                if (byNativeId.Length > 1) { return null; }
            }

            var key = VariableKey(reference.NamespaceName, reference.Name);
            var byQualifiedName = nodes.Where(item => item.Kind == SemanticObjectKind.Variable &&
                string.Equals(item.Key, key, StringComparison.Ordinal)).ToArray();
            return byQualifiedName.Length == 1 ? byQualifiedName[0] : null;
        }

        private static DependencyNode ResolveParameter(IEnumerable<DependencyNode> nodes, ExpressionReferenceOverview reference)
        {
            if (!string.IsNullOrWhiteSpace(reference.NativeId))
            {
                var byNativeId = nodes.Where(item => item.Kind == SemanticObjectKind.Parameter &&
                    string.Equals(item.NativeId, reference.NativeId, StringComparison.Ordinal)).ToArray();
                if (byNativeId.Length == 1) { return byNativeId[0]; }
                if (byNativeId.Length > 1) { return null; }
            }

            var byName = nodes.Where(item => item.Kind == SemanticObjectKind.Parameter &&
                string.Equals(item.Name, reference.Name, StringComparison.OrdinalIgnoreCase)).ToArray();
            return byName.Length == 1 ? byName[0] : null;
        }

        private static DependencyNode ResolveOwner(IEnumerable<DependencyNode> nodes, ExpressionDependencyOverview dependency)
        {
            var kinds = OwnerKinds(dependency.OwnerType);
            var matches = nodes.Where(item => kinds.Contains(item.Kind) &&
                string.Equals(item.NativeId, dependency.OwnerId, StringComparison.Ordinal)).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }

        private static HashSet<SemanticObjectKind> OwnerKinds(string ownerType)
        {
            if (ownerType.Equals("Package", StringComparison.OrdinalIgnoreCase))
                return new HashSet<SemanticObjectKind> { SemanticObjectKind.Package };
            if (ownerType.Equals("Connection", StringComparison.OrdinalIgnoreCase))
                return new HashSet<SemanticObjectKind> { SemanticObjectKind.Connection };
            if (ownerType.Equals("Variable", StringComparison.OrdinalIgnoreCase))
                return new HashSet<SemanticObjectKind> { SemanticObjectKind.Variable };
            if (ownerType.Equals("Parameter", StringComparison.OrdinalIgnoreCase))
                return new HashSet<SemanticObjectKind> { SemanticObjectKind.Parameter };
            return new HashSet<SemanticObjectKind>
            {
                SemanticObjectKind.Executable,
                SemanticObjectKind.DataFlow,
                SemanticObjectKind.DataFlowComponent
            };
        }

        private static void AddNode(IDictionary<string, DependencyNode> nodes, DependencyNode node)
        {
            if (!nodes.ContainsKey(node.Key)) { nodes.Add(node.Key, node); }
        }

        private static void AddEdge(IDictionary<string, DependencyNode> nodes, ICollection<DependencyEdge> edges,
            ICollection<UnsupportedItem> unsupported, DependencyEdge edge, string id, string name)
        {
            if (!nodes.ContainsKey(edge.From) || !nodes.ContainsKey(edge.To))
            {
                unsupported.Add(Gap(id, name, "A dependency endpoint could not be resolved."));
                return;
            }
            edges.Add(edge);
        }

        private static UnsupportedItem Gap(string id, string name, string reason) =>
            new UnsupportedItem(id, name, "ExpressionDependency", reason, UnsupportedItem.UnsupportedMetadataCode);
    }
}