using System;

namespace SsisAiRuntime.Inspectors.Expressions
{
    public sealed class ExpressionDependencyInspection
    {
        public ExpressionDependencyInspection(ExpressionScopeCatalog catalog, InspectionResult<ExpressionDependencyAnalysis> analyses)
        { Catalog = catalog; Analyses = analyses ?? throw new ArgumentNullException(nameof(analyses)); }
        public ExpressionScopeCatalog Catalog { get; }
        public InspectionResult<ExpressionDependencyAnalysis> Analyses { get; }
    }
}