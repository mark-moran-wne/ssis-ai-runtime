using System;
using System.Collections.Generic;
using SsisAiRuntime.Core;
using DtsRuntime = Microsoft.SqlServer.Dts.Runtime;

namespace SsisAiRuntime.Ssis16
{
    public sealed class PackageLoader : IPackageLoader<DtsRuntime.Package>
    {
        public PackageLoadResult<DtsRuntime.Package> Load(string packagePath)
        {
            var diagnostics = new List<RuntimeDiagnostic>();

            if (string.IsNullOrWhiteSpace(packagePath))
            {
                diagnostics.Add(new RuntimeDiagnostic(
                    "package.path.required",
                    RuntimeDiagnosticSeverity.Error,
                    "A package path is required."));
                return PackageLoadResult<DtsRuntime.Package>.Failure(CreateDiagnostics(diagnostics));
            }

            try
            {
                var application = new DtsRuntime.Application();
                var package = application.LoadPackage(packagePath, null);
                if (package == null)
                {
                    diagnostics.Add(new RuntimeDiagnostic(
                        "ssis.package.load_empty",
                        RuntimeDiagnosticSeverity.Error,
                        "The SSIS runtime returned no package."));
                    return PackageLoadResult<DtsRuntime.Package>.Failure(CreateDiagnostics(diagnostics));
                }

                var session = new PackageSession<DtsRuntime.Package>(packagePath, package.Name, package);
                return PackageLoadResult<DtsRuntime.Package>.Success(session, CreateDiagnostics(diagnostics));
            }
            catch (Exception)
            {
                diagnostics.Add(new RuntimeDiagnostic(
                    "ssis.package.load_failed",
                    RuntimeDiagnosticSeverity.Error,
                    "The SSIS runtime failed to load the package. Exception details and package paths are omitted to avoid exposing sensitive data."));
                return PackageLoadResult<DtsRuntime.Package>.Failure(CreateDiagnostics(diagnostics));
            }
        }

        private static RuntimeDiagnostics CreateDiagnostics(IEnumerable<RuntimeDiagnostic> items)
        {
            var runtimeVersion = typeof(DtsRuntime.Application).Assembly.GetName().Version;
            return new RuntimeDiagnostics(
                runtimeVersion == null ? string.Empty : runtimeVersion.ToString(),
                Environment.Is64BitProcess ? "x64" : "x86",
                items);
        }
    }
}