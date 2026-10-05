namespace SsisAiRuntime.Inspectors
{
    public sealed class UnsupportedItem
    {
        public UnsupportedItem(string id, string name, string creationName, string reason)
        {
            Id = id ?? string.Empty;
            Name = name ?? string.Empty;
            CreationName = creationName ?? string.Empty;
            Reason = reason ?? string.Empty;
        }

        public string Id { get; }

        public string Name { get; }

        public string CreationName { get; }

        public string Reason { get; }
    }
}