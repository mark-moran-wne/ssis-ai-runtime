using System;
using System.IO;

namespace SsisAiRuntime.Core
{
    public sealed class PackageSession<TPackage> where TPackage : class
    {
        public PackageSession(string packagePath, string packageName, TPackage package)
        {
            if (string.IsNullOrWhiteSpace(packagePath))
            {
                throw new ArgumentException("A package path is required.", nameof(packagePath));
            }

            SessionId = Guid.NewGuid();
            PackagePath = Path.GetFullPath(packagePath);
            PackageName = packageName ?? string.Empty;
            Package = package ?? throw new ArgumentNullException(nameof(package));
        }

        public Guid SessionId { get; }

        public string PackagePath { get; }

        public string PackageName { get; }

        public TPackage Package { get; }
    }
}