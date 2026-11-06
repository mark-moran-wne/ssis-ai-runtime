using System;
using System.Collections.Generic;
using System.Linq;
using SsisAiRuntime.Inspectors.Expressions;

namespace SsisAiRuntime.Inspectors
{
    public sealed class PackageParameterBindingGraphBuilder
    {
        private const string BindingEvidence = "DeclaredPackageParameterBinding";

        public PackageDependencyGraph Enrich(PackageDependencyGraph graph,
            IEnumerable<ExecutableOverview> executables, IProjectInventory projectInventory,
            ExpressionScopeCatalog scopeCatalog)
        {
            if (graph == null) { throw new ArgumentNullException(nameof(graph)); }
            if (executables == null) { throw new ArgumentNullException(nameof(executables)); }
            if (projectInventory == null) { throw new ArgumentNullException(nameof(projectInventory)); }

            var nodes = graph.Nodes.ToDictionary(node => node.Key, StringComparer.Ordinal);
            var edges = new List<DependencyEdge>(graph.Edges);
            var unsupported = new List<UnsupportedItem>(graph.UnsupportedItems);
            var packages = projectInventory.Packages ?? throw new ArgumentException("Project packages are required.", nameof(projectInventory));
            var parser = new ExpressionReferenceParser();
            var resolver = new ExpressionScopeResolver();

            foreach (var executable in executables.Where(item => item.PackageParameterBindings.Count != 0))
            {
                var taskKey = SemanticObjectKind.Executable + ":" + executable.Id;
                var resolvedTargets = edges.Where(edge => edge.From == taskKey && edge.Kind == DependencyKind.InvokesPackage &&
                    edge.Evidence == "ProjectInventoryResolved").Select(edge => edge.To).Distinct(StringComparer.Ordinal).ToArray();
                if (resolvedTargets.Length != 1 || !nodes.TryGetValue(resolvedTargets[0], out var childPackageNode))
                {
                    AddGap(unsupported, executable, UnsupportedItem.PackageParameterBindingUnresolvedCode);
                    continue;
                }

                var childPackages = packages.Where(item => item.PackageId == childPackageNode.NativeId &&
                    item.DependencyKey(projectInventory.ProjectIdentity) == childPackageNode.Key).ToArray();
                if (childPackages.Length != 1)
                {
                    AddGap(unsupported, executable, UnsupportedItem.PackageParameterBindingUnresolvedCode);
                    continue;
                }

                foreach (var bindingGroup in executable.PackageParameterBindings
                    .GroupBy(binding => binding.ChildParameterName, StringComparer.Ordinal))
                {
                    if (string.IsNullOrWhiteSpace(bindingGroup.Key) || bindingGroup.Count() != 1)
                    {
                        AddGap(unsupported, executable, UnsupportedItem.PackageParameterBindingAmbiguousCode);
                        continue;
                    }

                    var targetParameters = childPackages[0].PackageParameters.Where(parameter =>
                        string.Equals(parameter.Name, bindingGroup.Key, StringComparison.Ordinal)).ToArray();
                    if (targetParameters.Length != 1)
                    {
                        AddGap(unsupported, executable, targetParameters.Length > 1
                            ? UnsupportedItem.PackageParameterBindingAmbiguousCode
                            : UnsupportedItem.PackageParameterBindingUnresolvedCode);
                        continue;
                    }

                    var sourceResolution = ResolveSource(bindingGroup.Single().ParentSourceReference,
                        executable.Id, parser, resolver, scopeCatalog);
                    if (sourceResolution == null || sourceResolution.Status != ExpressionReferenceResolutionStatus.Resolved)
                    {
                        AddGap(unsupported, executable, sourceResolution?.Status == ExpressionReferenceResolutionStatus.Ambiguous
                            ? UnsupportedItem.PackageParameterBindingAmbiguousCode
                            : UnsupportedItem.PackageParameterBindingUnresolvedCode);
                        continue;
                    }

                    var sourceSymbol = sourceResolution.ResolvedSymbol;
                    var sourceKey = ExpressionDependencyGraphBuilder.SymbolKey(sourceSymbol);
                    if (!nodes.TryGetValue(sourceKey, out var sourceNode) || sourceNode.NativeId != sourceSymbol.NativeId)
                    {
                        AddGap(unsupported, executable, UnsupportedItem.PackageParameterBindingUnresolvedCode);
                        continue;
                    }

                    var targetParameter = targetParameters[0];
                    var childParameterSymbol = new ExpressionSymbol(targetParameter.NativeId, childPackages[0].PackageId,
                        ExpressionSymbolKind.PackageParameter, "$Package", targetParameter.Name);
                    var childParameterKey = PackageParameterKey(projectInventory.ProjectIdentity,
                        childPackages[0].PackageId, targetParameter.NativeId);
                    if (!nodes.TryGetValue(childParameterKey, out var childParameterNode))
                    {
                        childParameterNode = new DependencyNode(childParameterKey, SemanticObjectKind.Parameter,
                            childParameterSymbol.QualifiedName, targetParameter.NativeId, childPackages[0].PackageId);
                        nodes.Add(childParameterKey, childParameterNode);
                    }

                    var bindingKey = BindingKey(projectInventory.ProjectIdentity, executable.Id, targetParameter.NativeId);
                    if (nodes.ContainsKey(bindingKey))
                    {
                        AddGap(unsupported, executable, UnsupportedItem.PackageParameterBindingAmbiguousCode);
                        continue;
                    }
                    nodes.Add(bindingKey, new DependencyNode(bindingKey, SemanticObjectKind.PackageParameterBinding,
                        targetParameter.Name, bindingKey, executable.Id));

                    var sourceKind = sourceSymbol.Kind == ExpressionSymbolKind.Variable ||
                        sourceSymbol.Kind == ExpressionSymbolKind.SystemVariable
                        ? DependencyKind.UsesVariable : DependencyKind.UsesParameter;
                    edges.Add(new DependencyEdge(taskKey, bindingKey,
                        DependencyKind.HasPackageParameterBinding, BindingEvidence));
                    edges.Add(new DependencyEdge(bindingKey, sourceNode.Key, sourceKind,
                        ExpressionResolutionEvidence.LexicalAndScopeResolved.ToString()));
                    edges.Add(new DependencyEdge(bindingKey, childParameterNode.Key,
                        DependencyKind.BindsPackageParameter, BindingEvidence));
                }
            }

            return new PackageDependencyGraph(nodes.Values.OrderBy(node => node.Key, StringComparer.Ordinal),
                edges.GroupBy(edge => edge.From + "\u001f" + edge.To + "\u001f" + edge.Kind + "\u001f" + edge.Evidence,
                    StringComparer.Ordinal).Select(group => group.First()),
                unsupported.GroupBy(item => item.Id + "\u001f" + item.Name + "\u001f" + item.CreationName + "\u001f" + item.ReasonCode,
                    StringComparer.Ordinal).Select(group => group.First()));
        }

        internal static string PackageParameterKey(string projectIdentity, string packageId, string parameterNativeId) =>
            SemanticObjectKind.Parameter + ":PackageParameter:" + Uri.EscapeDataString(projectIdentity) + ":" +
            Uri.EscapeDataString(packageId) + ":" + Uri.EscapeDataString(parameterNativeId);

        private static string BindingKey(string projectIdentity, string taskId, string parameterNativeId) =>
            SemanticObjectKind.PackageParameterBinding + ":" + Uri.EscapeDataString(projectIdentity) + ":" +
            Uri.EscapeDataString(taskId) + ":" + Uri.EscapeDataString(parameterNativeId);

        private static ExpressionReferenceResolution ResolveSource(string sourceReference, string ownerScopeId,
            ExpressionReferenceParser parser, ExpressionScopeResolver resolver, ExpressionScopeCatalog scopeCatalog)
        {
            if (string.IsNullOrWhiteSpace(sourceReference) || scopeCatalog == null) { return null; }
            var expression = sourceReference.StartsWith("@", StringComparison.Ordinal)
                ? sourceReference
                : sourceReference.IndexOf("::", StringComparison.Ordinal) >= 0
                    ? "@[" + sourceReference.Replace("]", "\\]") + "]"
                    : "@" + sourceReference;
            var parsed = parser.Analyze(expression);
            if (!parsed.Succeeded || parsed.References.Count != 1 || parsed.References[0].StartIndex != 0 ||
                parsed.References[0].Length != expression.Length)
            { return null; }
            return resolver.Resolve(ownerScopeId, parsed.References, scopeCatalog).Single();
        }

        private static void AddGap(ICollection<UnsupportedItem> unsupported, ExecutableOverview executable, string code) =>
            unsupported.Add(new UnsupportedItem(executable.Id, executable.Name, "ExecutePackageParameterBinding",
                "A child package parameter binding could not be resolved uniquely.", code));
    }
}