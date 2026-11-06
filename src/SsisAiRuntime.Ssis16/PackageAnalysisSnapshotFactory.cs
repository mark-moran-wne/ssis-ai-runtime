using System;
using System.Collections.Generic;
using System.Linq;
using SsisAiRuntime.AI;
using SsisAiRuntime.Core;
using SsisAiRuntime.Inspectors;
using SsisAiRuntime.Inspectors.Expressions;

namespace SsisAiRuntime.Ssis16
{
    public sealed class PackageAnalysisSnapshotFactory
    {
        public InspectionResult<PackageAnalysisSnapshot> Create(PackageSession<Microsoft.SqlServer.Dts.Runtime.Package> session) =>
            Create(session, (IEnumerable<ExpressionSymbol>)null);
        public InspectionResult<PackageAnalysisSnapshot> Create(PackageSession<Microsoft.SqlServer.Dts.Runtime.Package> session, bool includeSanitizedText) =>
            Create(session, (IEnumerable<ExpressionSymbol>)null, includeSanitizedText);

        public InspectionResult<PackageAnalysisSnapshot> Create(PackageSession<Microsoft.SqlServer.Dts.Runtime.Package> session,
            IEnumerable<ExpressionSymbol> projectParameters, bool includeSanitizedText = false)
        {
            return CreateCore(session, projectParameters, null, includeSanitizedText);
        }

        public InspectionResult<PackageAnalysisSnapshot> CreateWithProjectInventory(
            PackageSession<Microsoft.SqlServer.Dts.Runtime.Package> session, IProjectInventory projectInventory,
            bool includeSanitizedText = false)
        {
            if (projectInventory == null) { throw new ArgumentNullException(nameof(projectInventory)); }
            return CreateCore(session, null, projectInventory, includeSanitizedText);
        }

        private InspectionResult<PackageAnalysisSnapshot> CreateCore(
            PackageSession<Microsoft.SqlServer.Dts.Runtime.Package> session, IEnumerable<ExpressionSymbol> projectParameters,
            IProjectInventory projectInventory, bool includeSanitizedText)
        {
            if (session == null) { throw new ArgumentNullException(nameof(session)); }
            var overview = new PackageOverviewInspector().InspectDetailed(session);
            if (overview.Items.Count != 1)
            {
                return new InspectionResult<PackageAnalysisSnapshot>(Array.Empty<PackageAnalysisSnapshot>(), overview.UnsupportedItems);
            }

            var connections = new PackageConnectionInspector().InspectDetailed(session);
            var variables = new PackageVariableInspector().InspectDetailed(session);
            var parameters = new PackageParameterInspector().InspectDetailed(session);
            var executables = new PackageExecutableInspector().InspectDetailed(session);
            var sql = new PackageSqlInspector().InspectDetailed(session, includeSanitizedText);
            var dataFlows = new PackageDataFlowInspector().InspectDetailed(session);
            var expressions = new PackageExpressionInspector().InspectDetailed(session);
            var precedence = new PackagePrecedenceInspector().InspectDetailed(session);
            var unsupported = Merge(overview.UnsupportedItems, connections.UnsupportedItems, variables.UnsupportedItems,
                parameters.UnsupportedItems, executables.UnsupportedItems, sql.UnsupportedItems, dataFlows.UnsupportedItems,
                expressions.UnsupportedItems, precedence.UnsupportedItems);
            var package = overview.Items[0];
            var catalog = new SemanticHandleCatalogBuilder().Build(package, connections.Items, variables.Items,
                parameters.Items, executables.Items, dataFlows.Items);
            var controlFlow = new ControlFlowGraphBuilder().Build(executables.Items, precedence, catalog);
            unsupported = Merge(unsupported, controlFlow.UnsupportedItems);
            var effectiveProjectParameters = projectParameters;
            if (projectInventory != null)
            {
                var packages = projectInventory.Packages ?? throw new ArgumentException("Project packages are required.", nameof(projectInventory));
                var inventoryParameters = projectInventory.Parameters ?? throw new ArgumentException("Project parameters are required.", nameof(projectInventory));
                if (packages.Count(item => item.PackageId == package.PackageId) == 1)
                {
                    effectiveProjectParameters = inventoryParameters.Select(parameter => new ExpressionSymbol(
                        parameter.NativeId, package.PackageId, ExpressionSymbolKind.ProjectParameter, "$Project", parameter.Name)).ToArray();
                }
            }
            var dependencies = new PackageDependencyGraphBuilder().Build(package, connections.Items, executables.Items,
                sql.Items, dataFlows.Items, controlFlow, unsupported, projectInventory);
            var expressionDependencies = new PackageExpressionDependencyInspector().Inspect(session,
                effectiveProjectParameters, includeSanitizedText);
            if (expressionDependencies.Catalog != null)
            {
                dependencies = new ExpressionDependencyGraphBuilder().Enrich(dependencies, expressionDependencies.Catalog,
                    expressionDependencies.Analyses.Items, expressionDependencies.Analyses.UnsupportedItems);
            }
            else
            {
                dependencies = new PackageDependencyGraph(dependencies.Nodes, dependencies.Edges,
                    dependencies.UnsupportedItems.Concat(expressionDependencies.Analyses.UnsupportedItems));
            }
            if (projectInventory != null)
            {
                dependencies = new PackageParameterBindingGraphBuilder().Enrich(dependencies, executables.Items,
                    projectInventory, expressionDependencies.Catalog);
            }
            unsupported = Merge(unsupported, expressionDependencies.Analyses.UnsupportedItems);
            unsupported = Merge(unsupported, dependencies.UnsupportedItems);
            var snapshot = new PackageAnalysisSnapshot(package, connections.Items, variables.Items, parameters.Items,
                executables.Items, sql.Items, dataFlows.Items, expressions.Items, controlFlow, dependencies, catalog, unsupported,
                includeSanitizedText ? sql.Items.Where(item => item.SanitizedText != null).Select(item => item.SanitizedText).Concat(expressionDependencies.SanitizedTexts) : null);
            return new InspectionResult<PackageAnalysisSnapshot>(new[] { snapshot }, unsupported);
        }

        private static IReadOnlyList<UnsupportedItem> Merge(params IEnumerable<UnsupportedItem>[] sequences) =>
            sequences.SelectMany(sequence => sequence)
                .GroupBy(item => item.Id + "\u001f" + item.Name + "\u001f" + item.CreationName + "\u001f" + item.ReasonCode)
                .Select(group => group.First())
                .ToArray();
    }
}