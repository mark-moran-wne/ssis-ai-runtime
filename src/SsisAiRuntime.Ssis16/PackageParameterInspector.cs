using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using SsisAiRuntime.Core;
using SsisAiRuntime.Inspectors;
using DtsRuntime = Microsoft.SqlServer.Dts.Runtime;

namespace SsisAiRuntime.Ssis16
{
    public sealed class PackageParameterInspector : IPackageParameterInspector<DtsRuntime.Package>
    {
        public IReadOnlyList<ParameterOverview> Inspect(PackageSession<DtsRuntime.Package> session)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            var parameters = new List<ParameterOverview>();
            foreach (DtsRuntime.Parameter parameter in session.Package.Parameters)
            {
                parameters.Add(new ParameterOverview(
                    parameter.ID,
                    parameter.Name,
                    parameter.CreationName,
                    parameter.Description,
                    parameter.DataType.ToString(),
                    parameter.Required,
                    parameter.Sensitive));
            }

            return new ReadOnlyCollection<ParameterOverview>(parameters);
        }

        public InspectionResult<ParameterOverview> InspectDetailed(PackageSession<DtsRuntime.Package> session)
        {
            return InspectionResult<ParameterOverview>.Complete(Inspect(session));
        }
    }
}