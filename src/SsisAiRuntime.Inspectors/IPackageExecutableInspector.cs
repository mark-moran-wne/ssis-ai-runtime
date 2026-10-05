using System.Collections.Generic;
using SsisAiRuntime.Core;

namespace SsisAiRuntime.Inspectors
{
    public interface IPackageExecutableInspector<TPackage> where TPackage : class
    {
        IReadOnlyList<ExecutableOverview> Inspect(PackageSession<TPackage> session);
    }
}