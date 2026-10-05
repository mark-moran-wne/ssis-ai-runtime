using System;
using System.Collections.Generic;
using SsisAiRuntime.Core;
using SsisAiRuntime.Inspectors;
using SsisAiRuntime.Inspectors.Expressions;
using DtsRuntime = Microsoft.SqlServer.Dts.Runtime;

namespace SsisAiRuntime.Ssis16
{
    public sealed class PackageExpressionDependencyInspector : IPackageExpressionDependencyInspector<DtsRuntime.Package>
    {
        public ExpressionDependencyInspection Inspect(PackageSession<DtsRuntime.Package> session) => Inspect(session, null);
        public ExpressionDependencyInspection Inspect(PackageSession<DtsRuntime.Package> session, IEnumerable<ExpressionSymbol> projectParameters,
            bool includeSanitizedText = false)
        {
            if (session == null) { throw new ArgumentNullException(nameof(session)); }
            var gaps = new List<UnsupportedItem>();
            var analyses = new List<ExpressionDependencyAnalysis>();
            var texts = new List<SanitizedTextSnippet>();
            ExpressionScopeCatalog catalog;
            try { catalog = new NativeExpressionScopeCatalogBuilder().Build(session.Package, projectParameters); }
            catch (ExpressionScopeCatalogException exception)
            { return Failed(session.Package.ID, exception.ReasonCode); }
            catch { return Failed(session.Package.ID, "expression.owner_scope_missing"); }
            var analyzer = new ExpressionDependencyAnalyzer(new ExpressionReferenceParser(), new ExpressionScopeResolver());
            foreach (var input in new NativeExpressionDependencyInputBuilder().Build(session.Package, gaps))
            {
                analyses.Add(analyzer.Analyze(input, catalog));
                if (includeSanitizedText) { texts.Add(SanitizedTextFactory.Expression(input)); }
            }
            return new ExpressionDependencyInspection(catalog, new InspectionResult<ExpressionDependencyAnalysis>(analyses, gaps), texts);
        }
        private static ExpressionDependencyInspection Failed(string id, string code) => new ExpressionDependencyInspection(null,
            new InspectionResult<ExpressionDependencyAnalysis>(Array.Empty<ExpressionDependencyAnalysis>(), new[] {
                new UnsupportedItem(id, "ScopeCatalog", "ExpressionDependency", "Expression scope metadata is unavailable.", code) }));
    }
}