using System;
using SsisAiRuntime.Inspectors;

namespace SsisAiRuntime.AI
{
    public sealed class AiToolRequest
    {
        public AiToolRequest(string toolName, string query = null, SemanticObjectKind? kind = null,
            string taskId = null, bool predecessors = false, bool recursive = false,
            string flowId = null, string componentId = null, string columnId = null, bool upstream = false,
            string nodeKey = null, bool incoming = true, string question = null, string selector = null)
        {
            if (string.IsNullOrWhiteSpace(toolName)) throw new ArgumentException("A tool name is required.", nameof(toolName));
            ToolName = toolName;
            Query = query ?? string.Empty;
            Kind = kind;
            TaskId = taskId ?? string.Empty;
            Predecessors = predecessors;
            Recursive = recursive;
            FlowId = flowId ?? string.Empty;
            ComponentId = componentId ?? string.Empty;
            ColumnId = columnId ?? string.Empty;
            Upstream = upstream;
            NodeKey = nodeKey ?? string.Empty;
            Incoming = incoming;
            Question = question ?? string.Empty;
            Selector = selector ?? string.Empty;
        }

        public string ToolName { get; }
        public string Query { get; }
        public SemanticObjectKind? Kind { get; }
        public string TaskId { get; }
        public bool Predecessors { get; }
        public bool Recursive { get; }
        public string FlowId { get; }
        public string ComponentId { get; }
        public string ColumnId { get; }
        public bool Upstream { get; }
        public string NodeKey { get; }
        public bool Incoming { get; }
        public string Question { get; }
        public string Selector { get; }
    }
}