#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SsisAiRuntime.Cli
{
    public sealed class CliInspectionBatch
    {
        public static IReadOnlyList<string> Operations { get; } = Array.AsReadOnly(new[] { "overview", "sql", "lineage", "configuration" });

        public CliInspectionBatch(IDictionary<string, CliInspection> reports)
        {
            Reports = new ReadOnlyDictionary<string, CliInspection>(new Dictionary<string, CliInspection>(reports, StringComparer.Ordinal));
        }

        public IReadOnlyDictionary<string, CliInspection> Reports { get; }
    }
}