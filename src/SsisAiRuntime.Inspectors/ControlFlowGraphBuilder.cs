using System;
using System.Collections.Generic;

namespace SsisAiRuntime.Inspectors
{
    public sealed class ControlFlowGraphBuilder
    {
        public ControlFlowGraph Build(
            IEnumerable<ExecutableOverview> executables,
            InspectionResult<PrecedenceConstraintOverview> precedenceConstraints,
            SemanticHandleCatalog catalog)
        {
            if (executables == null)
            {
                throw new ArgumentNullException(nameof(executables));
            }

            if (precedenceConstraints == null)
            {
                throw new ArgumentNullException(nameof(precedenceConstraints));
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            var nodes = new List<ControlFlowNode>();
            var edges = new List<ControlFlowEdge>();
            var unsupportedItems = new List<UnsupportedItem>(precedenceConstraints.UnsupportedItems);
            var nodeHandles = new Dictionary<string, SemanticHandle>(StringComparer.Ordinal);
            var executableList = new List<ExecutableOverview>(executables);

            foreach (var executable in executableList)
            {
                var resolution = catalog.ResolveNativeId(SemanticObjectKind.Executable, executable.Id);
                if (resolution.Status != SemanticHandleResolutionStatus.Resolved)
                {
                    unsupportedItems.Add(new UnsupportedItem(
                        executable.Id,
                        executable.Name,
                        executable.CreationName,
                        "The executable could not be resolved to one semantic handle."));
                    continue;
                }

                nodeHandles[executable.Id] = resolution.ResolvedObject.Handle;
                nodes.Add(new ControlFlowNode(
                    resolution.ResolvedObject.Handle,
                    executable.Name,
                    executable.CreationName,
                    executable.Depth,
                    executable.IsContainer));
            }

            foreach (var executable in executableList)
            {
                if (string.IsNullOrEmpty(executable.ParentId))
                {
                    continue;
                }

                if (!nodeHandles.TryGetValue(executable.Id, out var childHandle)
                    || !nodeHandles.TryGetValue(executable.ParentId, out var parentHandle))
                {
                    unsupportedItems.Add(new UnsupportedItem(
                        executable.Id,
                        executable.Name,
                        executable.CreationName,
                        "A containment endpoint could not be resolved to a semantic handle."));
                    continue;
                }

                edges.Add(new ControlFlowEdge(
                    parentHandle,
                    childHandle,
                    ControlFlowEdgeKind.Containment,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    null));
            }

            foreach (var constraint in precedenceConstraints.Items)
            {
                var from = catalog.ResolveNativeId(SemanticObjectKind.Executable, constraint.FromExecutableId);
                var to = catalog.ResolveNativeId(SemanticObjectKind.Executable, constraint.ToExecutableId);
                if (from.Status != SemanticHandleResolutionStatus.Resolved
                    || to.Status != SemanticHandleResolutionStatus.Resolved)
                {
                    unsupportedItems.Add(new UnsupportedItem(
                        constraint.Id,
                        constraint.Name,
                        "PrecedenceConstraint",
                        "One or both precedence endpoints could not be resolved unambiguously."));
                    continue;
                }

                edges.Add(new ControlFlowEdge(
                    from.ResolvedObject.Handle,
                    to.ResolvedObject.Handle,
                    ControlFlowEdgeKind.Precedence,
                    constraint.Name,
                    constraint.EvaluationOperation,
                    constraint.ConstraintValue,
                    constraint.LogicalAnd));
            }

            return new ControlFlowGraph(nodes, edges, unsupportedItems);
        }
    }
}