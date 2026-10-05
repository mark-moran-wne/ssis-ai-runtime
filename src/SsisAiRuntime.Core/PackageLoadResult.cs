using System;

namespace SsisAiRuntime.Core
{
    public sealed class PackageLoadResult<TPackage> where TPackage : class
    {
        private PackageLoadResult(PackageSession<TPackage> session, RuntimeDiagnostics diagnostics)
        {
            Session = session;
            Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            Succeeded = session != null;

            if (Succeeded == diagnostics.HasErrors)
            {
                throw new ArgumentException("A load result must have either a session or error diagnostics, but not both.");
            }
        }

        public bool Succeeded { get; }

        public PackageSession<TPackage> Session { get; }

        public RuntimeDiagnostics Diagnostics { get; }

        public static PackageLoadResult<TPackage> Success(
            PackageSession<TPackage> session,
            RuntimeDiagnostics diagnostics)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            return new PackageLoadResult<TPackage>(session, diagnostics);
        }

        public static PackageLoadResult<TPackage> Failure(RuntimeDiagnostics diagnostics)
        {
            return new PackageLoadResult<TPackage>(null, diagnostics);
        }
    }
}