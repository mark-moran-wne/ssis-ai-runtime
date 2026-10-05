namespace SsisAiRuntime.Inspectors
{
    public sealed class PrecedenceConstraintOverview
    {
        public PrecedenceConstraintOverview(
            string id,
            string name,
            string fromExecutableId,
            string fromExecutableName,
            string toExecutableId,
            string toExecutableName,
            string evaluationOperation,
            string constraintValue,
            bool logicalAnd,
            bool hasExpression)
        {
            Id = id ?? string.Empty;
            Name = name ?? string.Empty;
            FromExecutableId = fromExecutableId ?? string.Empty;
            FromExecutableName = fromExecutableName ?? string.Empty;
            ToExecutableId = toExecutableId ?? string.Empty;
            ToExecutableName = toExecutableName ?? string.Empty;
            EvaluationOperation = evaluationOperation ?? string.Empty;
            ConstraintValue = constraintValue ?? string.Empty;
            LogicalAnd = logicalAnd;
            HasExpression = hasExpression;
        }

        public string Id { get; }

        public string Name { get; }

        public string FromExecutableId { get; }

        public string FromExecutableName { get; }

        public string ToExecutableId { get; }

        public string ToExecutableName { get; }

        public string EvaluationOperation { get; }

        public string ConstraintValue { get; }

        public bool LogicalAnd { get; }

        public bool HasExpression { get; }

        public bool ExpressionTextOmitted => HasExpression;
    }
}