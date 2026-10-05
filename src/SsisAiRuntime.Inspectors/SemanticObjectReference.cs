namespace SsisAiRuntime.Inspectors
{
    public sealed class SemanticObjectReference
    {
        public SemanticObjectReference(SemanticHandle handle, string name, string creationName)
        {
            Handle = handle;
            Name = name ?? string.Empty;
            CreationName = creationName ?? string.Empty;
        }

        public SemanticHandle Handle { get; }

        public string Name { get; }

        public string CreationName { get; }
    }
}