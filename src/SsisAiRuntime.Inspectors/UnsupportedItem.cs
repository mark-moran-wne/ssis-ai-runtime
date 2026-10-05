using System;

namespace SsisAiRuntime.Inspectors
{
    public sealed class UnsupportedItem
    {
        public const string UnspecifiedCode = "coverage.unspecified";
        public const string IntentionalOmissionCode = "coverage.intentional_omission";
        public const string UnsupportedMetadataCode = "coverage.unsupported_metadata";
        public const string ReadFailureCode = "coverage.read_failed";

        public UnsupportedItem(string id, string name, string creationName, string reason)
            : this(id, name, creationName, reason, UnspecifiedCode)
        {
        }

        public UnsupportedItem(string id, string name, string creationName, string reason, string reasonCode)
        {
            if (reasonCode != UnspecifiedCode && reasonCode != IntentionalOmissionCode &&
                reasonCode != UnsupportedMetadataCode && reasonCode != ReadFailureCode)
            {
                throw new ArgumentException("A supported coverage reason code is required.", nameof(reasonCode));
            }

            Id = id ?? string.Empty;
            Name = name ?? string.Empty;
            CreationName = creationName ?? string.Empty;
            Reason = reason ?? string.Empty;
            ReasonCode = reasonCode;
        }

        public string Id { get; }

        public string Name { get; }

        public string CreationName { get; }

        public string Reason { get; }

        public string ReasonCode { get; }
    }
}