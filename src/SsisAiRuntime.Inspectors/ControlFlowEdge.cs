namespace SsisAiRuntime.Inspectors
{
    public sealed class ControlFlowEdge
    {
        public ControlFlowEdge(
            SemanticHandle from,
            SemanticHandle to,
            ControlFlowEdgeKind kind,
            string constraintName,
            string evaluationOperation,
            string constraintValue,
            bool? logicalAnd)
        {
            From = from;
            To = to;
            Kind = kind;
            ConstraintName = constraintName ?? string.Empty;
            EvaluationOperation = evaluationOperation ?? string.Empty;
            ConstraintValue = constraintValue ?? string.Empty;
            LogicalAnd = logicalAnd;
        }

        public SemanticHandle From { get; }

        public SemanticHandle To { get; }

        public ControlFlowEdgeKind Kind { get; }

        public string ConstraintName { get; }

        public string EvaluationOperation { get; }

        public string ConstraintValue { get; }

        public bool? LogicalAnd { get; }
    }
}