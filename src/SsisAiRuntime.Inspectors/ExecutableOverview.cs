using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SsisAiRuntime.Inspectors
{
    public sealed class ExecutableOverview
    {
        public ExecutableOverview(
            string id,
            string parentId,
            string name,
            string creationName,
            string description,
            int depth,
            bool isContainer,
            bool hasExpressions,
            string executePackageReferenceType = "",
            string executePackageTargetName = "",
            IEnumerable<ExecutePackageParameterBindingOverview> packageParameterBindings = null)
        {
            Id = id ?? string.Empty;
            ParentId = parentId ?? string.Empty;
            Name = name ?? string.Empty;
            CreationName = creationName ?? string.Empty;
            Description = description ?? string.Empty;
            Depth = depth;
            IsContainer = isContainer;
            HasExpressions = hasExpressions;
            ExecutePackageReferenceType = executePackageReferenceType ?? string.Empty;
            ExecutePackageTargetName = executePackageTargetName ?? string.Empty;
            PackageParameterBindings = new ReadOnlyCollection<ExecutePackageParameterBindingOverview>(
                (packageParameterBindings ?? Array.Empty<ExecutePackageParameterBindingOverview>()).ToArray());
        }

        public string Id { get; }

        public string ParentId { get; }

        public string Name { get; }

        public string CreationName { get; }

        public string Description { get; }

        public int Depth { get; }

        public bool IsContainer { get; }

        public bool HasExpressions { get; }

        public string ExecutePackageReferenceType { get; }

        public string ExecutePackageTargetName { get; }

        public IReadOnlyList<ExecutePackageParameterBindingOverview> PackageParameterBindings { get; }
    }
}