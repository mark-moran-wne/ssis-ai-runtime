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
            bool hasExpression,
            string nativeId = "",
            string scope = "Unknown",
            string sensitivityClassification = "Unclassified",
            string scopeNativeId = "")
        {
            NativeId = nativeId ?? string.Empty;
            Scope = string.IsNullOrWhiteSpace(scope) ? "Unknown" : scope;
            SensitivityClassification = string.IsNullOrWhiteSpace(sensitivityClassification)
                ? "Unclassified" : sensitivityClassification;
            ScopeNativeId = scopeNativeId ?? string.Empty;
            Name = name ?? string.Empty;
            Namespace = @namespace ?? string.Empty;
            DataType = dataType ?? string.Empty;
            IsReadOnly = isReadOnly;
            IsSystemVariable = isSystemVariable;
            EvaluatesAsExpression = evaluatesAsExpression;
            HasExpression = hasExpression;
        }

        public string NativeId { get; }

        public string Scope { get; }

        public string SensitivityClassification { get; }

        public string ScopeNativeId { get; }

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