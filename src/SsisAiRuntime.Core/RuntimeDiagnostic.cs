using System;

namespace SsisAiRuntime.Core
{
    public sealed class RuntimeDiagnostic
    {
        public RuntimeDiagnostic(string code, RuntimeDiagnosticSeverity severity, string message)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                throw new ArgumentException("A diagnostic code is required.", nameof(code));
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                throw new ArgumentException("A diagnostic message is required.", nameof(message));
            }

            Code = code;
            Severity = severity;
            Message = message;
        }

        public string Code { get; }

        public RuntimeDiagnosticSeverity Severity { get; }

        public string Message { get; }
    }
}