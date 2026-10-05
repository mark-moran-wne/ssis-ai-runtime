using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SsisAiRuntime.Inspectors.Expressions
{
    public sealed class ExpressionDependencyInput
    {
        public ExpressionDependencyInput(string ownerNativeId, string ownerScopeId, string propertyName, string expression)
        {
            if (string.IsNullOrWhiteSpace(ownerNativeId)) { throw new ArgumentException("An owner ID is required.", nameof(ownerNativeId)); }
            if (string.IsNullOrWhiteSpace(ownerScopeId)) { throw new ArgumentException("An owner scope ID is required.", nameof(ownerScopeId)); }
            OwnerNativeId = ownerNativeId; OwnerScopeId = ownerScopeId; PropertyName = propertyName ?? string.Empty;
            Expression = expression ?? throw new ArgumentNullException(nameof(expression));
        }
        public string OwnerNativeId { get; }
        public string OwnerScopeId { get; }
        public string PropertyName { get; }
        internal string Expression { get; }
    }

    public sealed class ExpressionDependencyAnalysis
    {
        public ExpressionDependencyAnalysis(string ownerNativeId, string propertyName, IEnumerable<ExpressionReferenceResolution> resolutions, bool parsed, string ownerScopeId = "")
        {
            OwnerNativeId = ownerNativeId ?? string.Empty; PropertyName = propertyName ?? string.Empty; Parsed = parsed;
            OwnerScopeId = ownerScopeId ?? string.Empty;
            var items = (resolutions ?? throw new ArgumentNullException(nameof(resolutions))).ToList();
            Resolutions = new ReadOnlyCollection<ExpressionReferenceResolution>(parsed ? items : new List<ExpressionReferenceResolution>());
        }
        public string OwnerNativeId { get; }
        public string OwnerScopeId { get; }
        public string PropertyName { get; }
        public bool Parsed { get; }
        public IReadOnlyList<ExpressionReferenceResolution> Resolutions { get; }
        public bool IsComplete => Parsed && Resolutions.All(item => item.Status == ExpressionReferenceResolutionStatus.Resolved);
    }

    public sealed class ExpressionDependencyAnalyzer
    {
        private readonly ExpressionReferenceParser parser;
        private readonly IExpressionScopeResolver resolver;
        public ExpressionDependencyAnalyzer(ExpressionReferenceParser parser, IExpressionScopeResolver resolver)
        { this.parser = parser ?? throw new ArgumentNullException(nameof(parser)); this.resolver = resolver ?? throw new ArgumentNullException(nameof(resolver)); }
        public ExpressionDependencyAnalysis Analyze(ExpressionDependencyInput input, ExpressionScopeCatalog catalog)
        {
            if (input == null) { throw new ArgumentNullException(nameof(input)); }
            if (catalog == null) { throw new ArgumentNullException(nameof(catalog)); }
            var result = parser.Analyze(input.Expression);
            return new ExpressionDependencyAnalysis(input.OwnerNativeId, input.PropertyName,
                result.Succeeded ? resolver.Resolve(input.OwnerScopeId, result.References, catalog) : Array.Empty<ExpressionReferenceResolution>(), result.Succeeded, input.OwnerScopeId);
        }
    }
}