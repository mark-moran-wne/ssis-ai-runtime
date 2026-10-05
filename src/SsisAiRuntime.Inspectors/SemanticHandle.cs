using System;

namespace SsisAiRuntime.Inspectors
{
    public sealed class SemanticHandle : IEquatable<SemanticHandle>
    {
        internal SemanticHandle(Guid sessionId, SemanticObjectKind kind, string value)
        {
            SessionId = sessionId;
            Kind = kind;
            Value = value ?? string.Empty;
        }

        public Guid SessionId { get; }

        public SemanticObjectKind Kind { get; }

        public string Value { get; }

        public bool Equals(SemanticHandle other)
        {
            return other != null
                && SessionId == other.SessionId
                && Kind == other.Kind
                && string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as SemanticHandle);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = SessionId.GetHashCode();
                hashCode = (hashCode * 397) ^ (int)Kind;
                hashCode = (hashCode * 397) ^ StringComparer.Ordinal.GetHashCode(Value);
                return hashCode;
            }
        }

        public override string ToString()
        {
            return Value;
        }
    }
}