using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SsisAiRuntime.Inspectors.Expressions
{
    public sealed class ExpressionDependencyInspection
    {
        public ExpressionDependencyInspection(ExpressionScopeCatalog catalog, InspectionResult<ExpressionDependencyAnalysis> analyses,
            IEnumerable<SanitizedTextSnippet> sanitizedTexts = null)
        {
            Catalog = catalog; Analyses = analyses ?? throw new ArgumentNullException(nameof(analyses));
            SanitizedTexts = new ReadOnlyCollection<SanitizedTextSnippet>((sanitizedTexts ?? Array.Empty<SanitizedTextSnippet>()).ToList());
        }
        public ExpressionScopeCatalog Catalog { get; }
        public InspectionResult<ExpressionDependencyAnalysis> Analyses { get; }
        public IReadOnlyList<SanitizedTextSnippet> SanitizedTexts { get; }
    }
}