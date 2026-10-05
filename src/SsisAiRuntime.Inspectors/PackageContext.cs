using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SsisAiRuntime.Inspectors
{
    public sealed class PackageContext
    {
        public PackageContext(
            PackageContextKind kind,
            PackageOverview package,
            IEnumerable<SqlStatementOverview> sqlStatements,
            IEnumerable<DataFlowOverview> dataFlows,
            IEnumerable<ConnectionOverview> connections,
            IEnumerable<VariableOverview> variables,
            IEnumerable<ParameterOverview> parameters,
            IEnumerable<ExpressionOverview> expressions,
            IEnumerable<UnsupportedItem> unsupportedItems)
        {
            Kind = kind;
            Package = package;
            SqlStatements = new ReadOnlyCollection<SqlStatementOverview>(new List<SqlStatementOverview>(sqlStatements));
            DataFlows = new ReadOnlyCollection<DataFlowOverview>(new List<DataFlowOverview>(dataFlows));
            Connections = new ReadOnlyCollection<ConnectionOverview>(new List<ConnectionOverview>(connections));
            Variables = new ReadOnlyCollection<VariableOverview>(new List<VariableOverview>(variables));
            Parameters = new ReadOnlyCollection<ParameterOverview>(new List<ParameterOverview>(parameters));
            Expressions = new ReadOnlyCollection<ExpressionOverview>(new List<ExpressionOverview>(expressions));
            UnsupportedItems = new ReadOnlyCollection<UnsupportedItem>(new List<UnsupportedItem>(unsupportedItems));
        }

        public PackageContextKind Kind { get; }

        public PackageOverview Package { get; }

        public IReadOnlyList<SqlStatementOverview> SqlStatements { get; }

        public IReadOnlyList<DataFlowOverview> DataFlows { get; }

        public IReadOnlyList<ConnectionOverview> Connections { get; }

        public IReadOnlyList<VariableOverview> Variables { get; }

        public IReadOnlyList<ParameterOverview> Parameters { get; }

        public IReadOnlyList<ExpressionOverview> Expressions { get; }

        public IReadOnlyList<UnsupportedItem> UnsupportedItems { get; }

        public bool IsComplete => UnsupportedItems.Count == 0;
    }
}