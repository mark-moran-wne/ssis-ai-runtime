using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SsisAiRuntime.Core
{
    public sealed class RuntimeDiagnostics
    {
        public RuntimeDiagnostics(
            string runtimeVersion,
            string processArchitecture,
            IEnumerable<RuntimeDiagnostic> items)
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            var diagnostics = new List<RuntimeDiagnostic>();
            foreach (var item in items)
            {
                diagnostics.Add(item ?? throw new ArgumentException("Diagnostics cannot contain null items.", nameof(items)));
            }

            RuntimeVersion = runtimeVersion ?? string.Empty;
            ProcessArchitecture = processArchitecture ?? string.Empty;
            Items = new ReadOnlyCollection<RuntimeDiagnostic>(diagnostics);

            foreach (var item in diagnostics)
            {
                if (item.Severity == RuntimeDiagnosticSeverity.Error)
                {
                    HasErrors = true;
                    break;
                }
            }
        }

        public string RuntimeVersion { get; }

        public string ProcessArchitecture { get; }

        public IReadOnlyList<RuntimeDiagnostic> Items { get; }

        public bool HasErrors { get; }
    }
}