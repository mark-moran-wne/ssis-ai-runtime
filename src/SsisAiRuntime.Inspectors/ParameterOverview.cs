namespace SsisAiRuntime.Inspectors
{
    public sealed class ParameterOverview
    {
        public ParameterOverview(
            string id,
            string name,
            string creationName,
            string description,
            string dataType,
            bool isRequired,
            bool isSensitive)
        {
            Id = id ?? string.Empty;
            Name = name ?? string.Empty;
            CreationName = creationName ?? string.Empty;
            Description = description ?? string.Empty;
            DataType = dataType ?? string.Empty;
            IsRequired = isRequired;
            IsSensitive = isSensitive;
        }

        public string Id { get; }

        public string Name { get; }

        public string CreationName { get; }

        public string Description { get; }

        public string DataType { get; }

        public bool IsRequired { get; }

        public bool IsSensitive { get; }

        public bool ValueOmitted => true;
    }
}