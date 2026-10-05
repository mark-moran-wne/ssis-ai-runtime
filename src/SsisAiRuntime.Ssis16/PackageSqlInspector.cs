using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using SsisAiRuntime.Core;
using SsisAiRuntime.Inspectors;
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

        public InspectionResult<SqlStatementOverview> InspectDetailed(PackageSession<DtsRuntime.Package> session)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            var statements = new List<SqlStatementOverview>();
            var unsupportedItems = new List<UnsupportedItem>();
            AddStatements(session.Package, session.Package.Executables, statements, unsupportedItems);
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
            ICollection<UnsupportedItem> unsupportedItems)
        {
            foreach (DtsRuntime.Executable executable in executables)
            {
                if (executable is DtsRuntime.IDTSSequence sequence)
                {
                    AddStatements(package, sequence.Executables, statements, unsupportedItems);
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
                    bindingCount));
            }
        }

        private static DtsRuntime.ConnectionManager FindConnection(DtsRuntime.Package package, string connectionReference)
        {
            if (string.IsNullOrWhiteSpace(connectionReference))
            {
                return null;
            }

            foreach (DtsRuntime.ConnectionManager connection in package.Connections)
            {
                if (string.Equals(connection.Name, connectionReference, StringComparison.OrdinalIgnoreCase))
                {
                    return connection;
                }
            }

            return null;
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