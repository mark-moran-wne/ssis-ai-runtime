namespace SsisAiRuntime.Inspectors
{
    public sealed class SqlStatementOverview
    {
        public SqlStatementOverview(
            string taskId,
            string taskName,
            string creationName,
            string statementSourceType,
            string statementText,
            bool statementTextRedacted,
            string connectionManagerId,
            string connectionManagerName,
            int? parameterBindingCount)
        {
            TaskId = taskId ?? string.Empty;
            TaskName = taskName ?? string.Empty;
            CreationName = creationName ?? string.Empty;
            StatementSourceType = statementSourceType ?? string.Empty;
            StatementText = statementText ?? string.Empty;
            StatementTextRedacted = statementTextRedacted;
            ConnectionManagerId = connectionManagerId ?? string.Empty;
            ConnectionManagerName = connectionManagerName ?? string.Empty;
            ParameterBindingCount = parameterBindingCount;
        }

        public string TaskId { get; }

        public string TaskName { get; }

        public string CreationName { get; }

        public string StatementSourceType { get; }

        public string StatementText { get; }

        public bool StatementTextRedacted { get; }

        public string ConnectionManagerId { get; }

        public string ConnectionManagerName { get; }

        public int? ParameterBindingCount { get; }
    }
}