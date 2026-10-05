#nullable enable
using System.Collections.Generic;

namespace SsisAiRuntime.Cli
{
    public sealed class CliTraceRequest
    {
        private CliTraceRequest(string flowId, string componentId, string columnId, bool upstream)
        {
            FlowId = flowId;
            ComponentId = componentId;
            ColumnId = columnId;
            Upstream = upstream;
        }

        public string FlowId { get; }
        public string ComponentId { get; }
        public string ColumnId { get; }
        public bool Upstream { get; }

        public static bool TryParse(string[] args, out CliTraceRequest? request, out bool summary)
        {
            request = null;
            summary = true;
            var options = new Dictionary<string, string>();
            var outputModeSpecified = false;
            for (var index = 2; index < args.Length; index++)
            {
                var option = args[index];
                if (option == "--details")
                {
                    if (outputModeSpecified) { return false; }
                    summary = false;
                    outputModeSpecified = true;
                    continue;
                }
                if ((option != "--flow" && option != "--component" && option != "--column" && option != "--direction") ||
                    options.ContainsKey(option) || index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]) || args[index + 1].StartsWith("--"))
                {
                    return false;
                }
                options.Add(option, args[++index]);
            }
            if (!options.TryGetValue("--flow", out var flow) || !options.TryGetValue("--component", out var component) ||
                !options.TryGetValue("--column", out var column)) { return false; }
            var direction = options.TryGetValue("--direction", out var selectedDirection) ? selectedDirection : "downstream";
            if (direction != "upstream" && direction != "downstream") { return false; }
            request = new CliTraceRequest(flow, component, column, direction == "upstream");
            return true;
        }
    }
}