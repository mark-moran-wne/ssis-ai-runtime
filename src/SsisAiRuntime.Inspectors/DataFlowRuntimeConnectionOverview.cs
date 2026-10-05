namespace SsisAiRuntime.Inspectors
{
    public sealed class DataFlowRuntimeConnectionOverview
    {
        public DataFlowRuntimeConnectionOverview(string id, string name, string connectionManagerId)
        {
            Id = id ?? string.Empty;
            Name = name ?? string.Empty;
            ConnectionManagerId = connectionManagerId ?? string.Empty;
        }

        public string Id { get; }

        public string Name { get; }

        public string ConnectionManagerId { get; }
    }
}