using System;

namespace SsisAiRuntime.Inspectors
{
    public sealed class PackageOverview
    {
        public PackageOverview(
            Guid sessionId,
            string packageName,
            string packageId,
            string description,
            DateTime creationDate,
            int versionMajor,
            int versionMinor,
            int versionBuild,
            string protectionLevel,
            string packageType,
            int connectionCount,
            int variableCount,
            int executableCount,
            int precedenceConstraintCount,
            int parameterCount,
            bool hasExpressions)
        {
            SessionId = sessionId;
            PackageName = packageName ?? string.Empty;
            PackageId = packageId ?? string.Empty;
            Description = description ?? string.Empty;
            CreationDate = creationDate;
            VersionMajor = versionMajor;
            VersionMinor = versionMinor;
            VersionBuild = versionBuild;
            ProtectionLevel = protectionLevel ?? string.Empty;
            PackageType = packageType ?? string.Empty;
            ConnectionCount = connectionCount;
            VariableCount = variableCount;
            ExecutableCount = executableCount;
            PrecedenceConstraintCount = precedenceConstraintCount;
            ParameterCount = parameterCount;
            HasExpressions = hasExpressions;
        }

        public Guid SessionId { get; }

        public string PackageName { get; }

        public string PackageId { get; }

        public string Description { get; }

        public DateTime CreationDate { get; }

        public int VersionMajor { get; }

        public int VersionMinor { get; }

        public int VersionBuild { get; }

        public string ProtectionLevel { get; }

        public string PackageType { get; }

        public int ConnectionCount { get; }

        public int VariableCount { get; }

        public int ExecutableCount { get; }

        public int PrecedenceConstraintCount { get; }

        public int ParameterCount { get; }

        public bool HasExpressions { get; }
    }
}