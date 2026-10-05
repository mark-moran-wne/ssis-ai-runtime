using SsisAiRuntime.Core;
using SsisAiRuntime.Inspectors.Expressions;

namespace SsisAiRuntime.Inspectors
{
    public interface IPackageExpressionDependencyInspector<TPackage> where TPackage : class
    {
        ExpressionDependencyInspection Inspect(PackageSession<TPackage> session);
    }
}