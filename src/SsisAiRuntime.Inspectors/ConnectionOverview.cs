namespace SsisAiRuntime.Inspectors
{
    public sealed class ConnectionOverview
    {
        public ConnectionOverview(string name, string id, string creationName)
        {
            Name = name ?? string.Empty;
            Id = id ?? string.Empty;
            CreationName = creationName ?? string.Empty;
        }

        public string Name { get; }

        public string Id { get; }

        public string CreationName { get; }

        public bool ConnectionStringOmitted => true;
    }
}