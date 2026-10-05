using System.Collections.Generic;
using SsisAiRuntime.Core;

namespace SsisAiRuntime.Inspectors
{
    public interface IPackageParameterInspector<TPackage> where TPackage : class
    {
        IReadOnlyList<ParameterOverview> Inspect(PackageSession<TPackage> session);
    }
}