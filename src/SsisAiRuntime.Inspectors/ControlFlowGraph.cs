using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SsisAiRuntime.Inspectors
{
    public sealed class ControlFlowGraph
    {
        public ControlFlowGraph(
            IEnumerable<ControlFlowNode> nodes,
            IEnumerable<ControlFlowEdge> edges,
            IEnumerable<UnsupportedItem> unsupportedItems)
        {
            Nodes = new ReadOnlyCollection<ControlFlowNode>(new List<ControlFlowNode>(nodes));
            Edges = new ReadOnlyCollection<ControlFlowEdge>(new List<ControlFlowEdge>(edges));
            UnsupportedItems = new ReadOnlyCollection<UnsupportedItem>(new List<UnsupportedItem>(unsupportedItems));
        }

        public IReadOnlyList<ControlFlowNode> Nodes { get; }

        public IReadOnlyList<ControlFlowEdge> Edges { get; }

        public IReadOnlyList<UnsupportedItem> UnsupportedItems { get; }

        public bool IsComplete => UnsupportedItems.Count == 0;
    }
}