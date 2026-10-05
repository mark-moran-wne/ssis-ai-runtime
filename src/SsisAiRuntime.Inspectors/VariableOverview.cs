namespace SsisAiRuntime.Inspectors
{
    public sealed class VariableOverview
    {
        public VariableOverview(
            string name,
            string @namespace,
            string dataType,
            bool isReadOnly,
            bool isSystemVariable,
            bool evaluatesAsExpression,
            bool hasExpression)
        {
            Name = name ?? string.Empty;
            Namespace = @namespace ?? string.Empty;
            DataType = dataType ?? string.Empty;
            IsReadOnly = isReadOnly;
            IsSystemVariable = isSystemVariable;
            EvaluatesAsExpression = evaluatesAsExpression;
            HasExpression = hasExpression;
        }

        public string Name { get; }

        public string Namespace { get; }

        public string DataType { get; }

        public bool IsReadOnly { get; }

        public bool IsSystemVariable { get; }

        public bool EvaluatesAsExpression { get; }

        public bool HasExpression { get; }

        public bool ValueOmitted => true;

        public bool ExpressionOmitted => HasExpression;
    }
}