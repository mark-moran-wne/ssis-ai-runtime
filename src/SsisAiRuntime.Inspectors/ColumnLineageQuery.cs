using System;
using System.Collections.Generic;
using System.Linq;

namespace SsisAiRuntime.Inspectors
{
    public sealed class ColumnLineageQuery
    {
        public InspectionResult<ColumnLineageTrace> Trace(DataFlowOverview flow, string componentId, string columnId, bool upstream = false)
        {
            if (flow == null) { throw new ArgumentNullException(nameof(flow)); }
            var columns = flow.Components.SelectMany(component => Inputs(component).Concat(component.OutputColumns)).ToList();
            var roots = columns.Where(column => column.ComponentId == componentId && column.Id == columnId).ToList();
            if (roots.Count == 0)
            {
                var virtualRoots = flow.Components.SelectMany(component => component.VirtualInputColumns)
                    .Where(column => column.ComponentId == componentId && column.Id == columnId).ToList();
                if (virtualRoots.Count == 1 && virtualRoots[0].LineageId > 0)
                {
                    var selected = virtualRoots[0];
                    roots = columns.Where(column => IsInput(column) && column.ComponentId == componentId &&
                        column.PortId == selected.PortId && column.LineageId == selected.LineageId).ToList();
                }
            }
            if (roots.Count != 1)
            {
                return new InspectionResult<ColumnLineageTrace>(Array.Empty<ColumnLineageTrace>(), new[]
                {
                    new UnsupportedItem("", "", "", "The column selection was missing or ambiguous.", UnsupportedItem.UnsupportedMetadataCode)
                });
            }

            var allLinks = new List<ColumnLineageLink>();
            var expressionGaps = new HashSet<DataFlowColumnOverview>();
            foreach (var path in flow.Paths)
            {
                var sources = columns.Where(column => column.Direction == "Output" && column.ComponentId == path.SourceComponentId && column.PortId == path.SourceOutputId);
                var targets = columns.Where(column => IsInput(column) && column.ComponentId == path.TargetComponentId && column.PortId == path.TargetInputId);
                foreach (var source in sources)
                {
                    foreach (var target in targets.Where(column => column.LineageId > 0 && column.LineageId == source.LineageId))
                    {
                        allLinks.Add(new ColumnLineageLink(source, target, path.Id));
                    }
                }
                var sourceComponent = flow.Components.SingleOrDefault(component => component.Id == path.SourceComponentId);
                if (sourceComponent == null) { continue; }
                foreach (var output in sourceComponent.Outputs.Where(item => item.Id == path.SourceOutputId && item.SynchronousInputId.Length > 0))
                {
                    var inputs = Inputs(sourceComponent).Where(input => input.PortId == output.SynchronousInputId && input.LineageId > 0 &&
                        !sourceComponent.OutputColumns.Any(column => column.PortId == output.Id && column.LineageId == input.LineageId));
                    foreach (var input in inputs)
                    {
                        foreach (var target in targets.Where(column => column.LineageId == input.LineageId))
                        {
                            allLinks.Add(new ColumnLineageLink(input, target, path.Id, output.Id));
                        }
                    }
                }
            }
            foreach (var component in flow.Components)
            {
                foreach (var output in component.OutputColumns.Where(column => column.ExpressionDependencies != null))
                {
                    var dependencies = output.ExpressionDependencies;
                    if (!dependencies.IsResolved) { expressionGaps.Add(output); continue; }
                    var ports = component.Outputs.Where(port => port.Id == output.PortId).ToList();
                    foreach (var lineageId in dependencies.InputLineageIds)
                    {
                        var sources = Inputs(component).Where(input => input.LineageId == lineageId &&
                            (component.Outputs.Count == 0 || (ports.Count == 1 &&
                                ports[0].SynchronousInputId == input.PortId))).ToList();
                        if (sources.Count == 1) { allLinks.Add(new ColumnLineageLink(sources[0], output, string.Empty)); }
                        else { expressionGaps.Add(output); }
                    }
                }
                foreach (var output in component.OutputColumns.Where(column => column.SourceInputLineageId.HasValue && column.LineageId > 0))
                {
                    var ports = component.Outputs.Where(port => port.Id == output.PortId).ToList();
                    var sources = Inputs(component).Where(input => input.LineageId == output.SourceInputLineageId.Value &&
                        (component.Outputs.Count == 0 || (ports.Count == 1 &&
                            (ports[0].SynchronousInputId.Length == 0 || ports[0].SynchronousInputId == input.PortId)))).ToList();
                    if (sources.Count == 1)
                    {
                        allLinks.Add(new ColumnLineageLink(sources[0], output, string.Empty));
                    }
                }
                foreach (var input in Inputs(component))
                {
                    foreach (var output in component.OutputColumns.Where(column => column.ExpressionDependencies == null && !column.SourceInputLineageId.HasValue && column.LineageId > 0 && column.LineageId == input.LineageId &&
                        (component.Outputs.Count == 0 || component.Outputs.Any(port => port.Id == column.PortId && port.SynchronousInputId == input.PortId))))
                    {
                        allLinks.Add(new ColumnLineageLink(input, output, string.Empty));
                    }
                }
            }

            var visited = new HashSet<DataFlowColumnOverview>();
            var reached = new List<DataFlowColumnOverview>();
            var links = new List<ColumnLineageLink>();
            var gaps = new List<UnsupportedItem>();
            var pending = new Queue<DataFlowColumnOverview>();
            pending.Enqueue(roots[0]);
            while (pending.Count > 0)
            {
                var current = pending.Dequeue();
                if (!visited.Add(current)) { continue; }
                reached.Add(current);
                var nextLinks = allLinks.Where(link => upstream ? ReferenceEquals(link.Target, current) : ReferenceEquals(link.Source, current)).ToList();
                foreach (var link in nextLinks)
                {
                    if (!links.Contains(link)) { links.Add(link); }
                    pending.Enqueue(upstream ? link.Source : link.Target);
                }

                var component = flow.Components.First(item => item.Id == current.ComponentId);
                var connectedOutputs = component.Outputs.Where(output => flow.Paths.Any(path => path.SourceComponentId == component.Id && path.SourceOutputId == output.Id)).ToList();
                var internalBoundary = upstream ? current.Direction == "Output" && (component.InputCount > 0 || current.SourceInputLineageId.HasValue) :
                    IsInput(current) && (component.Outputs.Count == component.OutputCount ?
                        connectedOutputs.Any(output => !HasResolvedReplacement(component, current, output.Id) &&
                            (output.SynchronousInputId.Length == 0 || output.SynchronousInputId == current.PortId ||
                            !Inputs(component).Any(input => input.PortId == output.SynchronousInputId))) : component.OutputCount > 0);
                if (upstream && current.ExpressionDependencies != null && current.ExpressionDependencies.IsResolved &&
                    current.ExpressionDependencies.InputLineageIds.Count == 0)
                {
                    internalBoundary = false;
                }
                var relevantPaths = flow.Paths.Where(path => upstream
                    ? IsInput(current) && path.TargetComponentId == current.ComponentId && path.TargetInputId == current.PortId
                    : path.SourceComponentId == current.ComponentId &&
                        ((current.Direction == "Output" && path.SourceOutputId == current.PortId) ||
                                 (IsInput(current) && !HasResolvedReplacement(component, current, path.SourceOutputId) &&
                                     component.Outputs.Any(output => output.Id == path.SourceOutputId && output.SynchronousInputId == current.PortId)))).ToList();
                var missingPath = relevantPaths.Any(path => !nextLinks.Any(link => link.PathId == path.Id ||
                    (!upstream && link.Target.Direction == "Output" && link.Target.PortId == path.SourceOutputId &&
                        allLinks.Any(continuation => ReferenceEquals(continuation.Source, link.Target) && continuation.PathId == path.Id))));
                var unknownDerivedRelationship = !upstream && IsInput(current) && component.OutputColumns.Any(output =>
                    output.ExpressionDependencies != null && !output.ExpressionDependencies.IsResolved &&
                    (component.Outputs.Count == 0 || component.Outputs.Any(port => port.Id == output.PortId && port.SynchronousInputId == current.PortId)));
                if (current.LineageId <= 0 || missingPath || unknownDerivedRelationship || expressionGaps.Contains(current) || (nextLinks.Count == 0 && internalBoundary))
                {
                    gaps.Add(new UnsupportedItem(current.Id, current.Name, component.ComponentClassId,
                        "The projected metadata cannot prove the next column relationship.", UnsupportedItem.UnsupportedMetadataCode));
                }
            }

            return new InspectionResult<ColumnLineageTrace>(new[]
            {
                new ColumnLineageTrace(flow.ExecutableId, flow.ExecutableName, upstream ? "Upstream" : "Downstream", reached, links)
            }, gaps);
        }

        private static bool HasResolvedReplacement(DataFlowComponentOverview component, DataFlowColumnOverview column, string outputId)
        {
            return component.OutputColumns.Any(output => output.IsReplacement && output.PortId == outputId &&
                output.LineageId == column.LineageId && output.ExpressionDependencies != null && output.ExpressionDependencies.IsResolved);
        }

        private static bool IsInput(DataFlowColumnOverview column)
        {
            return column.Direction == "Input" || column.Direction == "VirtualInput";
        }

        private static IEnumerable<DataFlowColumnOverview> Inputs(DataFlowComponentOverview component)
        {
            return component.InputColumns.Concat(component.VirtualInputColumns.Where(column =>
                !component.InputColumns.Any(input => input.PortId == column.PortId && input.LineageId == column.LineageId)));
        }
    }
}