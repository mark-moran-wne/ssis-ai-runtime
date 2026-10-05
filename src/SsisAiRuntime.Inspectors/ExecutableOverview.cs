namespace SsisAiRuntime.Inspectors
{
    public sealed class ExecutableOverview
    {
        public ExecutableOverview(
            string id,
            string parentId,
            string name,
            string creationName,
            string description,
            int depth,
            bool isContainer,
            bool hasExpressions)
        {
            Id = id ?? string.Empty;
            ParentId = parentId ?? string.Empty;
            Name = name ?? string.Empty;
            CreationName = creationName ?? string.Empty;
            Description = description ?? string.Empty;
            Depth = depth;
            IsContainer = isContainer;
            HasExpressions = hasExpressions;
        }

        public string Id { get; }

        public string ParentId { get; }

        public string Name { get; }

        public string CreationName { get; }

        public string Description { get; }

        public int Depth { get; }

        public bool IsContainer { get; }

        public bool HasExpressions { get; }
    }
}