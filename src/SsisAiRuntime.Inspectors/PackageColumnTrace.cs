using System;

namespace SsisAiRuntime.Inspectors
{
    public sealed class PackageColumnTrace
    {
        public PackageColumnTrace(PackageOverview package, ColumnLineageTrace trace)
        {
            Package = package ?? throw new ArgumentNullException(nameof(package));
            Trace = trace ?? throw new ArgumentNullException(nameof(trace));
        }

        public PackageOverview Package { get; }
        public ColumnLineageTrace Trace { get; }
    }
}