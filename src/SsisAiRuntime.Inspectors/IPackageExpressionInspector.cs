using System.Collections.Generic;
using SsisAiRuntime.Core;

namespace SsisAiRuntime.Inspectors
{
    public interface IPackageExpressionInspector<TPackage> where TPackage : class
    {
        IReadOnlyList<ExpressionOverview> Inspect(PackageSession<TPackage> session);
    }
}