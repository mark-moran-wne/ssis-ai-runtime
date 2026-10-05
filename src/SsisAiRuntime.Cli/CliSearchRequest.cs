#nullable enable
using System;
using System.Linq;
using SsisAiRuntime.Inspectors;

namespace SsisAiRuntime.Cli
{
    public sealed class CliSearchRequest
    {
        private CliSearchRequest(string query, SemanticObjectKind? kind)
        {
            Query = query;
            Kind = kind;
        }

        public string Query { get; }
        public SemanticObjectKind? Kind { get; }

        public static bool TryParse(string[] args, out CliSearchRequest? request, out bool summary)
        {
            request = null;
            summary = false;
            string? query = null;
            SemanticObjectKind? kind = null;
            for (var index = 2; index < args.Length; index++)
            {
                var option = args[index];
                if (option == "--summary")
                {
                    if (summary) { return false; }
                    summary = true;
                    continue;
                }
                if ((option != "--query" && option != "--kind") || index + 1 >= args.Length ||
                    string.IsNullOrWhiteSpace(args[index + 1]) || args[index + 1].StartsWith("--")) { return false; }
                var value = args[++index];
                if (option == "--query")
                {
                    if (query != null || value.Length > 256) { return false; }
                    query = value;
                }
                else
                {
                    if (kind.HasValue || !Enum.GetNames(typeof(SemanticObjectKind)).Any(name =>
                        string.Equals(name, value, StringComparison.OrdinalIgnoreCase))) { return false; }
                    kind = (SemanticObjectKind)Enum.Parse(typeof(SemanticObjectKind), value, true);
                }
            }
            if (query == null) { return false; }
            request = new CliSearchRequest(query, kind);
            return true;
        }
    }
}