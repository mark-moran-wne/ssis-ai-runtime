#nullable enable
namespace SsisAiRuntime.Cli
{
    public sealed class CliTaskRequest
    {
        private CliTaskRequest(string taskId, bool recursive)
        {
            TaskId = taskId;
            Recursive = recursive;
        }

        public string TaskId { get; }
        public bool Recursive { get; }

        public static bool TryParse(string[] args, out CliTaskRequest? request, out bool summary)
        {
            request = null;
            summary = false;
            string? task = null;
            var recursive = false;
            for (var index = 2; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--summary":
                        if (summary) { return false; }
                        summary = true;
                        break;
                    case "--recursive":
                        if (recursive) { return false; }
                        recursive = true;
                        break;
                    case "--task":
                        if (task != null || index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]) || args[index + 1].StartsWith("--")) { return false; }
                        task = args[++index];
                        break;
                    default:
                        return false;
                }
            }
            if (task == null) { return false; }
            request = new CliTaskRequest(task, recursive);
            return true;
        }
    }
}