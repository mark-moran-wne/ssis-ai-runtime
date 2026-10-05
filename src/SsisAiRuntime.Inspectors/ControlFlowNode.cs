namespace SsisAiRuntime.Inspectors
{
    public sealed class ControlFlowNode
    {
        public ControlFlowNode(SemanticHandle handle, string name, string creationName, int depth, bool isContainer)
            : this(handle, name, creationName, depth, isContainer, string.Empty)
        {
        }

        public ControlFlowNode(SemanticHandle handle, string name, string creationName, int depth, bool isContainer, string nativeId)
        {
            Handle = handle;
            Name = name ?? string.Empty;
            CreationName = creationName ?? string.Empty;
            Depth = depth;
            IsContainer = isContainer;
            NativeId = nativeId ?? string.Empty;
        }

        public SemanticHandle Handle { get; }

        public string Name { get; }

        public string CreationName { get; }

        public int Depth { get; }

        public bool IsContainer { get; }
        public string NativeId { get; }
    }
}