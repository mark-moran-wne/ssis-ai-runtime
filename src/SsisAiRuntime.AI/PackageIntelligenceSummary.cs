using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using SsisAiRuntime.Inspectors;

namespace SsisAiRuntime.AI
{
    public sealed class PackageIntelligenceSummary
    {
        public PackageIntelligenceSummary(PackageAnalysisSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            Package = snapshot.Package;
            ConnectionCount = snapshot.Connections.Count;
            VariableCount = snapshot.Variables.Count;
            ParameterCount = snapshot.Parameters.Count;
            ExecutableCount = snapshot.Executables.Count;
            SqlTaskCount = snapshot.SqlStatements.Count;
            DataFlowCount = snapshot.DataFlows.Count;
            ComponentCount = snapshot.DataFlows.Sum(flow => flow.Components.Count);
            PathCount = snapshot.DataFlows.Sum(flow => flow.Paths.Count);
            ExpressionOwnerCount = snapshot.Expressions.Count;
            CoverageGapCount = snapshot.UnsupportedItems.Count;
            Connections = new ReadOnlyCollection<NamedReference>(snapshot.Connections
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .Select(item => new NamedReference(item.Name, item.CreationName, item.Id)).ToList());
            Tasks = new ReadOnlyCollection<NamedReference>(snapshot.Executables
                .OrderBy(item => item.Depth).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .Select(item => new NamedReference(item.Name, item.CreationName, item.Id)).ToList());
        }

        public PackageOverview Package { get; }
        public int ConnectionCount { get; }
        public int VariableCount { get; }
        public int ParameterCount { get; }
        public int ExecutableCount { get; }
        public int SqlTaskCount { get; }
        public int DataFlowCount { get; }
        public int ComponentCount { get; }
        public int PathCount { get; }
        public int ExpressionOwnerCount { get; }
        public int CoverageGapCount { get; }
        public IReadOnlyList<NamedReference> Connections { get; }
        public IReadOnlyList<NamedReference> Tasks { get; }
    }

    public sealed class NamedReference
    {
        public NamedReference(string name, string creationName, string nativeId)
        {
            Name = name ?? string.Empty;
            CreationName = creationName ?? string.Empty;
            NativeId = nativeId ?? string.Empty;
        }
        public string Name { get; }
        public string CreationName { get; }
        public string NativeId { get; }
    }
}