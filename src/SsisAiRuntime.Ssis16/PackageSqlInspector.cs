using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Data.Common;
using SsisAiRuntime.Core;
using SsisAiRuntime.Inspectors;
using SsisAiRuntime.Inspectors.SqlDependencies;
using DtsRuntime = Microsoft.SqlServer.Dts.Runtime;

namespace SsisAiRuntime.Ssis16
{
    public sealed class PackageSqlInspector : IPackageSqlInspector<DtsRuntime.Package>
    {
        public IReadOnlyList<SqlStatementOverview> Inspect(PackageSession<DtsRuntime.Package> session)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            var statements = new List<SqlStatementOverview>();
            AddStatements(session.Package, statements);
            return new ReadOnlyCollection<SqlStatementOverview>(statements);
        }

        public InspectionResult<SqlStatementOverview> InspectDetailed(PackageSession<DtsRuntime.Package> session) => InspectDetailed(session, false);

        public InspectionResult<SqlStatementOverview> InspectDetailed(PackageSession<DtsRuntime.Package> session, bool includeSanitizedText)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            var statements = new List<SqlStatementOverview>();
            var unsupportedItems = new List<UnsupportedItem>();
            AddStatements(session.Package, session.Package.Executables, statements, unsupportedItems, includeSanitizedText);
            return new InspectionResult<SqlStatementOverview>(statements, unsupportedItems);
        }

        private static void AddStatements(
            DtsRuntime.Package package,
            ICollection<SqlStatementOverview> statements)
        {
            AddStatements(package, package.Executables, statements, null);
        }

        private static void AddStatements(
            DtsRuntime.Package package,
            DtsRuntime.Executables executables,
            ICollection<SqlStatementOverview> statements,
            ICollection<UnsupportedItem> unsupportedItems, bool includeSanitizedText = false)
        {
            foreach (DtsRuntime.Executable executable in executables)
            {
                if (executable is DtsRuntime.IDTSSequence sequence)
                {
                    AddStatements(package, sequence.Executables, statements, unsupportedItems, includeSanitizedText);
                    continue;
                }

                if (!(executable is DtsRuntime.TaskHost taskHost)
                    || taskHost.CreationName.IndexOf("ExecuteSQLTask", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                var task = taskHost.InnerObject;
                var taskType = task == null ? null : task.GetType();
                var statementProperty = taskType == null ? null : taskType.GetProperty("SqlStatementSource");
                if (statementProperty == null)
                {
                    unsupportedItems?.Add(new UnsupportedItem(
                        taskHost.ID,
                        taskHost.Name,
                        taskHost.CreationName,
                        "The SQL task statement source is not exposed by its runtime object.",
                        UnsupportedItem.UnsupportedMetadataCode));
                    continue;
                }

                var statement = statementProperty.GetValue(task, null) as string ?? string.Empty;
                var sanitized = SqlTextSanitizer.Sanitize(statement);
                var sourceTypeProperty = taskType.GetProperty("SqlStatementSourceType");
                var sourceType = sourceTypeProperty == null
                    ? string.Empty
                    : sourceTypeProperty.GetValue(task, null)?.ToString() ?? string.Empty;
                var bindingProperty = taskType.GetProperty("ParameterBindings");
                var bindings = bindingProperty == null ? null : bindingProperty.GetValue(task, null);
                var bindingCount = CountItems(bindings);
                var connectionReference = taskType.GetProperty("Connection")?.GetValue(task, null) as string;
                var connectionManager = FindConnection(package, connectionReference);
                var directInput = string.Equals(sourceType, "DirectInput", StringComparison.OrdinalIgnoreCase) &&
                    string.IsNullOrWhiteSpace(taskHost.GetExpression("SqlStatementSource"));
                var dependencies = directInput
                    ? new SqlDependencyAnalyzer().Analyze(statement, ResolveDialect(connectionManager))
                    : new SqlDependencyResolution(Array.Empty<SqlObjectReference>(), new[] { "sql.text_unavailable" }, false);
                foreach (var code in dependencies.CoverageCodes) { unsupportedItems?.Add(SqlDependencyCoverage.Gap(taskHost.ID, code)); }
                if (connectionManager == null)
                {
                    unsupportedItems?.Add(new UnsupportedItem(
                        taskHost.ID,
                        taskHost.Name,
                        taskHost.CreationName,
                        "The SQL task connection reference could not be resolved to a package connection.",
                        UnsupportedItem.ReadFailureCode));
                }

                if (bindings != null && !bindingCount.HasValue)
                {
                    unsupportedItems?.Add(new UnsupportedItem(
                        taskHost.ID,
                        taskHost.Name,
                        taskHost.CreationName,
                        "The SQL task parameter binding count could not be read.",
                        UnsupportedItem.ReadFailureCode));
                }

                statements.Add(new SqlStatementOverview(
                    taskHost.ID,
                    taskHost.Name,
                    taskHost.CreationName,
                    sourceType,
                    sanitized.Text,
                    sanitized.Redacted,
                    connectionManager == null ? string.Empty : connectionManager.ID,
                    connectionManager == null ? string.Empty : connectionManager.Name,
                    bindingCount,
                    dependencies,
                    includeSanitizedText ? SanitizedTextFactory.Sql(taskHost.ID, connectionManager?.ID,
                        directInput ? statement : null, ResolveDialect(connectionManager)) : null));
            }
        }

        private static DtsRuntime.ConnectionManager FindConnection(DtsRuntime.Package package, string connectionReference)
        {
            if (string.IsNullOrWhiteSpace(connectionReference))
            {
                return null;
            }

            var matches = package.Connections.Cast<DtsRuntime.ConnectionManager>().Where(connection =>
                string.Equals(connection.ID, connectionReference, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(connection.Name, connectionReference, StringComparison.OrdinalIgnoreCase)).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }

        private static SqlDialect ResolveDialect(DtsRuntime.ConnectionManager connection)
        {
            if (connection == null) { return SqlDialect.Unsupported; }
            if (connection.CreationName.IndexOf("SqlClient.SqlConnection", StringComparison.OrdinalIgnoreCase) >= 0) { return SqlDialect.TSql160; }
            if (!connection.CreationName.Equals("OLEDB", StringComparison.OrdinalIgnoreCase)) { return SqlDialect.Unsupported; }
            try
            {
                var property = connection.Properties.Cast<DtsRuntime.DtsProperty>().SingleOrDefault(item => item.Name.Equals("Provider", StringComparison.OrdinalIgnoreCase));
                var provider = property?.GetValue(connection) as string ?? string.Empty;
                if (provider.Length == 0)
                {
                    var builder = new DbConnectionStringBuilder { ConnectionString = connection.ConnectionString };
                    provider = builder.TryGetValue("Provider", out var value) ? value as string ?? string.Empty : string.Empty;
                }
                return provider.StartsWith("SQLNCLI", StringComparison.OrdinalIgnoreCase) || provider.Equals("SQLOLEDB", StringComparison.OrdinalIgnoreCase) ||
                    provider.Equals("SQLOLEDB.1", StringComparison.OrdinalIgnoreCase) ||
                    provider.StartsWith("MSOLEDBSQL", StringComparison.OrdinalIgnoreCase) ? SqlDialect.TSql160 : SqlDialect.Unsupported;
            }
            catch { return SqlDialect.Unsupported; }
        }

        private static int? CountItems(object collection)
        {
            if (!(collection is IEnumerable items))
            {
                return null;
            }

            var count = 0;
            foreach (var item in items)
            {
                count++;
            }

            return count;
        }
    }
}