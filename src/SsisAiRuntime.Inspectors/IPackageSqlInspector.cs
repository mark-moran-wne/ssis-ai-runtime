using System.Collections.Generic;
using SsisAiRuntime.Core;

namespace SsisAiRuntime.Inspectors
{
    public interface IPackageSqlInspector<TPackage> where TPackage : class
    {
        IReadOnlyList<SqlStatementOverview> Inspect(PackageSession<TPackage> session);
    }
}