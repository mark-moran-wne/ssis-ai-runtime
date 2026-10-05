using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using SsisAiRuntime.Core;
using SsisAiRuntime.Inspectors;
using DtsRuntime = Microsoft.SqlServer.Dts.Runtime;

namespace SsisAiRuntime.Ssis16
{
    public sealed class PackageVariableInspector : IPackageVariableInspector<DtsRuntime.Package>
    {
        public IReadOnlyList<VariableOverview> Inspect(PackageSession<DtsRuntime.Package> session)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            var variables = new List<VariableOverview>();
            foreach (DtsRuntime.Variable variable in session.Package.Variables)
            {
                variables.Add(new VariableOverview(
                    variable.Name,
                    variable.Namespace,
                    variable.DataType.ToString(),
                    variable.ReadOnly,
                    variable.SystemVariable,
                    variable.EvaluateAsExpression,
                    !string.IsNullOrWhiteSpace(variable.Expression)));
            }

            return new ReadOnlyCollection<VariableOverview>(variables);
        }

        public InspectionResult<VariableOverview> InspectDetailed(PackageSession<DtsRuntime.Package> session)
        {
            return InspectionResult<VariableOverview>.Complete(Inspect(session));
        }
    }
}