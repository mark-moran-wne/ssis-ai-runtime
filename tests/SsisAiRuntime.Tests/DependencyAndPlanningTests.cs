using SsisAiRuntime.AI;
using SsisAiRuntime.Inspectors;
using SsisAiRuntime.Inspectors.SqlDependencies;

namespace SsisAiRuntime.Tests;

public class DependencyAndPlanningTests
{
    [Fact]
    public void ImpactFindsConnectionConsumer()
    {
        var graph = new PackageDependencyGraph(new[]
        {
            new DependencyNode("Connection:c", SemanticObjectKind.Connection, "Warehouse", "c"),
            new DependencyNode("Executable:t", SemanticObjectKind.Executable, "Load", "t")
        }, new[]
        {
            new DependencyEdge("Executable:t", "Connection:c", DependencyKind.UsesConnection)
        }, Array.Empty<UnsupportedItem>());

        var result = new ImpactAnalysisQuery().Analyze(graph, "Connection:c");

        Assert.Equal("Load", result.Items.Single().ImpactedNodes.Single().Name);
    }

    [Fact]
    public void PackageDependencyBuilderPreservesTypedSqlControlFlowAndDataFlowEdges()
    {
        var package = new PackageOverview(Guid.NewGuid(), "Demo", "package", string.Empty, DateTime.UnixEpoch,
            1, 0, 0, "DontSaveSensitive", "Default", 1, 0, 3, 1, 0, false);
        var connection = new ConnectionOverview("Warehouse", "connection", "OLEDB");
        var executables = new[]
        {
            new ExecutableOverview("sql-task", string.Empty, "Read Students", "STOCK:SQLTask", string.Empty, 0, false, false),
            new ExecutableOverview("next-task", string.Empty, "Continue", "STOCK:PipelineTask", string.Empty, 0, false, false),
            new ExecutableOverview("flow-task", string.Empty, "Load Flow", "STOCK:PipelineTask", string.Empty, 0, false, false)
        };
        var precedence = new PrecedenceConstraintOverview("constraint", "On success", "sql-task", "Read Students",
            "next-task", "Continue", "Constraint", "Success", true, false);
        var catalog = new SemanticHandleCatalogBuilder().Build(package, new[] { connection },
            Array.Empty<VariableOverview>(), Array.Empty<ParameterOverview>(), executables, Array.Empty<DataFlowOverview>());
        var controlFlow = new ControlFlowGraphBuilder().Build(executables,
            InspectionResult<PrecedenceConstraintOverview>.Complete(new[] { precedence }), catalog);
        var sqlDependencies = new SqlDependencyResolution(new[]
        {
            new SqlObjectReference(new[] { "dbo", "Students" }, SqlDependencyAccess.Read,
                SqlDependencyEvidence.ParsedSchemaObject)
        }, Array.Empty<string>(), parsed: true);
        var statement = new SqlStatementOverview("sql-task", "Read Students", "STOCK:SQLTask",
            "DirectInput", string.Empty, true, "connection", "Warehouse", null, sqlDependencies);
        var sourceColumn = new DataFlowColumnOverview("source", "source-output", "Output", "Output",
            "source-column", "StudentId", "DT_I4", 0, 0, 0, 0, 41, 0, "Output");
        var destinationColumn = new DataFlowColumnOverview("destination", "destination-input", "Input", "Input",
            "destination-column", "StudentId", "DT_I4", 0, 0, 0, 0, 41, 0, "ReadOnly");
        var source = new DataFlowComponentOverview("source", "Flat File Source", "source-class", string.Empty,
            0, 1, Array.Empty<DataFlowColumnOverview>(), new[] { sourceColumn }, Array.Empty<DataFlowColumnOverview>(),
            Array.Empty<DataFlowRuntimeConnectionOverview>(), Array.Empty<DataFlowSettingOverview>());
        var destination = new DataFlowComponentOverview("destination", "OLE DB Destination", "destination-class",
            string.Empty, 1, 0, new[] { destinationColumn }, Array.Empty<DataFlowColumnOverview>(),
            Array.Empty<DataFlowColumnOverview>(),
            new[] { new DataFlowRuntimeConnectionOverview("runtime-connection", "Destination", "connection") },
            Array.Empty<DataFlowSettingOverview>());
        var flow = new DataFlowOverview("flow-task", "Load Flow", new[] { source, destination },
            new[] { new DataFlowPathOverview("path", "StudentId path", "source", "source-output",
                "destination", "destination-input") });

        var graph = new PackageDependencyGraphBuilder().Build(package, new[] { connection }, executables,
            new[] { statement }, new[] { flow }, controlFlow, Array.Empty<UnsupportedItem>());
        var edges = graph.Edges;

        Assert.Contains(edges, edge => edge.From == "Package:package" && edge.To == "Executable:sql-task" &&
            edge.Kind == DependencyKind.ContainsTask);
        Assert.Contains(edges, edge => edge.From == "Executable:sql-task" && edge.To == "Connection:connection" &&
            edge.Kind == DependencyKind.UsesConnection && edge.Evidence == "ExecuteSqlTask");
        Assert.Contains(edges, edge => edge.From == "Executable:sql-task" && edge.To == "Executable:next-task" &&
            edge.Kind == DependencyKind.DependsOnTask);
        Assert.Contains(edges, edge => edge.From == "Executable:flow-task" && edge.To == "DataFlow:flow-task" &&
            edge.Kind == DependencyKind.ContainsDataFlow);
        Assert.Contains(edges, edge => edge.From == "DataFlow:flow-task" &&
            edge.To == "DataFlowComponent:flow-task:source" && edge.Kind == DependencyKind.ContainsComponent);
        Assert.Contains(edges, edge => edge.From == "DataFlowComponent:flow-task:destination" &&
            edge.To == "Connection:connection" && edge.Kind == DependencyKind.UsesConnection && edge.Evidence == "Destination");
        Assert.Contains(edges, edge => edge.From == "DataFlowComponent:flow-task:source" &&
            edge.To == "OutputColumn:flow-task:source:source-column" && edge.Kind == DependencyKind.WritesColumn);
        Assert.Contains(edges, edge => edge.From == "DataFlowComponent:flow-task:destination" &&
            edge.To == "InputColumn:flow-task:destination:destination-column" && edge.Kind == DependencyKind.ReadsColumn);
        Assert.Contains(edges, edge => edge.From == "DataFlowComponent:flow-task:source" &&
            edge.To == "DataFlowComponent:flow-task:destination" && edge.Kind == DependencyKind.ConnectsComponent &&
            edge.Evidence == "path");
        Assert.Contains(edges, edge => edge.From == "Executable:sql-task" &&
            edge.To == SqlDependencyGraphBuilder.ObjectKey("connection", new[] { "dbo", "Students" }) &&
            edge.Kind == DependencyKind.ReadsSchemaObject && edge.Evidence == "ParsedSchemaObject");
        Assert.Empty(graph.UnsupportedItems);
    }

    [Fact]
    public void ExecutePackageWithoutResolvedTargetReportsCoverageInsteadOfInventingEdge()
    {
        var package = new PackageOverview(Guid.NewGuid(), "Parent", "parent", string.Empty, DateTime.UnixEpoch,
            1, 0, 0, "DontSaveSensitive", "Default", 0, 0, 1, 0, 0, false);
        var task = new ExecutableOverview("execute-package", string.Empty, "Run Child Package",
            "Microsoft.SqlServer.Dts.Tasks.ExecutePackageTask.ExecutePackageTask, Microsoft.SqlServer.ExecPkgTask",
            string.Empty, 0, false, false);
        var graph = new PackageDependencyGraphBuilder().Build(package,
            Array.Empty<ConnectionOverview>(), new[] { task }, Array.Empty<SqlStatementOverview>(),
            Array.Empty<DataFlowOverview>(),
            new ControlFlowGraph(Array.Empty<ControlFlowNode>(), Array.Empty<ControlFlowEdge>(), Array.Empty<UnsupportedItem>()),
            Array.Empty<UnsupportedItem>());

        Assert.DoesNotContain(graph.Edges, edge => edge.From == "Executable:execute-package");
        Assert.Contains(graph.UnsupportedItems, item =>
            item.ReasonCode == "dependency.execute_package_target_unresolved" && item.Id == "execute-package");
    }

    [Fact]
    public void ExecutePackageProjectReferencePreservesDeclaredTargetWithoutClaimingResolution()
    {
        var package = new PackageOverview(Guid.NewGuid(), "Parent", "parent", string.Empty, DateTime.UnixEpoch,
            1, 0, 0, "DontSaveSensitive", "Default", 0, 0, 1, 0, 0, false);
        var task = new ExecutableOverview("execute-package", string.Empty, "Run Child Package",
            "Microsoft.SqlServer.Dts.Tasks.ExecutePackageTask.ExecutePackageTask, Microsoft.SqlServer.ExecPkgTask",
            string.Empty, 0, false, false, "ProjectReference", "ChildPackage");
        var graph = new PackageDependencyGraphBuilder().Build(package,
            Array.Empty<ConnectionOverview>(), new[] { task }, Array.Empty<SqlStatementOverview>(),
            Array.Empty<DataFlowOverview>(),
            new ControlFlowGraph(Array.Empty<ControlFlowNode>(), Array.Empty<ControlFlowEdge>(), Array.Empty<UnsupportedItem>()),
            Array.Empty<UnsupportedItem>());
        var externalTarget = Assert.Single(graph.Nodes, node =>
            node.Kind == SemanticObjectKind.ExternalPackageReference && node.Name == "ChildPackage");

        Assert.Contains(graph.Edges, edge => edge.From == "Executable:execute-package" &&
            edge.To == externalTarget.Key && edge.Kind == DependencyKind.InvokesPackage &&
            edge.Evidence == "DeclaredProjectReference");
        Assert.Contains(graph.UnsupportedItems, item =>
            item.Id == "execute-package" && item.ReasonCode == "dependency.execute_package_target_unresolved");
        Assert.DoesNotContain(graph.Nodes, node => node.Kind == SemanticObjectKind.Package && node.Name == "ChildPackage");
        var consumers = new DependencyQuery().Find(graph, externalTarget.Key, incoming: true, recursive: true).Items.Single();
        Assert.Contains(consumers.Nodes, node => node.Key == "Executable:execute-package");
    }

    [Fact]
    public void ExecutePackageProjectReferenceResolvesUniqueInventoryTarget()
    {
        var inventory = new ProjectInventory("project-identity", "ProjectDeployment", new[]
        {
            new ProjectPackageInfo("parent", "Parent.dtsx"),
            new ProjectPackageInfo("child", "ChildPackage")
        }, Array.Empty<ProjectParameterInfo>());

        var graph = BuildProjectReferenceGraph(inventory);
        var parent = graph.Nodes.Single(node => node.Kind == SemanticObjectKind.Package && node.NativeId == "parent");
        var child = graph.Nodes.Single(node => node.Kind == SemanticObjectKind.Package && node.NativeId == "child");
        var externalReference = graph.Nodes.Single(node => node.Kind == SemanticObjectKind.ExternalPackageReference);

        Assert.Equal("project-identity", parent.ParentId);
        Assert.Equal("project-identity", child.ParentId);
        Assert.Contains(graph.Edges, edge => edge.From == "Executable:execute-package" &&
            edge.To == externalReference.Key && edge.Kind == DependencyKind.InvokesPackage &&
            edge.Evidence == "DeclaredProjectReference");
        Assert.Contains(graph.Edges, edge => edge.From == "Executable:execute-package" && edge.To == child.Key &&
            edge.Kind == DependencyKind.InvokesPackage && edge.Evidence == "ProjectInventoryResolved");
        Assert.Contains(graph.Edges, edge => edge.From == externalReference.Key && edge.To == child.Key &&
            edge.Kind == DependencyKind.ResolvesPackageReference && edge.Evidence == "ProjectInventoryResolved");
        Assert.DoesNotContain(graph.UnsupportedItems, item => item.Id == "execute-package" &&
            item.ReasonCode == UnsupportedItem.ExecutePackageTargetUnresolvedCode);
    }

    [Fact]
    public void ExecutePackageProjectReferenceDoesNotResolveWithoutParentInventoryIdentity()
    {
        var inventory = new ProjectInventory("project-identity", "ProjectDeployment", new[]
        {
            new ProjectPackageInfo("child", "ChildPackage")
        }, Array.Empty<ProjectParameterInfo>());

        var graph = BuildProjectReferenceGraph(inventory);

        Assert.DoesNotContain(graph.Nodes, node => node.Kind == SemanticObjectKind.Package && node.NativeId == "child");
        Assert.Contains(graph.Edges, edge => edge.From == "Executable:execute-package" &&
            edge.Kind == DependencyKind.InvokesPackage && edge.Evidence == "DeclaredProjectReference");
        Assert.Contains(graph.UnsupportedItems, item => item.Id == "execute-package" &&
            item.ReasonCode == UnsupportedItem.ExecutePackageTargetUnresolvedCode);
    }

    [Fact]
    public void ExecutePackageProjectReferenceDoesNotGuessAmongDuplicateInventoryTargets()
    {
        var inventory = new ProjectInventory("project-identity", "ProjectDeployment", new[]
        {
            new ProjectPackageInfo("parent", "Parent.dtsx"),
            new ProjectPackageInfo("child-a", "ChildPackage"),
            new ProjectPackageInfo("child-b", "ChildPackage")
        }, Array.Empty<ProjectParameterInfo>());

        var graph = BuildProjectReferenceGraph(inventory);

        Assert.DoesNotContain(graph.Nodes, node => node.Kind == SemanticObjectKind.Package &&
            (node.NativeId == "child-a" || node.NativeId == "child-b"));
        Assert.DoesNotContain(graph.Edges, edge => edge.From == "Executable:execute-package" &&
            edge.Kind == DependencyKind.InvokesPackage && edge.Evidence == "ProjectInventoryResolved");
        Assert.Contains(graph.UnsupportedItems, item => item.Id == "execute-package" &&
            item.ReasonCode == UnsupportedItem.ExecutePackageTargetAmbiguousCode);
    }

    private static PackageDependencyGraph BuildProjectReferenceGraph(IProjectInventory inventory)
    {
        var package = new PackageOverview(Guid.NewGuid(), "Parent", "parent", string.Empty, DateTime.UnixEpoch,
            1, 0, 0, "DontSaveSensitive", "Default", 0, 0, 1, 0, 0, false);
        var task = new ExecutableOverview("execute-package", string.Empty, "Run Child Package",
            "Microsoft.SqlServer.Dts.Tasks.ExecutePackageTask.ExecutePackageTask, Microsoft.SqlServer.ExecPkgTask",
            string.Empty, 0, false, false, "ProjectReference", "ChildPackage");
        return new PackageDependencyGraphBuilder().Build(package,
            Array.Empty<ConnectionOverview>(), new[] { task }, Array.Empty<SqlStatementOverview>(),
            Array.Empty<DataFlowOverview>(),
            new ControlFlowGraph(Array.Empty<ControlFlowNode>(), Array.Empty<ControlFlowEdge>(), Array.Empty<UnsupportedItem>()),
            Array.Empty<UnsupportedItem>(), inventory);
    }

    [Fact]
    public void PlannerRoutesDependencyQuestionWithoutResolvingItsSelector()
    {
        var plan = new QuestionPlanner().Plan("What uses WarehouseDB?");

        Assert.Equal(QuestionIntent.DependencyQuery, plan.Intent);
        Assert.Equal(new[] { "nodeKey" }, plan.RequiredSelectors);
    }
}