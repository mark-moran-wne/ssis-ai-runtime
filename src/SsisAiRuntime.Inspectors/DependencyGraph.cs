using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SsisAiRuntime.Inspectors
{
    public enum DependencyKind
    {
        UsesConnection,
        ContainsTask,
        DependsOnTask,
        ContainsDataFlow,
        ContainsComponent,
        ConnectsComponent,
        ReadsColumn,
        WritesColumn,
        HasExpression,
        UsesVariable,
        UsesParameter,
        ReadsSchemaObject,
        WritesSchemaObject,
        ExecutesSchemaObject,
        ReferencesSqlFunction
    }

    public sealed class DependencyNode
    {
        public DependencyNode(string key, SemanticObjectKind kind, string name, string nativeId, string parentId = "")
        {
            if (string.IsNullOrWhiteSpace(key)) { throw new ArgumentException("A dependency node key is required.", nameof(key)); }
            Key = key;
            Kind = kind;
            Name = name ?? string.Empty;
            NativeId = nativeId ?? string.Empty;
            ParentId = parentId ?? string.Empty;
        }

        public string Key { get; }
        public SemanticObjectKind Kind { get; }
        public string Name { get; }
        public string NativeId { get; }
        public string ParentId { get; }
    }

    public sealed class DependencyEdge
    {
        public DependencyEdge(string from, string to, DependencyKind kind, string evidence = "")
        {
            From = from ?? throw new ArgumentNullException(nameof(from));
            To = to ?? throw new ArgumentNullException(nameof(to));
            Kind = kind;
            Evidence = evidence ?? string.Empty;
        }

        public string From { get; }
        public string To { get; }
        public DependencyKind Kind { get; }
        public string Evidence { get; }
    }

    public static class DependencyEvidence
    {
        public static string Safe(string evidence) => evidence == "LexicalAndScopeResolved" || evidence == "ParsedSchemaObject" ||
            evidence == "ParsedExecuteTarget" || evidence == "ParsedFunctionReference" ? evidence : string.Empty;
    }

    public sealed class PackageDependencyGraph
    {
        public PackageDependencyGraph(IEnumerable<DependencyNode> nodes, IEnumerable<DependencyEdge> edges,
            IEnumerable<UnsupportedItem> unsupportedItems)
        {
            Nodes = ReadOnly(nodes, nameof(nodes));
            Edges = ReadOnly(edges, nameof(edges));
            UnsupportedItems = ReadOnly(unsupportedItems, nameof(unsupportedItems));
        }

        public IReadOnlyList<DependencyNode> Nodes { get; }
        public IReadOnlyList<DependencyEdge> Edges { get; }
        public IReadOnlyList<UnsupportedItem> UnsupportedItems { get; }
        public bool IsComplete => UnsupportedItems.Count == 0;

        private static IReadOnlyList<T> ReadOnly<T>(IEnumerable<T> items, string name)
        {
            if (items == null) { throw new ArgumentNullException(name); }
            return new ReadOnlyCollection<T>(new List<T>(items));
        }
    }

    public sealed class PackageDependencyGraphBuilder
    {
        public PackageDependencyGraph Build(PackageOverview package, IEnumerable<ConnectionOverview> connections,
            IEnumerable<ExecutableOverview> executables, IEnumerable<SqlStatementOverview> sqlStatements,
            IEnumerable<DataFlowOverview> dataFlows, ControlFlowGraph controlFlow,
            IEnumerable<UnsupportedItem> unsupportedItems)
        {
            if (package == null) { throw new ArgumentNullException(nameof(package)); }
            if (controlFlow == null) { throw new ArgumentNullException(nameof(controlFlow)); }
            var nodes = new Dictionary<string, DependencyNode>(StringComparer.Ordinal);
            var edges = new List<DependencyEdge>();
            var unsupported = new List<UnsupportedItem>(unsupportedItems ?? throw new ArgumentNullException(nameof(unsupportedItems)));
            var packageKey = Key(SemanticObjectKind.Package, package.PackageId);
            Add(nodes, new DependencyNode(packageKey, SemanticObjectKind.Package, package.PackageName, package.PackageId));

            foreach (var connection in connections ?? throw new ArgumentNullException(nameof(connections)))
            {
                Add(nodes, new DependencyNode(Key(SemanticObjectKind.Connection, connection.Id), SemanticObjectKind.Connection,
                    connection.Name, connection.Id));
            }

            foreach (var executable in executables ?? throw new ArgumentNullException(nameof(executables)))
            {
                var key = Key(SemanticObjectKind.Executable, executable.Id);
                Add(nodes, new DependencyNode(key, SemanticObjectKind.Executable, executable.Name, executable.Id, executable.ParentId));
            }
            foreach (var executable in executables)
            {
                AddEdge(nodes, edges, unsupported, new DependencyEdge(string.IsNullOrEmpty(executable.ParentId)
                    ? packageKey
                    : Key(SemanticObjectKind.Executable, executable.ParentId), Key(SemanticObjectKind.Executable, executable.Id),
                    DependencyKind.ContainsTask), executable.Id, executable.Name);
            }

            foreach (var edge in controlFlow.Edges.Where(edge => edge.Kind == ControlFlowEdgeKind.Precedence))
            {
                var from = controlFlow.Nodes.FirstOrDefault(node => node.Handle.Equals(edge.From));
                var to = controlFlow.Nodes.FirstOrDefault(node => node.Handle.Equals(edge.To));
                if (from == null || to == null)
                {
                    AddGap(unsupported, "PrecedenceConstraint", "A dependency endpoint could not be resolved.");
                    continue;
                }
                AddEdge(nodes, edges, unsupported, new DependencyEdge(Key(SemanticObjectKind.Executable, from.NativeId),
                    Key(SemanticObjectKind.Executable, to.NativeId), DependencyKind.DependsOnTask),
                    string.Empty, edge.ConstraintName);
            }

            foreach (var statement in sqlStatements ?? throw new ArgumentNullException(nameof(sqlStatements)))
            {
                if (!string.IsNullOrWhiteSpace(statement.ConnectionManagerId))
                {
                    AddEdge(nodes, edges, unsupported, new DependencyEdge(Key(SemanticObjectKind.Executable, statement.TaskId),
                        Key(SemanticObjectKind.Connection, statement.ConnectionManagerId), DependencyKind.UsesConnection, "ExecuteSqlTask"),
                        statement.TaskId, statement.TaskName);
                }
            }

            foreach (var flow in dataFlows ?? throw new ArgumentNullException(nameof(dataFlows)))
            {
                var flowKey = Key(SemanticObjectKind.DataFlow, flow.ExecutableId);
                Add(nodes, new DependencyNode(flowKey, SemanticObjectKind.DataFlow, flow.ExecutableName, flow.ExecutableId));
                AddEdge(nodes, edges, unsupported,
                    new DependencyEdge(Key(SemanticObjectKind.Executable, flow.ExecutableId), flowKey, DependencyKind.ContainsDataFlow),
                    flow.ExecutableId, flow.ExecutableName);
                foreach (var component in flow.Components)
                {
                    var componentKey = ScopedKey(SemanticObjectKind.DataFlowComponent, flow.ExecutableId, component.Id);
                    Add(nodes, new DependencyNode(componentKey, SemanticObjectKind.DataFlowComponent,
                        component.Name, component.Id, flow.ExecutableId));
                    AddEdge(nodes, edges, unsupported, new DependencyEdge(flowKey, componentKey, DependencyKind.ContainsComponent),
                        component.Id, component.Name);
                    foreach (var runtimeConnection in component.RuntimeConnections.Where(item => !string.IsNullOrWhiteSpace(item.ConnectionManagerId)))
                    {
                        AddEdge(nodes, edges, unsupported, new DependencyEdge(componentKey,
                            Key(SemanticObjectKind.Connection, runtimeConnection.ConnectionManagerId),
                            DependencyKind.UsesConnection, runtimeConnection.Name), runtimeConnection.Id, runtimeConnection.Name);
                    }
                    foreach (var column in component.InputColumns)
                    {
                        AddColumn(nodes, edges, unsupported, flow.ExecutableId, component.Id, componentKey, column,
                            SemanticObjectKind.InputColumn, DependencyKind.ReadsColumn);
                    }
                    foreach (var column in component.OutputColumns)
                    {
                        AddColumn(nodes, edges, unsupported, flow.ExecutableId, component.Id, componentKey, column,
                            SemanticObjectKind.OutputColumn, DependencyKind.WritesColumn);
                    }
                }

                foreach (var path in flow.Paths)
                {
                    AddEdge(nodes, edges, unsupported, new DependencyEdge(ScopedKey(SemanticObjectKind.DataFlowComponent, flow.ExecutableId, path.SourceComponentId),
                        ScopedKey(SemanticObjectKind.DataFlowComponent, flow.ExecutableId, path.TargetComponentId),
                        DependencyKind.ConnectsComponent, path.Id), path.Id, path.Name);
                }
            }

            var distinctEdges = edges.GroupBy(edge => edge.From + "\u001f" + edge.To + "\u001f" + edge.Kind)
                .Select(group => group.First());
            return new SqlDependencies.SqlDependencyGraphBuilder().Enrich(
                new PackageDependencyGraph(nodes.Values.OrderBy(node => node.Key, StringComparer.Ordinal), distinctEdges, unsupported), sqlStatements);
        }

        private static void AddColumn(IDictionary<string, DependencyNode> nodes, ICollection<DependencyEdge> edges,
            ICollection<UnsupportedItem> unsupported,
            string flowId, string componentId, string componentKey, DataFlowColumnOverview column,
            SemanticObjectKind objectKind, DependencyKind dependencyKind)
        {
            var key = ScopedKey(objectKind, flowId + ":" + componentId, column.Id);
            Add(nodes, new DependencyNode(key, objectKind, column.Name, column.Id, componentId));
            AddEdge(nodes, edges, unsupported, new DependencyEdge(componentKey, key, dependencyKind), column.Id, column.Name);
        }

        private static void AddEdge(IDictionary<string, DependencyNode> nodes, ICollection<DependencyEdge> edges,
            ICollection<UnsupportedItem> unsupported, DependencyEdge edge, string id, string name)
        {
            if (!nodes.ContainsKey(edge.From) || !nodes.ContainsKey(edge.To))
            {
                AddGap(unsupported, id, name);
                return;
            }
            edges.Add(edge);
        }

        private static void AddGap(ICollection<UnsupportedItem> unsupported, string id, string name) =>
            unsupported.Add(new UnsupportedItem(id, name, "DependencyGraph", "A dependency endpoint could not be resolved.",
                UnsupportedItem.UnsupportedMetadataCode));

        private static void Add(IDictionary<string, DependencyNode> nodes, DependencyNode node)
        {
            if (!nodes.ContainsKey(node.Key)) { nodes.Add(node.Key, node); }
        }

        private static string Key(SemanticObjectKind kind, string id) => kind + ":" + (id ?? string.Empty);
        private static string ScopedKey(SemanticObjectKind kind, string scope, string id) => kind + ":" + scope + ":" + (id ?? string.Empty);
    }

    public sealed class DependencyQuery
    {
        public InspectionResult<PackageDependencyGraph> Find(PackageDependencyGraph graph, string key, bool incoming, bool recursive)
        {
            if (graph == null) { throw new ArgumentNullException(nameof(graph)); }
            var root = graph.Nodes.SingleOrDefault(node => node.Key == key);
            if (root == null) { return MissingSelection(); }
            var nodes = new List<DependencyNode> { root };
            var edges = new List<DependencyEdge>();
            var seen = new HashSet<string>(StringComparer.Ordinal) { key };
            var queue = new Queue<string>();
            queue.Enqueue(key);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var edge in graph.Edges.Where(edge => incoming ? edge.To == current : edge.From == current))
                {
                    var next = incoming ? edge.From : edge.To;
                    var node = graph.Nodes.SingleOrDefault(candidate => candidate.Key == next);
                    if (node == null) { continue; }
                    edges.Add(edge);
                    if (seen.Add(next))
                    {
                        nodes.Add(node);
                        if (recursive) { queue.Enqueue(next); }
                    }
                }
            }

            var result = new PackageDependencyGraph(nodes, edges, graph.UnsupportedItems);
            return new InspectionResult<PackageDependencyGraph>(new[] { result }, result.UnsupportedItems);
        }

        private static InspectionResult<PackageDependencyGraph> MissingSelection() =>
            new InspectionResult<PackageDependencyGraph>(Array.Empty<PackageDependencyGraph>(), new[]
            {
                new UnsupportedItem("", "", "DependencyGraph", "Selection missing or ambiguous.", UnsupportedItem.UnsupportedMetadataCode)
            });
    }

    public sealed class ImpactAnalysisResult
    {
        public ImpactAnalysisResult(DependencyNode root, IEnumerable<DependencyNode> impactedNodes,
            IEnumerable<DependencyEdge> paths, IEnumerable<UnsupportedItem> unsupportedItems)
        {
            Root = root ?? throw new ArgumentNullException(nameof(root));
            ImpactedNodes = new ReadOnlyCollection<DependencyNode>(new List<DependencyNode>(impactedNodes ?? throw new ArgumentNullException(nameof(impactedNodes))));
            Paths = new ReadOnlyCollection<DependencyEdge>(new List<DependencyEdge>(paths ?? throw new ArgumentNullException(nameof(paths))));
            UnsupportedItems = new ReadOnlyCollection<UnsupportedItem>(new List<UnsupportedItem>(unsupportedItems ?? throw new ArgumentNullException(nameof(unsupportedItems))));
        }

        public DependencyNode Root { get; }
        public IReadOnlyList<DependencyNode> ImpactedNodes { get; }
        public IReadOnlyList<DependencyEdge> Paths { get; }
        public IReadOnlyList<UnsupportedItem> UnsupportedItems { get; }
    }

    public sealed class ImpactAnalysisQuery
    {
        public InspectionResult<ImpactAnalysisResult> Analyze(PackageDependencyGraph graph, string key)
        {
            if (graph == null) { throw new ArgumentNullException(nameof(graph)); }
            var query = new DependencyQuery().Find(graph, key, incoming: true, recursive: true);
            if (query.Items.Count != 1)
            {
                return new InspectionResult<ImpactAnalysisResult>(Array.Empty<ImpactAnalysisResult>(), query.UnsupportedItems);
            }

            var selected = query.Items[0];
            var root = selected.Nodes.Single(node => node.Key == key);
            var result = new ImpactAnalysisResult(root, selected.Nodes.Where(node => node.Key != key),
                selected.Edges, selected.UnsupportedItems);
            return new InspectionResult<ImpactAnalysisResult>(new[] { result }, result.UnsupportedItems);
        }
    }
}