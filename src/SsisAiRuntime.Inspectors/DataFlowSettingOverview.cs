namespace SsisAiRuntime.Inspectors
{
    public sealed class DataFlowSettingOverview
    {
        public DataFlowSettingOverview(string ownerType, string ownerId, string name, string value, bool valueRedacted)
        {
            OwnerType = ownerType ?? string.Empty;
            OwnerId = ownerId ?? string.Empty;
            Name = name ?? string.Empty;
            Value = value ?? string.Empty;
            ValueRedacted = valueRedacted;
        }

        public string OwnerType { get; }

        public string OwnerId { get; }

        public string Name { get; }

        public string Value { get; }

        public bool ValueRedacted { get; }
    }
}