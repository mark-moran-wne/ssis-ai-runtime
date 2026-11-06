using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using SsisAiRuntime.Core;
using SsisAiRuntime.Inspectors;
using DtsRuntime = Microsoft.SqlServer.Dts.Runtime;

namespace SsisAiRuntime.Ssis16
{
    public sealed class PackageVariableInspector : IPackageVariableInspector<DtsRuntime.Package>
    {
        public IReadOnlyList<VariableOverview> Inspect(PackageSession<DtsRuntime.Package> session)
        {
            return InspectDetailed(session).Items;
        }

        public InspectionResult<VariableOverview> InspectDetailed(PackageSession<DtsRuntime.Package> session)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            var variables = new List<VariableOverview>();
            var unsupported = new List<UnsupportedItem>();
            DtsRuntime.DtsContainer[] scopes;
            try { scopes = NativeExpressionScopeCatalogBuilder.Containers(session.Package).ToArray(); }
            catch
            {
                unsupported.Add(new UnsupportedItem(session.Package.ID, session.Package.Name, "VariableScope",
                    "Package variable scopes could not be enumerated.", UnsupportedItem.ReadFailureCode));
                return new InspectionResult<VariableOverview>(variables, unsupported);
            }

            foreach (var scope in scopes)
            {
                try
                {
                    foreach (DtsRuntime.Variable variable in scope.Variables)
                    {
                        if (!(variable.Parent is DtsRuntime.IDTSName parent))
                        {
                            unsupported.Add(new UnsupportedItem(variable.ID, variable.Name, "Variable",
                                "Variable scope ownership could not be established.", UnsupportedItem.ReadFailureCode));
                            continue;
                        }
                        if (parent.ID != scope.ID) { continue; }
                        variables.Add(new VariableOverview(
                            variable.Name,
                            variable.Namespace,
                            variable.DataType.ToString(),
                            variable.ReadOnly,
                            variable.SystemVariable,
                            variable.EvaluateAsExpression,
                            !string.IsNullOrWhiteSpace(variable.Expression),
                            variable.ID,
                            scope is DtsRuntime.Package ? "Package" : scope.Name,
                            "Unclassified",
                            scope.ID));
                    }
                }
                catch
                {
                    unsupported.Add(new UnsupportedItem(scope.ID, scope.Name, scope.GetType().Name,
                        "Variables in this scope could not be inspected.", UnsupportedItem.ReadFailureCode));
                }
            }
            return new InspectionResult<VariableOverview>(variables, unsupported);
        }
    }
}