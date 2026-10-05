using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using SsisAiRuntime.Core;
using SsisAiRuntime.Inspectors;
using DtsRuntime = Microsoft.SqlServer.Dts.Runtime;

namespace SsisAiRuntime.Ssis16
{
    public sealed class PackagePrecedenceInspector : IPackagePrecedenceInspector<DtsRuntime.Package>
    {
        public IReadOnlyList<PrecedenceConstraintOverview> Inspect(PackageSession<DtsRuntime.Package> session)
        {
            return InspectDetailed(session).Items;
        }

        public InspectionResult<PrecedenceConstraintOverview> InspectDetailed(PackageSession<DtsRuntime.Package> session)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            var constraints = new List<PrecedenceConstraintOverview>();
            var unsupportedItems = new List<UnsupportedItem>();
            AddConstraintCollection(session.Package.PrecedenceConstraints, constraints, unsupportedItems);
            AddNestedConstraints(session.Package.Executables, constraints, unsupportedItems);
            return new InspectionResult<PrecedenceConstraintOverview>(constraints, unsupportedItems);
        }

        private static void AddNestedConstraints(
            DtsRuntime.Executables executables,
            ICollection<PrecedenceConstraintOverview> constraints,
            ICollection<UnsupportedItem> unsupportedItems)
        {
            foreach (DtsRuntime.Executable executable in executables)
            {
                if (executable is DtsRuntime.IDTSSequence sequence)
                {
                    AddConstraintCollection(sequence.PrecedenceConstraints, constraints, unsupportedItems);
                    AddNestedConstraints(sequence.Executables, constraints, unsupportedItems);
                }
            }
        }

        private static void AddConstraintCollection(
            DtsRuntime.PrecedenceConstraints collection,
            ICollection<PrecedenceConstraintOverview> constraints,
            ICollection<UnsupportedItem> unsupportedItems)
        {
            foreach (DtsRuntime.PrecedenceConstraint constraint in collection)
            {
                var from = constraint.PrecedenceExecutable as DtsRuntime.IDTSName;
                var to = constraint.ConstrainedExecutable as DtsRuntime.IDTSName;
                if (from == null || to == null)
                {
                    unsupportedItems.Add(new UnsupportedItem(
                        constraint.ID,
                        constraint.Name,
                        constraint.CreationName,
                        "A precedence constraint endpoint did not expose stable executable identity."));
                    continue;
                }

                var hasExpression = !string.IsNullOrWhiteSpace(constraint.Expression);
                constraints.Add(new PrecedenceConstraintOverview(
                    constraint.ID,
                    constraint.Name,
                    from.ID,
                    from.Name,
                    to.ID,
                    to.Name,
                    constraint.EvalOp.ToString(),
                    constraint.Value.ToString(),
                    constraint.LogicalAnd,
                    hasExpression));

                if (hasExpression)
                {
                    unsupportedItems.Add(new UnsupportedItem(
                        constraint.ID,
                        constraint.Name,
                        constraint.CreationName,
                        "The precedence expression is present but its text is omitted."));
                }
            }
        }
    }
}