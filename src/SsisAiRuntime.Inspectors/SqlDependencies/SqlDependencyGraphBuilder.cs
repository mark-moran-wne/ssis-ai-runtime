using System;
using System.Collections.Generic;
using System.Linq;

namespace SsisAiRuntime.Inspectors.SqlDependencies
{
    public sealed class SqlDependencyGraphBuilder
    {
        public PackageDependencyGraph Enrich(PackageDependencyGraph graph, IEnumerable<SqlStatementOverview> statements)
        {
            if (graph == null) { throw new ArgumentNullException(nameof(graph)); }
            if (statements == null) { throw new ArgumentNullException(nameof(statements)); }
            var nodes = graph.Nodes.ToDictionary(node => node.Key, StringComparer.Ordinal);
            var edges = new List<DependencyEdge>(graph.Edges);
            var gaps = new List<UnsupportedItem>(graph.UnsupportedItems);
            foreach (var statement in statements)
            {
                var analysis = statement.SqlDependencies;
                if (analysis == null) { continue; }
                gaps.AddRange(analysis.CoverageCodes.Select(code => SqlDependencyCoverage.Gap(statement.TaskId, code)));
                var owners = nodes.Values.Where(node => node.Kind == SemanticObjectKind.Executable && node.NativeId == statement.TaskId).ToArray();
                var connections = nodes.Values.Where(node => node.Kind == SemanticObjectKind.Connection && node.NativeId == statement.ConnectionManagerId).ToArray();
                if (owners.Length != 1 || connections.Length != 1)
                { gaps.Add(SqlDependencyCoverage.Gap(statement.TaskId, "sql.connection_unresolved")); continue; }
                foreach (var reference in analysis.References)
                {
                    var key = ObjectKey(statement.ConnectionManagerId, reference.Identifiers);
                    if (!nodes.ContainsKey(key)) { nodes.Add(key, new DependencyNode(key, SemanticObjectKind.SchemaObject, reference.QualifiedName, string.Empty, statement.ConnectionManagerId)); }
                    edges.Add(new DependencyEdge(owners[0].Key, key, EdgeKind(reference.Access), reference.Evidence.ToString()));
                }
            }
            return new PackageDependencyGraph(nodes.Values.OrderBy(node => node.Key, StringComparer.Ordinal),
                edges.GroupBy(edge => edge.From + "\u001f" + edge.To + "\u001f" + edge.Kind + "\u001f" + edge.Evidence, StringComparer.Ordinal).Select(group => group.First()),
                gaps.GroupBy(gap => gap.Id + "\u001f" + gap.Name + "\u001f" + gap.ReasonCode, StringComparer.Ordinal).Select(group => group.First()));
        }
        public static string ObjectKey(string connectionId, IEnumerable<string> identifiers) =>
            "SchemaObject:" + Uri.EscapeDataString(connectionId ?? string.Empty) + ":" +
            string.Join("/", identifiers.Select(part => Uri.EscapeDataString((part ?? string.Empty).Length + ":" + (part ?? string.Empty))));
        private static DependencyKind EdgeKind(SqlDependencyAccess access)
        {
            switch (access)
            {
                case SqlDependencyAccess.Write: return DependencyKind.WritesSchemaObject;
                case SqlDependencyAccess.Execute: return DependencyKind.ExecutesSchemaObject;
                case SqlDependencyAccess.Function: return DependencyKind.ReferencesSqlFunction;
                default: return DependencyKind.ReadsSchemaObject;
            }
        }
    }
}