using System.Collections.Generic;
using SsisAiRuntime.Core;

namespace SsisAiRuntime.Inspectors
{
    public interface IPackagePrecedenceInspector<TPackage> where TPackage : class
    {
        IReadOnlyList<PrecedenceConstraintOverview> Inspect(PackageSession<TPackage> session);
    }
}