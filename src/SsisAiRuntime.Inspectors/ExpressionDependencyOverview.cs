using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SsisAiRuntime.Inspectors
{
    public enum ExpressionReferenceKind
    {
        Variable,
        Parameter
    }

    public sealed class ExpressionReferenceOverview
    {
        public ExpressionReferenceOverview(ExpressionReferenceKind kind, string nativeId, string namespaceName, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) { throw new ArgumentException("A referenced object name is required.", nameof(name)); }
            Kind = kind;
            NativeId = nativeId ?? string.Empty;
            NamespaceName = namespaceName ?? string.Empty;
            Name = name;
        }

        public ExpressionReferenceKind Kind { get; }
        public string NativeId { get; }
        public string NamespaceName { get; }
        public string Name { get; }
        public string QualifiedName => NamespaceName.Length == 0 ? Name : NamespaceName + "::" + Name;
    }

    public sealed class ExpressionDependencyOverview
    {
        public ExpressionDependencyOverview(string ownerType, string ownerId, string ownerName, string creationName,
            string propertyName, IEnumerable<ExpressionReferenceOverview> references)
        {
            if (string.IsNullOrWhiteSpace(ownerType)) { throw new ArgumentException("An expression-owner type is required.", nameof(ownerType)); }
            if (string.IsNullOrWhiteSpace(ownerId)) { throw new ArgumentException("An expression-owner ID is required.", nameof(ownerId)); }
            if (references == null) { throw new ArgumentNullException(nameof(references)); }

            OwnerType = ownerType;
            OwnerId = ownerId;
            OwnerName = ownerName ?? string.Empty;
            CreationName = creationName ?? string.Empty;
            PropertyName = propertyName ?? string.Empty;
            References = new ReadOnlyCollection<ExpressionReferenceOverview>(references
                .Select(item => item ?? throw new ArgumentException("Expression references cannot contain null entries.", nameof(references)))
                .GroupBy(item => item.Kind + "\u001f" + item.NativeId + "\u001f" + item.QualifiedName, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(item => item.Kind)
                .ThenBy(item => item.QualifiedName, StringComparer.OrdinalIgnoreCase)
                .ToList());
        }

        public string OwnerType { get; }
        public string OwnerId { get; }
        public string OwnerName { get; }
        public string CreationName { get; }
        public string PropertyName { get; }
        public IReadOnlyList<ExpressionReferenceOverview> References { get; }
        public bool ExpressionTextOmitted => true;
        public bool ValuesOmitted => true;
    }
}