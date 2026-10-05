namespace SsisAiRuntime.Core
{
    public interface IPackageLoader<TPackage> where TPackage : class
    {
        PackageLoadResult<TPackage> Load(string packagePath);
    }
}