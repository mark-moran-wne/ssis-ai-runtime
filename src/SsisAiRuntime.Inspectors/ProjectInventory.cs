using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SsisAiRuntime.Inspectors
{
    public interface IProjectInventory
    {
        string ProjectIdentity { get; }
        string DeploymentModel { get; }
        IReadOnlyList<ProjectPackageInfo> Packages { get; }
        IReadOnlyList<ProjectParameterInfo> Parameters { get; }
    }

    public sealed class ProjectInventory : IProjectInventory
    {
        public ProjectInventory(string projectIdentity, string deploymentModel,
            IEnumerable<ProjectPackageInfo> packages, IEnumerable<ProjectParameterInfo> parameters)
        {
            if (string.IsNullOrWhiteSpace(projectIdentity)) { throw new ArgumentException("A project identity is required.", nameof(projectIdentity)); }
            if (string.IsNullOrWhiteSpace(deploymentModel)) { throw new ArgumentException("A deployment model is required.", nameof(deploymentModel)); }
            ProjectIdentity = projectIdentity;
            DeploymentModel = deploymentModel;
            Packages = ReadOnly(packages, nameof(packages));
            Parameters = ReadOnly(parameters, nameof(parameters));
        }

        public string ProjectIdentity { get; }
        public string DeploymentModel { get; }
        public IReadOnlyList<ProjectPackageInfo> Packages { get; }
        public IReadOnlyList<ProjectParameterInfo> Parameters { get; }

        private static IReadOnlyList<T> ReadOnly<T>(IEnumerable<T> items, string name)
        {
            if (items == null) { throw new ArgumentNullException(name); }
            var materialized = items.ToArray();
            if (materialized.Any(item => item == null)) { throw new ArgumentException("Inventory entries cannot be null.", name); }
            return new ReadOnlyCollection<T>(materialized);
        }
    }

    public sealed class ProjectPackageInfo
    {
        public ProjectPackageInfo(string packageId, string projectRelativeName,
            IEnumerable<PackageParameterInfo> packageParameters = null)
        {
            if (string.IsNullOrWhiteSpace(packageId)) { throw new ArgumentException("A package identity is required.", nameof(packageId)); }
            if (string.IsNullOrWhiteSpace(projectRelativeName)) { throw new ArgumentException("A project-relative package name is required.", nameof(projectRelativeName)); }
            PackageId = packageId;
            ProjectRelativeName = projectRelativeName;
            PackageParameters = ReadOnly(packageParameters ?? Array.Empty<PackageParameterInfo>(), nameof(packageParameters));
        }

        public string PackageId { get; }
        public string ProjectRelativeName { get; }
        public IReadOnlyList<PackageParameterInfo> PackageParameters { get; }

        public string DependencyKey(string projectIdentity) =>
            SemanticObjectKind.Package + ":" + Uri.EscapeDataString(projectIdentity ?? string.Empty) + ":" + Uri.EscapeDataString(PackageId);

        private static IReadOnlyList<T> ReadOnly<T>(IEnumerable<T> items, string name)
        {
            if (items == null) { throw new ArgumentNullException(name); }
            var materialized = items.ToArray();
            if (materialized.Any(item => item == null)) { throw new ArgumentException("Inventory entries cannot be null.", name); }
            return new ReadOnlyCollection<T>(materialized);
        }
    }

    public sealed class PackageParameterInfo
    {
        public PackageParameterInfo(string nativeId, string name)
        {
            if (string.IsNullOrWhiteSpace(nativeId)) { throw new ArgumentException("A package parameter identity is required.", nameof(nativeId)); }
            if (string.IsNullOrWhiteSpace(name)) { throw new ArgumentException("A package parameter name is required.", nameof(name)); }
            NativeId = nativeId;
            Name = name;
        }

        public string NativeId { get; }
        public string Name { get; }
    }

    public sealed class ProjectParameterInfo
    {
        public ProjectParameterInfo(string nativeId, string name)
        {
            if (string.IsNullOrWhiteSpace(nativeId)) { throw new ArgumentException("A project parameter identity is required.", nameof(nativeId)); }
            if (string.IsNullOrWhiteSpace(name)) { throw new ArgumentException("A project parameter name is required.", nameof(name)); }
            NativeId = nativeId;
            Name = name;
        }

        public string NativeId { get; }
        public string Name { get; }
    }
}