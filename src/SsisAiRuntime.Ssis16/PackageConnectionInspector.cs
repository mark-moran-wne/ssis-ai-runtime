using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using SsisAiRuntime.Core;
using SsisAiRuntime.Inspectors;
using DtsRuntime = Microsoft.SqlServer.Dts.Runtime;

namespace SsisAiRuntime.Ssis16
{
    public sealed class PackageConnectionInspector : IPackageConnectionInspector<DtsRuntime.Package>
    {
        public IReadOnlyList<ConnectionOverview> Inspect(PackageSession<DtsRuntime.Package> session)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            var connections = new List<ConnectionOverview>();
            foreach (DtsRuntime.ConnectionManager connection in session.Package.Connections)
            {
                connections.Add(new ConnectionOverview(
                    connection.Name,
                    connection.ID,
                    connection.CreationName));
            }

            return new ReadOnlyCollection<ConnectionOverview>(connections);
        }

        public InspectionResult<ConnectionOverview> InspectDetailed(PackageSession<DtsRuntime.Package> session)
        {
            return InspectionResult<ConnectionOverview>.Complete(Inspect(session));
        }
    }
}