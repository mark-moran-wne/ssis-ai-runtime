using SsisAiRuntime.AI;
using SsisAiRuntime.Inspectors;

namespace SsisAiRuntime.Tests;

public sealed class ProjectDependencyGraphTests
{
    [Fact]
    public void ProjectGraphInputIsBounded()
    {
        var packageIds = Enumerable.Range(0, ProjectDependencyGraphBuilder.MaximumPackageGraphs + 1)
            .Select(index => "package-" + index).ToArray();
        var inventory = Inventory(packageIds);
        var packageGraphs = inventory.Packages.Select(package => new ProjectPackageGraph(package.PackageId,
            new PackageDependencyGraph(new[]
            {
                new DependencyNode(package.DependencyKey(inventory.ProjectIdentity), SemanticObjectKind.Package,
                    package.ProjectRelativeName, package.PackageId, inventory.ProjectIdentity)
            }, Array.Empty<DependencyEdge>(), Array.Empty<UnsupportedItem>())));

        Assert.Throws<ArgumentOutOfRangeException>(() => new ProjectDependencyGraphBuilder().Build(inventory, packageGraphs));
    }

    [Fact]
    public void ProjectParameterImpactCrossesPackageBindingToChildConsumers()
    {
        var inventory = new ProjectInventory("project", "ProjectDeployment", new[]
        {
            new ProjectPackageInfo("parent", "Parent.dtsx"),
            new ProjectPackageInfo("child", "Child.dtsx", new[]
            {
                new PackageParameterInfo("child-parameter", "InputValue")
            })
        }, new[] { new ProjectParameterInfo("project-parameter", "RunDate") });
        var parentKey = inventory.Packages.Single(item => item.PackageId == "parent").DependencyKey("project");
        var childKey = inventory.Packages.Single(item => item.PackageId == "child").DependencyKey("project");
        var projectParameter = new DependencyNode("Parameter:ProjectParameter:project-parameter",
            SemanticObjectKind.Parameter, "$Project::RunDate", "project-parameter", "parent");
        var childParameter = new DependencyNode("Parameter:PackageParameter:child-parameter",
            SemanticObjectKind.Parameter, "$Package::InputValue", "child-parameter", "child");
        var parentTask = new DependencyNode("Executable:parent-call", SemanticObjectKind.Executable, "Call Child", "parent-call");
        var binding = new DependencyNode("PackageParameterBinding:project:parent-call:child-parameter",
            SemanticObjectKind.PackageParameterBinding, "InputValue", "binding", "parent-call");
        var childTask = new DependencyNode("Executable:child-consumer", SemanticObjectKind.Executable, "Consume Input", "child-consumer");
        var parentGraph = new PackageDependencyGraph(new[]
        {
            new DependencyNode(parentKey, SemanticObjectKind.Package, "Parent.dtsx", "parent", "project"),
            new DependencyNode(childKey, SemanticObjectKind.Package, "Child.dtsx", "child", "project"),
            parentTask, binding, projectParameter, childParameter
        }, new[]
        {
            new DependencyEdge(parentKey, parentTask.Key, DependencyKind.ContainsTask),
            new DependencyEdge(parentTask.Key, binding.Key, DependencyKind.HasPackageParameterBinding,
                "DeclaredPackageParameterBinding"),
            new DependencyEdge(binding.Key, projectParameter.Key, DependencyKind.UsesParameter, "LexicalAndScopeResolved"),
            new DependencyEdge(binding.Key, childParameter.Key, DependencyKind.BindsPackageParameter,
                "DeclaredPackageParameterBinding"),
            new DependencyEdge(parentTask.Key, childKey, DependencyKind.InvokesPackage, "ProjectInventoryResolved"),
        }, Array.Empty<UnsupportedItem>());
        var childGraph = new PackageDependencyGraph(new[]
        {
            new DependencyNode(parentKey, SemanticObjectKind.Package, "Parent.dtsx", "parent", "project"),
            new DependencyNode(childKey, SemanticObjectKind.Package, "Child.dtsx", "child", "project"),
            childTask, projectParameter, childParameter
        }, new[]
        {
            new DependencyEdge(childKey, childTask.Key, DependencyKind.ContainsTask),
            new DependencyEdge(childTask.Key, childParameter.Key, DependencyKind.UsesParameter, "LexicalAndScopeResolved")
        }, Array.Empty<UnsupportedItem>());
        var graph = new ProjectDependencyGraphBuilder().Build(inventory, new[]
        {
            new ProjectPackageGraph("parent", parentGraph), new ProjectPackageGraph("child", childGraph)
        });
        var root = Assert.Single(graph.Nodes, node => node.Kind == SemanticObjectKind.Parameter &&
            node.NativeId == "project-parameter");

        var impact = new RichImpactAnalysisQuery().Analyze(graph, root.Key).Items.Single();

        Assert.Contains(impact.Impacts, item => item.Node.Kind == SemanticObjectKind.Parameter &&
            item.Node.Name == "$Package::InputValue");
        Assert.Contains(impact.Impacts, item => item.Node.NativeId == "child-consumer" &&
            item.Path.Any(edge => edge.Kind == DependencyKind.BindsPackageParameter));
        Assert.Contains(graph.Edges, edge => edge.Kind == DependencyKind.InvokesPackage);
        Assert.Contains(graph.Edges, edge => edge.Kind == DependencyKind.BindsPackageParameter);
        Assert.Empty(graph.UnsupportedItems);
    }

    [Fact]
    public void SingleHopInvocationUsesStablePackageIdentity()
    {
        var (inventory, graph) = BuildProjectGraph(("A", "B"));
        var parentKey = PackageKey(inventory, "A");

        var result = new DependencyQuery().Find(graph, parentKey, incoming: false, recursive: true).Items.Single();

        Assert.Contains(result.Nodes, node => node.Kind == SemanticObjectKind.Package && node.NativeId == "B");
        Assert.Contains(result.Edges, edge => edge.Kind == DependencyKind.InvokesPackage);
        Assert.Empty(result.UnsupportedItems);
    }

    [Fact]
    public void MultiHopInvocationReachesAllSuppliedPackages()
    {
        var (inventory, graph) = BuildProjectGraph(("A", "B"), ("B", "C"));

        var result = new DependencyQuery().Find(graph, PackageKey(inventory, "A"), incoming: false, recursive: true).Items.Single();

        Assert.Contains(result.Nodes, node => node.Kind == SemanticObjectKind.Package && node.NativeId == "B");
        Assert.Contains(result.Nodes, node => node.Kind == SemanticObjectKind.Package && node.NativeId == "C");
        Assert.Equal(2, result.Edges.Count(edge => edge.Kind == DependencyKind.InvokesPackage));
        Assert.Empty(result.UnsupportedItems);
    }

    [Fact]
    public void SelfInvocationTerminatesAndRemainsAResolvedCycle()
    {
        var (inventory, graph) = BuildProjectGraph(("A", "A"));
        var packageKey = PackageKey(inventory, "A");

        var result = new DependencyQuery().Find(graph, packageKey, incoming: false, recursive: true).Items.Single();
        var impact = new ImpactAnalysisQuery().Analyze(graph, packageKey).Items.Single();
        var richImpact = new RichImpactAnalysisQuery().Analyze(graph, packageKey).Items.Single();

        Assert.Contains(result.Edges, edge => edge.Kind == DependencyKind.InvokesPackage && edge.To == packageKey);
        Assert.Contains(impact.Paths, edge => edge.Kind == DependencyKind.InvokesPackage && edge.To == packageKey);
        Assert.Single(richImpact.Impacts);
        Assert.Empty(richImpact.UnsupportedItems);
        Assert.DoesNotContain(result.UnsupportedItems, item => item.ReasonCode == UnsupportedItem.ExecutePackageTargetUnresolvedCode);
        Assert.Equal(result.Nodes.Select(node => node.Key).OrderBy(key => key, StringComparer.Ordinal),
            new ProjectDependencyGraphBuilder().Build(inventory, BuildPackageGraphs(inventory, ("A", "A")))
                .Nodes.Select(node => node.Key).OrderBy(key => key, StringComparer.Ordinal));
    }

    [Fact]
    public void MutualCycleTerminatesAndPreservesBothInvocationEdges()
    {
        var (inventory, graph) = BuildProjectGraph(("A", "B"), ("B", "A"));
        var packageKey = PackageKey(inventory, "A");

        var result = new DependencyQuery().Find(graph, packageKey, incoming: false, recursive: true).Items.Single();

        Assert.Contains(result.Nodes, node => node.Kind == SemanticObjectKind.Package && node.NativeId == "B");
        Assert.Equal(2, result.Edges.Count(edge => edge.Kind == DependencyKind.InvokesPackage));
        Assert.DoesNotContain(result.UnsupportedItems, item =>
            item.ReasonCode == UnsupportedItem.ExecutePackageTargetUnresolvedCode ||
            item.ReasonCode == UnsupportedItem.ProjectPackageContentsNotLoadedCode);
    }

    [Fact]
    public void OmittedPackageContentsRemainExplicitCoverage()
    {
        var inventory = Inventory("A", "B");
        var graph = new ProjectDependencyGraphBuilder().Build(inventory,
            BuildPackageGraphs(inventory, ("A", "B")).Where(item => item.PackageId == "A"));

        Assert.Contains(graph.Nodes, node => node.Kind == SemanticObjectKind.Package && node.NativeId == "B");
        Assert.Contains(graph.UnsupportedItems, item => item.Id == "B" &&
            item.ReasonCode == UnsupportedItem.ProjectPackageContentsNotLoadedCode);
    }

    private static (ProjectInventory Inventory, PackageDependencyGraph Graph) BuildProjectGraph(
        params (string From, string To)[] invocations)
    {
        var inventory = Inventory(invocations.SelectMany(call => new[] { call.From, call.To }).Distinct(StringComparer.Ordinal).ToArray());
        return (inventory, new ProjectDependencyGraphBuilder().Build(inventory, BuildPackageGraphs(inventory, invocations)));
    }

    private static ProjectInventory Inventory(params string[] packageIds) => new("project", "ProjectDeployment",
        packageIds.OrderBy(id => id, StringComparer.Ordinal)
            .Select(id => new ProjectPackageInfo(id, id + ".dtsx")), Array.Empty<ProjectParameterInfo>());

    private static IEnumerable<ProjectPackageGraph> BuildPackageGraphs(ProjectInventory inventory,
        params (string From, string To)[] invocations)
    {
        foreach (var package in inventory.Packages.OrderBy(item => item.PackageId, StringComparer.Ordinal))
        {
            var packageNodeKey = package.DependencyKey(inventory.ProjectIdentity);
            var nodes = inventory.Packages.Select(item => new DependencyNode(
                item.DependencyKey(inventory.ProjectIdentity), SemanticObjectKind.Package,
                item.ProjectRelativeName, item.PackageId, inventory.ProjectIdentity)).ToList();
            var edges = new List<DependencyEdge>();
            var index = 0;
            foreach (var invocation in invocations.Where(call => call.From == package.PackageId))
            {
                var taskId = "call-" + package.PackageId + "-" + index++;
                var taskKey = "Executable:" + taskId;
                nodes.Add(new DependencyNode(taskKey, SemanticObjectKind.Executable, "Execute Package", taskId));
                edges.Add(new DependencyEdge(packageNodeKey, taskKey, DependencyKind.ContainsTask));
                edges.Add(new DependencyEdge(taskKey, PackageKey(inventory, invocation.To),
                    DependencyKind.InvokesPackage, "ProjectInventoryResolved"));
            }
            yield return new ProjectPackageGraph(package.PackageId,
                new PackageDependencyGraph(nodes, edges, Array.Empty<UnsupportedItem>()));
        }
    }

    private static string PackageKey(ProjectInventory inventory, string packageId) =>
        inventory.Packages.Single(item => item.PackageId == packageId).DependencyKey(inventory.ProjectIdentity);
}
