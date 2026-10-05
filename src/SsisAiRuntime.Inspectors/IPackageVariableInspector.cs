using System.Collections.Generic;
using SsisAiRuntime.Core;

namespace SsisAiRuntime.Inspectors
{
    public interface IPackageVariableInspector<TPackage> where TPackage : class
    {
        IReadOnlyList<VariableOverview> Inspect(PackageSession<TPackage> session);
    }
}