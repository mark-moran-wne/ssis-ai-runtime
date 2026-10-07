using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using SsisAiRuntime.Core;
using SsisAiRuntime.Inspectors;
using DtsRuntime = Microsoft.SqlServer.Dts.Runtime;

namespace SsisAiRuntime.Ssis16
{
    public sealed class PackageExecutableInspector : IPackageExecutableInspector<DtsRuntime.Package>
    {
        public IReadOnlyList<ExecutableOverview> Inspect(PackageSession<DtsRuntime.Package> session)
        {
            return InspectDetailed(session).Items;
        }

        public InspectionResult<ExecutableOverview> InspectDetailed(PackageSession<DtsRuntime.Package> session)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            var overviews = new List<ExecutableOverview>();
            var unsupportedItems = new List<UnsupportedItem>();
            AddExecutables(session.Package.Executables, string.Empty, 0, overviews, unsupportedItems);
            AddEventHandlers(session.Package, string.Empty, 0, overviews, unsupportedItems);
            return new InspectionResult<ExecutableOverview>(overviews, unsupportedItems);
        }

        private static void AddExecutables(
            DtsRuntime.Executables executables,
            string parentId,
            int depth,
            ICollection<ExecutableOverview> overviews,
            ICollection<UnsupportedItem> unsupportedItems)
        {
            foreach (DtsRuntime.Executable executable in executables)
            {
                var metadata = (DtsRuntime.IDTSName)executable;
                var sequence = executable as DtsRuntime.IDTSSequence;
                var expressionProvider = executable as DtsRuntime.IDTSPropertiesProviderEx;
                overviews.Add(new ExecutableOverview(
                    metadata.ID,
                    parentId,
                    metadata.Name,
                    metadata.CreationName,
                    metadata.Description,
                    depth,
                    sequence != null,
                    expressionProvider != null && expressionProvider.HasExpressions));

                if (sequence != null)
                {
                    AddExecutables(sequence.Executables, metadata.ID, depth + 1, overviews, unsupportedItems);
                }
                else
                {
                    unsupportedItems.Add(new UnsupportedItem(
                        metadata.ID,
                        metadata.Name,
                        metadata.CreationName,
                        "Task-specific properties are not inspected yet.", UnsupportedItem.TaskPropertiesNotInspectedCode));
                }
                if (executable is DtsRuntime.EventsProvider eventsProvider)
                {
                    AddEventHandlers(eventsProvider, metadata.ID, depth + 1, overviews, unsupportedItems);
                }
            }
        }

        private static void AddEventHandlers(DtsRuntime.EventsProvider provider, string parentId, int depth,
            ICollection<ExecutableOverview> overviews, ICollection<UnsupportedItem> unsupportedItems)
        {
            foreach (DtsRuntime.DtsEventHandler handler in provider.EventHandlers)
            {
                overviews.Add(new ExecutableOverview(handler.ID, parentId, handler.Name, handler.CreationName,
                    handler.Description, depth, true, handler.HasExpressions));
                AddExecutables(handler.Executables, handler.ID, depth + 1, overviews, unsupportedItems);
            }
        }
    }
}