using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using SsisAiRuntime.Inspectors;

namespace SsisAiRuntime.AI
{
    public sealed class AiToolResult
    {
        public AiToolResult(string toolName, object result, IEnumerable<UnsupportedItem> unsupportedItems,
            string errorCode = "", string errorMessage = "")
        {
            ToolName = toolName ?? string.Empty;
            Result = result;
            UnsupportedItems = new ReadOnlyCollection<UnsupportedItem>(new List<UnsupportedItem>(unsupportedItems ?? throw new ArgumentNullException(nameof(unsupportedItems))));
            ErrorCode = errorCode ?? string.Empty;
            ErrorMessage = errorMessage ?? string.Empty;
            Succeeded = ErrorCode.Length == 0;
        }

        public string ToolName { get; }
        public bool Succeeded { get; }
        public object Result { get; }
        public IReadOnlyList<UnsupportedItem> UnsupportedItems { get; }
        public bool IsComplete => Succeeded && UnsupportedItems.Count == 0;
        public string ErrorCode { get; }
        public string ErrorMessage { get; }
        public static AiToolResult Failure(string toolName, string code, string message) =>
            new AiToolResult(toolName, null, Array.Empty<UnsupportedItem>(), code, message);
    }
}