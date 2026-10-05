#nullable enable
using System.Collections.Generic;

namespace SsisAiRuntime.Cli
{
    public sealed class CliInspectionRequest
    {
        internal CliInspectionRequest(string command, string packagePath, CliTraceRequest? trace, CliTaskRequest? task,
            CliSearchRequest? search, IReadOnlyList<string>? includedOperations)
        {
            Command = command;
            PackagePath = packagePath;
            Trace = trace;
            Task = task;
            Search = search;
            IncludedOperations = includedOperations;
        }

        public string Command { get; }
        public string PackagePath { get; }
        public CliTraceRequest? Trace { get; }
        public CliTaskRequest? Task { get; }
        public CliSearchRequest? Search { get; }
        public IReadOnlyList<string>? IncludedOperations { get; }
    }
}