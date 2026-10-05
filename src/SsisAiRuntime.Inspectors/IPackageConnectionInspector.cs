using System.Collections.Generic;
using SsisAiRuntime.Core;

namespace SsisAiRuntime.Inspectors
{
    public interface IPackageConnectionInspector<TPackage> where TPackage : class
    {
        IReadOnlyList<ConnectionOverview> Inspect(PackageSession<TPackage> session);
    }
}