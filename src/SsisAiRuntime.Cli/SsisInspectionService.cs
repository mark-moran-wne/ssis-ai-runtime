#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using SsisAiRuntime.Core;
using SsisAiRuntime.Inspectors;
using SsisAiRuntime.Ssis16;

namespace SsisAiRuntime.Cli
{
    internal static class SsisInspectionService
    {
        public static CliInspection Inspect(CliInspectionRequest invocation)
        {
            var command = invocation.Command;
            var path = invocation.PackagePath;
            var request = invocation.Trace;
            var taskRequest = invocation.Task;
            var load = new PackageLoader().Load(path);
            if (!load.Succeeded)
            {
                return new CliInspection(null, load.Diagnostics, Array.Empty<UnsupportedItem>());
            }

            var session = load.Session;
            try
            {
                var overview = new PackageOverviewInspector().InspectDetailed(session);
                var package = overview.Items[0];
                var builder = new PackageContextBuilder();
                CliInspection InspectOperation(string operation)
                {
                    try
                    {
                        var unsupported = new List<UnsupportedItem>(overview.UnsupportedItems);
                        object results;
                        switch (operation)
                        {
                            case "overview":
                                results = package;
                                break;
                            case "sql":
                                var sql = new PackageSqlInspector().InspectDetailed(session);
                                unsupported.AddRange(sql.UnsupportedItems);
                                results = builder.BuildSql(package, sql);
                                break;
                            case "lineage":
                                var dataFlows = new PackageDataFlowInspector().InspectDetailed(session);
                                unsupported.AddRange(dataFlows.UnsupportedItems);
                                results = builder.BuildLineage(package, dataFlows);
                                break;
                            case "configuration":
                                var context = builder.BuildConfiguration(package,
                                    new PackageConnectionInspector().InspectDetailed(session),
                                    new PackageVariableInspector().InspectDetailed(session),
                                    new PackageParameterInspector().InspectDetailed(session),
                                    new PackageExpressionInspector().InspectDetailed(session));
                                unsupported.AddRange(context.UnsupportedItems);
                                results = context;
                                break;
                            case "trace":
                                if (request == null) { throw new ArgumentException("A trace selection is required."); }
                                var flowReport = new PackageDataFlowInspector().InspectDetailed(session);
                                var selectedFlows = flowReport.Items
                                    .Where(flow => flow.ExecutableId == request.FlowId).ToList();
                                var trace = selectedFlows.Count == 1
                                    ? new ColumnLineageQuery().Trace(selectedFlows[0], request.ComponentId, request.ColumnId, request.Upstream)
                                    : null;
                                if (trace == null || trace.Items.Count != 1)
                                {
                                    return new CliInspection(null, new RuntimeDiagnostics(load.Diagnostics.RuntimeVersion,
                                        load.Diagnostics.ProcessArchitecture, new[] { new RuntimeDiagnostic("lineage.selection.invalid",
                                            RuntimeDiagnosticSeverity.Error, "The flow or column selection was missing or ambiguous.") }),
                                        Array.Empty<UnsupportedItem>(), 4);
                                }
                                unsupported.AddRange(trace.UnsupportedItems);
                                unsupported.AddRange(flowReport.UnsupportedItems.Where(item => item.Id == request.FlowId && item.ReasonCode == UnsupportedItem.ReadFailureCode));
                                results = new PackageColumnTrace(package, trace.Items[0]);
                                break;
                            case "search":
                                var searchRequest = invocation.Search ?? throw new ArgumentException("A search selection is required.");
                                var searchConnections = new PackageConnectionInspector().InspectDetailed(session);
                                var searchVariables = new PackageVariableInspector().InspectDetailed(session);
                                var searchParameters = new PackageParameterInspector().InspectDetailed(session);
                                var searchExecutables = new PackageExecutableInspector().InspectDetailed(session);
                                var searchFlows = new PackageDataFlowInspector().InspectDetailed(session);
                                var searchCatalog = new SemanticHandleCatalogBuilder().Build(package, searchConnections.Items,
                                    searchVariables.Items, searchParameters.Items, searchExecutables.Items, searchFlows.Items);
                                unsupported.AddRange(searchConnections.UnsupportedItems);
                                unsupported.AddRange(searchVariables.UnsupportedItems);
                                unsupported.AddRange(searchParameters.UnsupportedItems);
                                unsupported.AddRange(searchFlows.UnsupportedItems.Where(item => item.ReasonCode == UnsupportedItem.ReadFailureCode));
                                results = new PackageSearchResult(package, searchCatalog, searchRequest.Query, searchRequest.Kind);
                                break;
                            case "control-flow":
                            case "predecessors":
                            case "successors":
                                var executables = new PackageExecutableInspector().InspectDetailed(session);
                                var precedence = new PackagePrecedenceInspector().InspectDetailed(session);
                                var catalog = new SemanticHandleCatalogBuilder().Build(package, Array.Empty<ConnectionOverview>(),
                                    Array.Empty<VariableOverview>(), Array.Empty<ParameterOverview>(), executables.Items, Array.Empty<DataFlowOverview>());
                                var graph = new ControlFlowGraphBuilder().Build(executables.Items, precedence, catalog);
                                if (operation != "control-flow")
                                {
                                    if (taskRequest == null) { throw new ArgumentException("A task selection is required."); }
                                    var related = new ControlFlowQuery().FindRelated(graph, taskRequest.TaskId,
                                        operation == "predecessors", taskRequest.Recursive);
                                    if (related.Items.Count != 1)
                                    {
                                        return new CliInspection(null, new RuntimeDiagnostics(load.Diagnostics.RuntimeVersion,
                                            load.Diagnostics.ProcessArchitecture, new[] { new RuntimeDiagnostic("controlflow.selection.invalid",
                                                RuntimeDiagnosticSeverity.Error, "The task selection was missing or ambiguous.") }),
                                            Array.Empty<UnsupportedItem>(), 4);
                                    }
                                    graph = related.Items[0];
                                }
                                unsupported.AddRange(graph.UnsupportedItems);
                                results = new PackageControlFlow(package, graph);
                                break;
                            default:
                                throw new ArgumentException("Unknown inspection command.", nameof(operation));
                        }
                        return new CliInspection(results, load.Diagnostics, unsupported);
                    }
                    catch (Exception)
                    {
                        return new CliInspection(null, new RuntimeDiagnostics(load.Diagnostics.RuntimeVersion,
                            load.Diagnostics.ProcessArchitecture, new[] { new RuntimeDiagnostic("cli.inspection.failed",
                                RuntimeDiagnosticSeverity.Error, "Inspection failed. Exception details are omitted.") }),
                            Array.Empty<UnsupportedItem>(), 4);
                    }
                }

                if (command != "inspect")
                {
                    return InspectOperation(command);
                }

                var reports = new Dictionary<string, CliInspection>(StringComparer.Ordinal);
                foreach (var operation in invocation.IncludedOperations ?? CliInspectionBatch.Operations)
                {
                    var inspection = InspectOperation(operation);
                    reports.Add(operation, inspection);
                    if (inspection.Diagnostics.HasErrors)
                    {
                        break;
                    }
                }
                return new CliInspection(new CliInspectionBatch(reports), load.Diagnostics, Array.Empty<UnsupportedItem>());
            }
            finally
            {
                session.Package.Dispose();
            }
        }
    }
}