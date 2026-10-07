using System;
using System.Linq;
using SsisAiRuntime.AI;
using SsisAiRuntime.Inspectors;

namespace SsisAiRuntime.Mutations
{
    public static class RenameCoveragePolicy
    {
        public static bool CanVerifyRename(PackageAnalysisSnapshot snapshot)
        {
            if (snapshot == null) { throw new ArgumentNullException(nameof(snapshot)); }
            return snapshot.UnsupportedItems.Concat(snapshot.Dependencies.UnsupportedItems)
                .Concat(snapshot.ControlFlow.UnsupportedItems).All(gap =>
                    gap.ReasonCode == UnsupportedItem.TaskPropertiesNotInspectedCode &&
                    snapshot.Executables.Any(task => task.Id == gap.Id && task.CreationName == gap.CreationName));
        }
    }
}