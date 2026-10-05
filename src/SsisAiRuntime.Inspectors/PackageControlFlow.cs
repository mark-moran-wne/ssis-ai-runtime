using System;

namespace SsisAiRuntime.Inspectors
{
    public sealed class PackageControlFlow
    {
        public PackageControlFlow(PackageOverview package, ControlFlowGraph graph)
        {
            Package = package ?? throw new ArgumentNullException(nameof(package));
            Graph = graph ?? throw new ArgumentNullException(nameof(graph));
        }

        public PackageOverview Package { get; }
        public ControlFlowGraph Graph { get; }
    }
}