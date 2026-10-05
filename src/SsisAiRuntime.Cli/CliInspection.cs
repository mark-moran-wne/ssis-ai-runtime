#nullable enable
using System;
using System.Collections.Generic;
using SsisAiRuntime.Core;
using SsisAiRuntime.Inspectors;

namespace SsisAiRuntime.Cli
{
    public sealed class CliInspection
    {
        public CliInspection(object? results, RuntimeDiagnostics diagnostics, IEnumerable<UnsupportedItem> unsupportedItems, int failureExitCode = 3)
        {
            if (failureExitCode != 3 && failureExitCode != 4)
            {
                throw new ArgumentOutOfRangeException(nameof(failureExitCode));
            }

            Results = results;
            Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            UnsupportedItems = new List<UnsupportedItem>(unsupportedItems ?? throw new ArgumentNullException(nameof(unsupportedItems))).AsReadOnly();
            FailureExitCode = failureExitCode;
        }

        public object? Results { get; }
        public RuntimeDiagnostics Diagnostics { get; }
        public IReadOnlyList<UnsupportedItem> UnsupportedItems { get; }
        public int FailureExitCode { get; }
    }
}