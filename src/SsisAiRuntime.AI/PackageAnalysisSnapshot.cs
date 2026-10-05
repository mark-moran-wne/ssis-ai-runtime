using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using SsisAiRuntime.Inspectors;

namespace SsisAiRuntime.AI
{
    public sealed class PackageAnalysisSnapshot
    {
        public PackageAnalysisSnapshot(PackageOverview package, IEnumerable<ConnectionOverview> connections,
            IEnumerable<VariableOverview> variables, IEnumerable<ParameterOverview> parameters,
            IEnumerable<ExecutableOverview> executables, IEnumerable<SqlStatementOverview> sqlStatements,
            IEnumerable<DataFlowOverview> dataFlows, IEnumerable<ExpressionOverview> expressions,
            ControlFlowGraph controlFlow, SemanticHandleCatalog catalog, IEnumerable<UnsupportedItem> unsupportedItems)
        {
            Package = package ?? throw new ArgumentNullException(nameof(package));
            Connections = ReadOnly(connections, nameof(connections));
            Variables = ReadOnly(variables, nameof(variables));
            Parameters = ReadOnly(parameters, nameof(parameters));
            Executables = ReadOnly(executables, nameof(executables));
            SqlStatements = ReadOnly(sqlStatements, nameof(sqlStatements));
            DataFlows = ReadOnly(dataFlows, nameof(dataFlows));
            Expressions = ReadOnly(expressions, nameof(expressions));
            ControlFlow = controlFlow ?? throw new ArgumentNullException(nameof(controlFlow));
            Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            UnsupportedItems = ReadOnly(unsupportedItems, nameof(unsupportedItems));
            if (catalog.SessionId != package.SessionId) throw new ArgumentException("The catalog must belong to the package session.", nameof(catalog));
        }

        public PackageOverview Package { get; }
        public IReadOnlyList<ConnectionOverview> Connections { get; }
        public IReadOnlyList<VariableOverview> Variables { get; }
        public IReadOnlyList<ParameterOverview> Parameters { get; }
        public IReadOnlyList<ExecutableOverview> Executables { get; }
        public IReadOnlyList<SqlStatementOverview> SqlStatements { get; }
        public IReadOnlyList<DataFlowOverview> DataFlows { get; }
        public IReadOnlyList<ExpressionOverview> Expressions { get; }
        public ControlFlowGraph ControlFlow { get; }
        public SemanticHandleCatalog Catalog { get; }
        public IReadOnlyList<UnsupportedItem> UnsupportedItems { get; }
        public bool IsComplete => UnsupportedItems.Count == 0;

        private static IReadOnlyList<T> ReadOnly<T>(IEnumerable<T> values, string name)
        {
            if (values == null) throw new ArgumentNullException(name);
            return new ReadOnlyCollection<T>(new List<T>(values));
        }
    }
}