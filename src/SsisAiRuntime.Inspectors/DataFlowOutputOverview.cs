namespace SsisAiRuntime.Inspectors
{
    public sealed class DataFlowOutputOverview
    {
        public DataFlowOutputOverview(string id, string name, string synchronousInputId, bool isErrorOutput)
        {
            Id = id ?? string.Empty;
            Name = name ?? string.Empty;
            SynchronousInputId = synchronousInputId ?? string.Empty;
            IsErrorOutput = isErrorOutput;
        }

        public string Id { get; }
        public string Name { get; }
        public string SynchronousInputId { get; }
        public bool IsErrorOutput { get; }
    }
}