using SsisAiRuntime.Core;

namespace SsisAiRuntime.Inspectors
{
    public interface IPackageOverviewInspector<TPackage> where TPackage : class
    {
        PackageOverview Inspect(PackageSession<TPackage> session);
    }
}