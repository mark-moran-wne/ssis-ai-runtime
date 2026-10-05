using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SsisAiRuntime.Inspectors
{
    public sealed class SemanticHandleCatalogBuilder
    {
        public SemanticHandleCatalog Build(
            PackageOverview package,
            IEnumerable<ConnectionOverview> connections,
            IEnumerable<VariableOverview> variables,
            IEnumerable<ParameterOverview> parameters,
            IEnumerable<ExecutableOverview> executables,
            IEnumerable<DataFlowOverview> dataFlows)
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

            if (executables == null)
            {
                throw new ArgumentNullException(nameof(executables));
            }

            if (dataFlows == null)
            {
                throw new ArgumentNullException(nameof(dataFlows));
            }

            var entries = new List<SemanticObjectReference>();
            var ordinals = new Dictionary<string, int>(StringComparer.Ordinal);
            var nativeHandles = new Dictionary<string, List<SemanticHandle>>(StringComparer.Ordinal);
            var packagePath = AddEntry(
                package.SessionId,
                entries,
                ordinals,
                SemanticObjectKind.Package,
                "package",
                package.PackageName,
                package.PackageType,
                "ssis");

            foreach (var connection in connections)
            {
                AddEntry(package.SessionId, entries, ordinals, SemanticObjectKind.Connection, "connection", connection.Name, connection.CreationName, packagePath);
                AddNativeHandle(nativeHandles, SemanticObjectKind.Connection, connection.Id, entries[entries.Count - 1].Handle);
            }

            foreach (var variable in variables)
            {
                var namespacePath = Append(packagePath, "namespace", variable.Namespace, 0);
                AddEntry(package.SessionId, entries, ordinals, SemanticObjectKind.Variable, "variable", variable.Name, variable.DataType, namespacePath);
            }

            foreach (var parameter in parameters)
            {
                AddEntry(package.SessionId, entries, ordinals, SemanticObjectKind.Parameter, "parameter", parameter.Name, parameter.CreationName, packagePath);
                AddNativeHandle(nativeHandles, SemanticObjectKind.Parameter, parameter.Id, entries[entries.Count - 1].Handle);
            }

            var executablePaths = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var executable in executables.OrderBy(item => item.Depth))
            {
                var parentPath = packagePath + "/executables";
                if (!string.IsNullOrEmpty(executable.ParentId)
                    && executablePaths.TryGetValue(executable.ParentId, out var knownParentPath))
                {
                    parentPath = knownParentPath;
                }

                var executablePath = AddEntry(
                    package.SessionId,
                    entries,
                    ordinals,
                    SemanticObjectKind.Executable,
                    executable.IsContainer ? "container" : "task",
                    executable.Name,
                    executable.CreationName,
                    parentPath);
                AddNativeHandle(nativeHandles, SemanticObjectKind.Executable, executable.Id, entries[entries.Count - 1].Handle);

                if (!string.IsNullOrEmpty(executable.Id))
                {
                    executablePaths[executable.Id] = executablePath;
                }
            }

            foreach (var dataFlow in dataFlows)
            {
                var parentPath = executablePaths.TryGetValue(dataFlow.ExecutableId, out var executablePath)
                    ? executablePath
                    : packagePath + "/data-flows";
                var dataFlowPath = AddEntry(
                    package.SessionId,
                    entries,
                    ordinals,
                    SemanticObjectKind.DataFlow,
                    "data-flow",
                    dataFlow.ExecutableName,
                    "SSIS.Pipeline",
                    parentPath);
                var componentPaths = new Dictionary<string, string>(StringComparer.Ordinal);

                foreach (var component in dataFlow.Components)
                {
                    var componentPath = AddEntry(
                        package.SessionId,
                        entries,
                        ordinals,
                        SemanticObjectKind.DataFlowComponent,
                        "component",
                        component.Name,
                        component.ComponentClassId,
                        dataFlowPath);
                    AddNativeHandle(nativeHandles, SemanticObjectKind.DataFlowComponent, component.Id, entries[entries.Count - 1].Handle);
                    if (!string.IsNullOrEmpty(component.Id))
                    {
                        componentPaths[component.Id] = componentPath;
                    }

                    AddColumns(package.SessionId, entries, ordinals, componentPath, component.InputColumns, SemanticObjectKind.InputColumn, "input-column");
                    AddColumns(package.SessionId, entries, ordinals, componentPath, component.OutputColumns, SemanticObjectKind.OutputColumn, "output-column");
                    AddColumns(package.SessionId, entries, ordinals, componentPath, component.ExternalMetadataColumns, SemanticObjectKind.ExternalMetadataColumn, "external-column");

                    foreach (var runtimeConnection in component.RuntimeConnections)
                    {
                        AddEntry(
                            package.SessionId,
                            entries,
                            ordinals,
                            SemanticObjectKind.DataFlowRuntimeConnection,
                            "runtime-connection",
                            runtimeConnection.Name,
                            runtimeConnection.ConnectionManagerId,
                            componentPath);
                    }
                }

                foreach (var path in dataFlow.Paths)
                {
                    AddEntry(
                        package.SessionId,
                        entries,
                        ordinals,
                        SemanticObjectKind.DataFlowPath,
                        "path",
                        path.Name,
                        string.Empty,
                        dataFlowPath);
                    AddNativeHandle(nativeHandles, SemanticObjectKind.DataFlowPath, path.Id, entries[entries.Count - 1].Handle);
                }
            }

            return new SemanticHandleCatalog(package.SessionId, entries, nativeHandles);
        }

        private static void AddColumns(
            Guid sessionId,
            ICollection<SemanticObjectReference> entries,
            IDictionary<string, int> ordinals,
            string parentPath,
            IEnumerable<DataFlowColumnOverview> columns,
            SemanticObjectKind kind,
            string segment)
        {
            foreach (var column in columns)
            {
                var portPath = Append(parentPath, column.Direction, column.PortName, 0);
                AddEntry(sessionId, entries, ordinals, kind, segment, column.Name, column.DataType, portPath);
            }
        }

        private static void AddNativeHandle(
            IDictionary<string, List<SemanticHandle>> nativeHandles,
            SemanticObjectKind kind,
            string nativeId,
            SemanticHandle handle)
        {
            if (nativeHandles == null || string.IsNullOrWhiteSpace(nativeId))
            {
                return;
            }

            var key = SemanticHandleCatalog.GetNativeIdKey(kind, nativeId);
            if (!nativeHandles.TryGetValue(key, out var handles))
            {
                handles = new List<SemanticHandle>();
                nativeHandles.Add(key, handles);
            }

            handles.Add(handle);
        }

        private static string AddEntry(
            Guid sessionId,
            ICollection<SemanticObjectReference> entries,
            IDictionary<string, int> ordinals,
            SemanticObjectKind kind,
            string segment,
            string name,
            string creationName,
            string parentPath)
        {
            var safeName = name ?? string.Empty;
            var ordinalKey = parentPath + "|" + kind.ToString() + "|" + safeName;
            ordinals.TryGetValue(ordinalKey, out var ordinal);
            ordinals[ordinalKey] = ordinal + 1;
            var path = Append(parentPath, segment, safeName, ordinal);
            var value = "ssis://" + sessionId.ToString("N", CultureInfo.InvariantCulture) + "/" + path;
            var handle = new SemanticHandle(sessionId, kind, value);
            entries.Add(new SemanticObjectReference(handle, safeName, creationName));
            return path;
        }

        private static string Append(string parentPath, string segment, string name, int ordinal)
        {
            return parentPath
                + "/" + Uri.EscapeDataString(segment ?? string.Empty)
                + "/" + Uri.EscapeDataString(name ?? string.Empty)
                + "[" + ordinal.ToString(CultureInfo.InvariantCulture) + "]";
        }
    }
}