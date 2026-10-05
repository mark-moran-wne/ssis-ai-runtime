using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using SsisAiRuntime.Core;
using SsisAiRuntime.Inspectors;
using DtsRuntime = Microsoft.SqlServer.Dts.Runtime;

namespace SsisAiRuntime.Ssis16
{
    public sealed class PackageExpressionInspector : IPackageExpressionInspector<DtsRuntime.Package>
    {
        public IReadOnlyList<ExpressionOverview> Inspect(PackageSession<DtsRuntime.Package> session)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            var expressions = new List<ExpressionOverview>();
            var package = session.Package;
            AddExpression(expressions, "Package", package.ID, package.Name, package.CreationName, package.HasExpressions);

            foreach (DtsRuntime.ConnectionManager connection in package.Connections)
            {
                AddExpression(
                    expressions,
                    "Connection",
                    connection.ID,
                    connection.Name,
                    connection.CreationName,
                    HasExpressions(connection));
            }

            foreach (DtsRuntime.Variable variable in package.Variables)
            {
                AddExpression(
                    expressions,
                    "Variable",
                    variable.ID,
                    variable.Name,
                    variable.CreationName,
                    !string.IsNullOrWhiteSpace(variable.Expression));
            }

            AddExecutables(package.Executables, expressions);
            return new ReadOnlyCollection<ExpressionOverview>(expressions);
        }

        public InspectionResult<ExpressionOverview> InspectDetailed(PackageSession<DtsRuntime.Package> session)
        {
            var expressions = Inspect(session);
            var unsupportedItems = new List<UnsupportedItem>();
            foreach (var expression in expressions)
            {
                unsupportedItems.Add(new UnsupportedItem(
                    expression.ObjectId,
                    expression.ObjectName,
                    expression.CreationName,
                    "The adapter reports expression presence but does not expose property association or expression text."));
            }

            return new InspectionResult<ExpressionOverview>(expressions, unsupportedItems);
        }

        private static void AddExecutables(
            DtsRuntime.Executables executables,
            ICollection<ExpressionOverview> expressions)
        {
            foreach (DtsRuntime.Executable executable in executables)
            {
                var metadata = (DtsRuntime.IDTSName)executable;
                AddExpression(
                    expressions,
                    executable is DtsRuntime.IDTSSequence ? "Container" : "Task",
                    metadata.ID,
                    metadata.Name,
                    metadata.CreationName,
                    HasExpressions(executable));

                if (executable is DtsRuntime.IDTSSequence sequence)
                {
                    AddExecutables(sequence.Executables, expressions);
                }
            }
        }

        private static bool HasExpressions(object value)
        {
            return value is DtsRuntime.IDTSPropertiesProviderEx provider && provider.HasExpressions;
        }

        private static void AddExpression(
            ICollection<ExpressionOverview> expressions,
            string objectType,
            string objectId,
            string objectName,
            string creationName,
            bool hasExpressions)
        {
            if (hasExpressions)
            {
                expressions.Add(new ExpressionOverview(objectType, objectId, objectName, creationName));
            }
        }
    }
}