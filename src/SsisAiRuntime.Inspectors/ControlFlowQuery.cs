using System;
using System.Collections.Generic;
using System.Linq;

namespace SsisAiRuntime.Inspectors
{
    public sealed class ControlFlowQuery
    {
        public InspectionResult<ControlFlowGraph> FindRelated(ControlFlowGraph graph, string nativeId, bool predecessors, bool recursive = false)
        {
            if (graph == null) { throw new ArgumentNullException(nameof(graph)); }
            var roots = graph.Nodes.Where(node => string.Equals(node.NativeId, nativeId, StringComparison.OrdinalIgnoreCase)).ToList();
            if (string.IsNullOrWhiteSpace(nativeId) || roots.Count != 1)
            {
                return new InspectionResult<ControlFlowGraph>(Array.Empty<ControlFlowGraph>(), new[]
                {
                    new UnsupportedItem("", "", "", "The task selection was missing or ambiguous.", UnsupportedItem.UnsupportedMetadataCode)
                });
            }

            var root = roots[0];
            var visited = new HashSet<SemanticHandle> { root.Handle };
            var nodes = new List<ControlFlowNode> { root };
            var edges = new List<ControlFlowEdge>();
            var unsupported = new List<UnsupportedItem>(graph.UnsupportedItems);
            var pending = new Queue<SemanticHandle>();
            pending.Enqueue(root.Handle);
            while (pending.Count > 0)
            {
                var current = pending.Dequeue();
                foreach (var edge in graph.Edges.Where(item => item.Kind == ControlFlowEdgeKind.Precedence &&
                    (predecessors ? item.To.Equals(current) : item.From.Equals(current))))
                {
                    var next = predecessors ? edge.From : edge.To;
                    var matches = graph.Nodes.Where(node => node.Handle.Equals(next)).ToList();
                    if (matches.Count != 1)
                    {
                        unsupported.Add(new UnsupportedItem("", "", "PrecedenceConstraint",
                            "A query endpoint is missing or ambiguous.", UnsupportedItem.UnsupportedMetadataCode));
                        continue;
                    }
                    if (!edges.Contains(edge)) { edges.Add(edge); }
                    if (visited.Add(next))
                    {
                        nodes.Add(matches[0]);
                        if (recursive) { pending.Enqueue(next); }
                    }
                }
            }

            var result = new ControlFlowGraph(nodes, edges, unsupported);
            return new InspectionResult<ControlFlowGraph>(new[] { result }, unsupported);
        }
    }
}