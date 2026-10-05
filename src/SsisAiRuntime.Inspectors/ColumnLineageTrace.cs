using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SsisAiRuntime.Inspectors
{
    public sealed class ColumnLineageTrace
    {
        public ColumnLineageTrace(string flowId, string flowName, string direction,
            IEnumerable<DataFlowColumnOverview> columns, IEnumerable<ColumnLineageLink> links)
        {
            FlowId = flowId;
            FlowName = flowName;
            Direction = direction;
            Columns = new ReadOnlyCollection<DataFlowColumnOverview>(new List<DataFlowColumnOverview>(columns));
            Links = new ReadOnlyCollection<ColumnLineageLink>(new List<ColumnLineageLink>(links));
        }

        public string FlowId { get; }
        public string FlowName { get; }
        public string Direction { get; }
        public IReadOnlyList<DataFlowColumnOverview> Columns { get; }
        public IReadOnlyList<ColumnLineageLink> Links { get; }
    }
}