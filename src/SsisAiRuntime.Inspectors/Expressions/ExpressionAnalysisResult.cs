using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SsisAiRuntime.Inspectors.Expressions
{
    public enum ExpressionReferenceKind { Variable, SystemVariable, PackageParameter, ProjectParameter, Unknown }

    public sealed class ExpressionReference
    {
        public ExpressionReference(ExpressionReferenceKind kind, string namespaceName, string name, int startIndex, int length)
        {
            if (string.IsNullOrWhiteSpace(name)) { throw new ArgumentException("A name is required.", nameof(name)); }
            if (startIndex < 0) { throw new ArgumentOutOfRangeException(nameof(startIndex)); }
            if (length < 1) { throw new ArgumentOutOfRangeException(nameof(length)); }
            Kind = kind;
            NamespaceName = namespaceName ?? string.Empty;
            Name = name;
            StartIndex = startIndex;
            Length = length;
        }
        public ExpressionReferenceKind Kind { get; }
        public string NamespaceName { get; }
        public string Name { get; }
        public int StartIndex { get; }
        public int Length { get; }
    }

    public sealed class ExpressionParseDiagnostic
    {
        public ExpressionParseDiagnostic(int line, int column, string code)
        { Line = line; Column = column; Code = code; }
        public int Line { get; }
        public int Column { get; }
        public string Code { get; }
    }

    public sealed class ExpressionAnalysisResult
    {
        public ExpressionAnalysisResult(IEnumerable<ExpressionReference> references, IEnumerable<ExpressionParseDiagnostic> diagnostics)
        {
            Diagnostics = new ReadOnlyCollection<ExpressionParseDiagnostic>((diagnostics ?? throw new ArgumentNullException(nameof(diagnostics))).ToList());
            var candidates = (references ?? throw new ArgumentNullException(nameof(references))).ToList();
            References = new ReadOnlyCollection<ExpressionReference>(Diagnostics.Count == 0 ? candidates : new List<ExpressionReference>());
        }
        public IReadOnlyList<ExpressionReference> References { get; }
        public IReadOnlyList<ExpressionParseDiagnostic> Diagnostics { get; }
        public bool Succeeded => Diagnostics.Count == 0;
    }
}