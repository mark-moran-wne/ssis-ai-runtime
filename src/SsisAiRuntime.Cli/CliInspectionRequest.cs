#nullable enable
namespace SsisAiRuntime.Cli
{
    public sealed class CliInspectionRequest
    {
        internal CliInspectionRequest(string command, string packagePath, CliTraceRequest? trace, CliTaskRequest? task, CliSearchRequest? search)
        {
            Command = command;
            PackagePath = packagePath;
            Trace = trace;
            Task = task;
            Search = search;
        }

        public string Command { get; }
        public string PackagePath { get; }
        public CliTraceRequest? Trace { get; }
        public CliTaskRequest? Task { get; }
        public CliSearchRequest? Search { get; }
    }
}