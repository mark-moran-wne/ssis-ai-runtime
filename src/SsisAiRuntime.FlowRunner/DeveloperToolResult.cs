using System;
using Newtonsoft.Json.Linq;

namespace SsisAiRuntime.FlowRunner
{
    internal static class DeveloperToolResult
    {
        public static JObject Success(string command, JObject results) => new JObject
        {
            ["schemaVersion"] = "1.0", ["command"] = command, ["succeeded"] = true,
            ["exitCode"] = 0, ["results"] = results, ["diagnostics"] = new JArray(),
            ["executionMode"] = "trusted-developer", ["packageExecuted"] = false
        };

        public static JObject Failure(string command, string code, string stage, Exception error, int exitCode = 4) => new JObject
        {
            ["schemaVersion"] = "1.0", ["command"] = command, ["succeeded"] = false,
            ["code"] = code, ["exitCode"] = exitCode, ["results"] = null,
            ["executionMode"] = "trusted-developer",
            ["diagnostics"] = new JArray(new JObject
            {
                ["code"] = code, ["stage"] = stage, ["severity"] = "Error",
                ["message"] = error.Message, ["exceptionType"] = error.GetType().FullName,
                ["hresult"] = "0x" + unchecked((uint)error.HResult).ToString("X8")
            })
        };
    }
}