using System;
using System.Collections.Generic;

namespace SsisAiRuntime.Inspectors
{
    public sealed class PackageContextBuilder
    {
        public PackageContext BuildSql(
            PackageOverview package,
            InspectionResult<SqlStatementOverview> sql)
        {
            if (package == null)
            {
                throw new ArgumentNullException(nameof(package));
            }

            if (sql == null)
            {
                throw new ArgumentNullException(nameof(sql));
            }

            return Create(
                PackageContextKind.Sql,
                package,
                sqlStatements: sql.Items,
                unsupportedItems: sql.UnsupportedItems);
        }

        public PackageContext BuildLineage(
            PackageOverview package,
            InspectionResult<DataFlowOverview> dataFlows)
        {
            if (package == null)
            {
                throw new ArgumentNullException(nameof(package));
            }

            if (dataFlows == null)
            {
                throw new ArgumentNullException(nameof(dataFlows));
            }

            return Create(
                PackageContextKind.Lineage,
                package,
                dataFlows: dataFlows.Items,
                unsupportedItems: dataFlows.UnsupportedItems);
        }

        public PackageContext BuildConfiguration(
            PackageOverview package,
            InspectionResult<ConnectionOverview> connections,
            InspectionResult<VariableOverview> variables,
            InspectionResult<ParameterOverview> parameters,
            InspectionResult<ExpressionOverview> expressions)
        {
            if (package == null)
            {
                throw new ArgumentNullException(nameof(package));
            }

            if (connections == null)
            {
                throw new ArgumentNullException(nameof(connections));
            }

            if (variables == null)
            {
                throw new ArgumentNullException(nameof(variables));
            }

            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            if (expressions == null)
            {
                throw new ArgumentNullException(nameof(expressions));
            }

            var unsupportedItems = new List<UnsupportedItem>();
            unsupportedItems.AddRange(connections.UnsupportedItems);
            unsupportedItems.AddRange(variables.UnsupportedItems);
            unsupportedItems.AddRange(parameters.UnsupportedItems);
            unsupportedItems.AddRange(expressions.UnsupportedItems);

            return Create(
                PackageContextKind.Configuration,
                package,
                connections: connections.Items,
                variables: variables.Items,
                parameters: parameters.Items,
                expressions: expressions.Items,
                unsupportedItems: unsupportedItems);
        }

        private static PackageContext Create(
            PackageContextKind kind,
            PackageOverview package,
            IEnumerable<SqlStatementOverview> sqlStatements = null,
            IEnumerable<DataFlowOverview> dataFlows = null,
            IEnumerable<ConnectionOverview> connections = null,
            IEnumerable<VariableOverview> variables = null,
            IEnumerable<ParameterOverview> parameters = null,
            IEnumerable<ExpressionOverview> expressions = null,
            IEnumerable<UnsupportedItem> unsupportedItems = null)
        {
            return new PackageContext(
                kind,
                package,
                sqlStatements ?? Array.Empty<SqlStatementOverview>(),
                dataFlows ?? Array.Empty<DataFlowOverview>(),
                connections ?? Array.Empty<ConnectionOverview>(),
                variables ?? Array.Empty<VariableOverview>(),
                parameters ?? Array.Empty<ParameterOverview>(),
                expressions ?? Array.Empty<ExpressionOverview>(),
                unsupportedItems ?? Array.Empty<UnsupportedItem>());
        }
    }
}