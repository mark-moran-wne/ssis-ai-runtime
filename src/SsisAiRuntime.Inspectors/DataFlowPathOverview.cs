namespace SsisAiRuntime.Inspectors
{
    public sealed class DataFlowPathOverview
    {
        public DataFlowPathOverview(
            string id,
            string name,
            string sourceComponentId,
            string sourceOutputId,
            string targetComponentId,
            string targetInputId)
        {
            Id = id ?? string.Empty;
            Name = name ?? string.Empty;
            SourceComponentId = sourceComponentId ?? string.Empty;
            SourceOutputId = sourceOutputId ?? string.Empty;
            TargetComponentId = targetComponentId ?? string.Empty;
            TargetInputId = targetInputId ?? string.Empty;
        }

        public string Id { get; }

        public string Name { get; }

        public string SourceComponentId { get; }

        public string SourceOutputId { get; }

        public string TargetComponentId { get; }

        public string TargetInputId { get; }
    }
}