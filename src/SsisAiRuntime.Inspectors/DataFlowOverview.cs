using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SsisAiRuntime.Inspectors
{
    public sealed class DataFlowOverview
    {
        public DataFlowOverview(
            string executableId,
            string executableName,
            IEnumerable<DataFlowComponentOverview> components,
            IEnumerable<DataFlowPathOverview> paths)
        {
            ExecutableId = executableId ?? string.Empty;
            ExecutableName = executableName ?? string.Empty;
            Components = new ReadOnlyCollection<DataFlowComponentOverview>(new List<DataFlowComponentOverview>(components));
            Paths = new ReadOnlyCollection<DataFlowPathOverview>(new List<DataFlowPathOverview>(paths));
        }

        public string ExecutableId { get; }

        public string ExecutableName { get; }

        public IReadOnlyList<DataFlowComponentOverview> Components { get; }

        public IReadOnlyList<DataFlowPathOverview> Paths { get; }
    }
}