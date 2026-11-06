using System;
using System.Collections.Generic;
using System.Linq;

namespace SsisAiRuntime.Inspectors
{
    public sealed class ProjectPackageGraph
    {
        public ProjectPackageGraph(string packageId, PackageDependencyGraph graph)
        {
            if (string.IsNullOrWhiteSpace(packageId)) { throw new ArgumentException("A package identity is required.", nameof(packageId)); }
            PackageId = packageId;
            Graph = graph ?? throw new ArgumentNullException(nameof(graph));
        }

        public string PackageId { get; }
        public PackageDependencyGraph Graph { get; }
    }

    public sealed class ProjectDependencyGraphBuilder
    {
        public const int MaximumPackageGraphs = 128;
        public const int MaximumNodes = 20000;
        public const int MaximumEdges = 50000;

        public PackageDependencyGraph Build(IProjectInventory inventory, IEnumerable<ProjectPackageGraph> packageGraphs)
        {
            if (inventory == null) { throw new ArgumentNullException(nameof(inventory)); }
            if (packageGraphs == null) { throw new ArgumentNullException(nameof(packageGraphs)); }
            var packages = inventory.Packages?.ToArray() ?? throw new ArgumentException("Project packages are required.", nameof(inventory));
            if (string.IsNullOrWhiteSpace(inventory.ProjectIdentity) || string.IsNullOrWhiteSpace(inventory.DeploymentModel) ||
                packages.GroupBy(package => package.PackageId, StringComparer.Ordinal).Any(group => group.Count() != 1) ||
                inventory.Parameters == null || inventory.Parameters.GroupBy(parameter => parameter.NativeId,
                    StringComparer.Ordinal).Any(group => group.Count() != 1) ||
                packages.Any(package => package.PackageParameters.GroupBy(parameter => parameter.NativeId,
                    StringComparer.Ordinal).Any(group => group.Count() != 1)))
            { throw new ArgumentException("Project inventory package identities must be complete and unique.", nameof(inventory)); }

            var packageGraphsToMerge = packageGraphs.Take(MaximumPackageGraphs + 1).ToArray();
            if (packageGraphsToMerge.Length == 0 || packageGraphsToMerge.Length > MaximumPackageGraphs)
            { throw new ArgumentOutOfRangeException(nameof(packageGraphs), "The explicit project graph package count is outside its supported bound."); }
            if (packageGraphsToMerge.GroupBy(item => item.PackageId, StringComparer.Ordinal).Any(group => group.Count() != 1))
            { throw new ArgumentException("A package graph can be supplied only once.", nameof(packageGraphs)); }

            var inventoryById = packages.ToDictionary(package => package.PackageId, StringComparer.Ordinal);
            var nodes = new Dictionary<string, DependencyNode>(StringComparer.Ordinal);
            var edges = new List<DependencyEdge>();
            var unsupported = new List<UnsupportedItem>();
            var includedPackages = new HashSet<string>(StringComparer.Ordinal);

            foreach (var package in packages.OrderBy(item => item.PackageId, StringComparer.Ordinal))
            {
                AddInventoryNode(nodes, new DependencyNode(package.DependencyKey(inventory.ProjectIdentity),
                    SemanticObjectKind.Package, package.ProjectRelativeName, package.PackageId, inventory.ProjectIdentity), packageGraphs);
                foreach (var parameter in package.PackageParameters.OrderBy(item => item.NativeId, StringComparer.Ordinal))
                {
                    var parameterKey = PackageParameterBindingGraphBuilder.PackageParameterKey(inventory.ProjectIdentity,
                        package.PackageId, parameter.NativeId);
                    AddInventoryNode(nodes, new DependencyNode(parameterKey, SemanticObjectKind.Parameter,
                        "$Package::" + parameter.Name, parameter.NativeId, package.DependencyKey(inventory.ProjectIdentity)), packageGraphs);
                }
            }
            foreach (var parameter in inventory.Parameters.OrderBy(item => item.NativeId, StringComparer.Ordinal))
            {
                var parameterKey = "Parameter:ProjectParameter:" + Uri.EscapeDataString(inventory.ProjectIdentity) + ":" +
                    Uri.EscapeDataString(parameter.NativeId);
                AddInventoryNode(nodes, new DependencyNode(parameterKey, SemanticObjectKind.Parameter,
                    "$Project::" + parameter.Name, parameter.NativeId, inventory.ProjectIdentity), packageGraphs);
            }

            foreach (var packageGraph in packageGraphsToMerge.OrderBy(item => item.PackageId, StringComparer.Ordinal))
            {
                if (!inventoryById.TryGetValue(packageGraph.PackageId, out var packageInfo))
                { throw new ArgumentException("A supplied package graph is not present in the project inventory.", nameof(packageGraphs)); }
                var rootKey = packageInfo.DependencyKey(inventory.ProjectIdentity);
                if (!packageGraph.Graph.Nodes.Any(node => node.Key == rootKey && node.Kind == SemanticObjectKind.Package &&
                    node.NativeId == packageGraph.PackageId && node.ParentId == inventory.ProjectIdentity))
                { throw new ArgumentException("A supplied package graph does not match its project package identity.", nameof(packageGraphs)); }

                var nodeKeys = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var node in packageGraph.Graph.Nodes.OrderBy(item => item.Key, StringComparer.Ordinal))
                {
                    var remapped = RemapNode(node, inventory, packageGraph.PackageId, inventoryById);
                    nodeKeys.Add(node.Key, remapped.Key);
                    if (nodes.TryGetValue(remapped.Key, out var existing))
                    {
                        if (!SameNode(existing, remapped))
                        { throw new InvalidOperationException("project.graph.identity_conflict"); }
                    }
                    else
                    {
                        nodes.Add(remapped.Key, remapped);
                    }
                    if (nodes.Count > MaximumNodes)
                    { throw new ArgumentException("The explicit project graph exceeds its node bound.", nameof(packageGraphs)); }
                }

                foreach (var edge in packageGraph.Graph.Edges)
                {
                    if (!nodeKeys.TryGetValue(edge.From, out var from) || !nodeKeys.TryGetValue(edge.To, out var to))
                    { throw new InvalidOperationException("project.graph.edge_endpoint_missing"); }
                    edges.Add(new DependencyEdge(from, to, edge.Kind, edge.Evidence));
                    if (edges.Count > MaximumEdges)
                    { throw new ArgumentException("The explicit project graph exceeds its edge bound.", nameof(packageGraphs)); }
                }

                foreach (var item in packageGraph.Graph.UnsupportedItems)
                {
                    var id = item.Id.Length == 0 ? string.Empty : packageGraph.PackageId + ":" + item.Id;
                    unsupported.Add(new UnsupportedItem(id, item.Name, item.CreationName, item.Reason, item.ReasonCode));
                }
                includedPackages.Add(packageGraph.PackageId);
            }

            foreach (var package in packages.Where(package => !includedPackages.Contains(package.PackageId)))
            {
                unsupported.Add(new UnsupportedItem(package.PackageId, package.ProjectRelativeName, "ProjectPackage",
                    "Package contents were not included in the explicit project graph.",
                    UnsupportedItem.ProjectPackageContentsNotLoadedCode));
            }

            var distinctEdges = edges.GroupBy(edge => edge.From + "\u001f" + edge.To + "\u001f" + edge.Kind + "\u001f" + edge.Evidence,
                StringComparer.Ordinal).Select(group => group.First())
                .OrderBy(edge => edge.From, StringComparer.Ordinal).ThenBy(edge => edge.To, StringComparer.Ordinal)
                .ThenBy(edge => edge.Kind).ThenBy(edge => edge.Evidence, StringComparer.Ordinal);
            var distinctUnsupported = unsupported.GroupBy(item => item.Id + "\u001f" + item.Name + "\u001f" +
                item.CreationName + "\u001f" + item.ReasonCode, StringComparer.Ordinal).Select(group => group.First())
                .OrderBy(item => item.Id, StringComparer.Ordinal).ThenBy(item => item.Name, StringComparer.Ordinal)
                .ThenBy(item => item.ReasonCode, StringComparer.Ordinal);
            return new PackageDependencyGraph(nodes.Values.OrderBy(node => node.Key, StringComparer.Ordinal),
                distinctEdges, distinctUnsupported);
        }

        private static DependencyNode RemapNode(DependencyNode node, IProjectInventory inventory,
            string sourcePackageId, IReadOnlyDictionary<string, ProjectPackageInfo> packages)
        {
            string key;
            string parentId;
            if (node.Kind == SemanticObjectKind.Package && packages.TryGetValue(node.NativeId, out var package))
            {
                key = package.DependencyKey(inventory.ProjectIdentity);
                parentId = inventory.ProjectIdentity;
            }
            else if (node.Kind == SemanticObjectKind.Parameter &&
                node.Key.StartsWith("Parameter:ProjectParameter:", StringComparison.Ordinal))
            {
                key = "Parameter:ProjectParameter:" + Uri.EscapeDataString(inventory.ProjectIdentity) + ":" +
                    Uri.EscapeDataString(node.NativeId);
                parentId = inventory.ProjectIdentity;
            }
            else if (node.Kind == SemanticObjectKind.Parameter &&
                node.Key.StartsWith("Parameter:PackageParameter:", StringComparison.Ordinal) &&
                packages.TryGetValue(node.ParentId, out var packageScope))
            {
                key = PackageParameterBindingGraphBuilder.PackageParameterKey(inventory.ProjectIdentity,
                    node.ParentId, node.NativeId);
                parentId = packageScope.DependencyKey(inventory.ProjectIdentity);
            }
            else
            {
                key = ProjectObjectKey(inventory.ProjectIdentity, sourcePackageId, node.Key);
                parentId = node.ParentId.Length == 0 ? string.Empty : ProjectObjectKey(inventory.ProjectIdentity,
                    sourcePackageId, node.ParentId);
            }

            return new DependencyNode(key, node.Kind, node.Name, node.NativeId, parentId);
        }

        private static string ProjectObjectKey(string projectIdentity, string packageId, string localId) =>
            "ProjectObject:" + Uri.EscapeDataString(projectIdentity) + ":" + Uri.EscapeDataString(packageId) + ":" + localId;

        private static void AddInventoryNode(IDictionary<string, DependencyNode> nodes, DependencyNode node,
            IEnumerable<ProjectPackageGraph> packageGraphs)
        {
            nodes.Add(node.Key, node);
            if (nodes.Count > MaximumNodes)
            { throw new ArgumentException("The explicit project graph exceeds its node bound.", nameof(packageGraphs)); }
        }

        private static bool SameNode(DependencyNode left, DependencyNode right) => left.Kind == right.Kind &&
            left.Name == right.Name && left.NativeId == right.NativeId && left.ParentId == right.ParentId;
    }
}