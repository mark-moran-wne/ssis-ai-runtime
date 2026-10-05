using System;
using System.Collections.Generic;
using System.Linq;
using SsisAiRuntime.Inspectors.Expressions;
using DtsRuntime = Microsoft.SqlServer.Dts.Runtime;

namespace SsisAiRuntime.Ssis16
{
    public sealed class NativeExpressionScopeCatalogBuilder
    {
        public ExpressionScopeCatalog Build(DtsRuntime.Package package, IEnumerable<ExpressionSymbol> projectParameters = null)
        {
            if (package == null) { throw new ArgumentNullException(nameof(package)); }
            var scopes = new List<ExpressionScope>();
            foreach (var container in Containers(package))
            {
                var symbols = new List<ExpressionSymbol>();
                foreach (DtsRuntime.Variable variable in container.Variables)
                {
                    var parent = variable.Parent as DtsRuntime.IDTSName;
                    if (parent == null) { throw new ExpressionScopeCatalogException("expression.owner_scope_missing"); }
                    if (parent.ID != container.ID) { continue; }
                    symbols.Add(new ExpressionSymbol(variable.ID, container.ID,
                        variable.SystemVariable ? ExpressionSymbolKind.SystemVariable : ExpressionSymbolKind.Variable,
                        variable.Namespace, variable.Name));
                }
                if (container == package)
                {
                    foreach (DtsRuntime.Parameter parameter in package.Parameters)
                    { symbols.Add(new ExpressionSymbol(parameter.ID, package.ID, ExpressionSymbolKind.PackageParameter, "$Package", parameter.Name)); }
                    if (projectParameters != null)
                    {
                        var supplied = projectParameters.ToArray();
                        if (supplied.Any(symbol => symbol == null || symbol.Kind != ExpressionSymbolKind.ProjectParameter || symbol.ScopeId != package.ID))
                        { throw new ExpressionScopeCatalogException("expression.target_not_projected"); }
                        symbols.AddRange(supplied);
                    }
                }
                var parentId = container == package ? string.Empty : ((DtsRuntime.IDTSName)container.Parent).ID;
                scopes.Add(new ExpressionScope(container.ID, parentId, container.Name, ScopeKind(container), symbols));
            }
            return new ExpressionScopeCatalog(scopes, projectParameters != null);
        }

        internal static IEnumerable<DtsRuntime.DtsContainer> Containers(DtsRuntime.DtsContainer root)
        {
            yield return root;
            if (root is DtsRuntime.IDTSSequence sequence)
            {
                foreach (DtsRuntime.Executable executable in sequence.Executables)
                {
                    if (!(executable is DtsRuntime.DtsContainer child))
                    { throw new ExpressionScopeCatalogException("expression.owner_scope_missing"); }
                    foreach (var descendant in Containers(child)) { yield return descendant; }
                }
            }
            if (root is DtsRuntime.EventsProvider eventsProvider)
            {
                foreach (DtsRuntime.DtsEventHandler handler in eventsProvider.EventHandlers)
                { foreach (var descendant in Containers(handler)) { yield return descendant; } }
            }
        }

        private static ExpressionScopeKind ScopeKind(DtsRuntime.DtsContainer container)
        {
            if (container is DtsRuntime.Package) { return ExpressionScopeKind.Package; }
            if (container is DtsRuntime.DtsEventHandler) { return ExpressionScopeKind.EventHandler; }
            if (container is DtsRuntime.Sequence) { return ExpressionScopeKind.Sequence; }
            if (container is DtsRuntime.ForLoop) { return ExpressionScopeKind.ForLoop; }
            if (container is DtsRuntime.ForEachLoop) { return ExpressionScopeKind.ForeachLoop; }
            return container is DtsRuntime.TaskHost ? ExpressionScopeKind.Task : ExpressionScopeKind.Unknown;
        }
    }
}