using System;
using SsisAiRuntime.Core;
using SsisAiRuntime.Inspectors;
using DtsRuntime = Microsoft.SqlServer.Dts.Runtime;

namespace SsisAiRuntime.Ssis16
{
    public sealed class PackageOverviewInspector : IPackageOverviewInspector<DtsRuntime.Package>
    {
        public PackageOverview Inspect(PackageSession<DtsRuntime.Package> session)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            var package = session.Package;
            return new PackageOverview(
                session.SessionId,
                package.Name,
                package.ID,
                package.Description,
                package.CreationDate,
                package.VersionMajor,
                package.VersionMinor,
                package.VersionBuild,
                package.ProtectionLevel.ToString(),
                package.PackageType.ToString(),
                package.Connections.Count,
                package.Variables.Count,
                package.Executables.Count,
                package.PrecedenceConstraints.Count,
                package.Parameters.Count,
                package.HasExpressions);
        }
    }
}