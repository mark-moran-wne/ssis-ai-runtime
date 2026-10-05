using System.Collections.Generic;
using SsisAiRuntime.Core;

namespace SsisAiRuntime.Inspectors
{
    public interface IPackageDataFlowInspector<TPackage> where TPackage : class
    {
        IReadOnlyList<DataFlowOverview> Inspect(PackageSession<TPackage> session);
    }
}