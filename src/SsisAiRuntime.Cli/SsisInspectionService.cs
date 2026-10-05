using System;
using System.Collections.Generic;
using SsisAiRuntime.Core;
using SsisAiRuntime.Inspectors;
using SsisAiRuntime.Ssis16;

namespace SsisAiRuntime.Cli
{
    internal static class SsisInspectionService
    {
        public static CliInspection Inspect(string command, string path)
        {
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

                if (command != "all")
                {
                    return InspectOperation(command);
                }

                var reports = new Dictionary<string, CliInspection>(StringComparer.Ordinal);
                foreach (var operation in CliInspectionBatch.Operations)
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