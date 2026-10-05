using System;
using System.Collections.Generic;
using System.Globalization;
using SsisAiRuntime.Inspectors;
using SsisAiRuntime.Inspectors.Expressions;
using DtsRuntime = Microsoft.SqlServer.Dts.Runtime;
using Pipeline = Microsoft.SqlServer.Dts.Pipeline.Wrapper;

namespace SsisAiRuntime.Ssis16
{
    internal sealed class NativeExpressionDependencyInputBuilder
    {
        internal IReadOnlyList<ExpressionDependencyInput> Build(DtsRuntime.Package package, ICollection<UnsupportedItem> coverage)
        {
            var inputs = new List<ExpressionDependencyInput>();
            foreach (var container in NativeExpressionScopeCatalogBuilder.Containers(package))
            {
                Read(container.ID, "PropertyExpression", coverage, () => AddProperties(container, container.ID, container.ID, "PropertyExpression", inputs));
                Read(container.ID, "VariableExpression", coverage, () =>
                {
                    foreach (DtsRuntime.Variable variable in container.Variables)
                    {
                        if (variable.Parent is DtsRuntime.IDTSName parent && parent.ID == container.ID && variable.EvaluateAsExpression)
                        { Add(inputs, variable.ID, container.ID, "VariableExpression", variable.Expression); }
                    }
                });
                if (container is DtsRuntime.ForLoop loop)
                {
                    Read(container.ID, "LoopExpression", coverage, () =>
                    {
                        Add(inputs, loop.ID, loop.ID, "LoopExpression", loop.InitExpression);
                        Add(inputs, loop.ID, loop.ID, "LoopExpression", loop.EvalExpression);
                        Add(inputs, loop.ID, loop.ID, "LoopExpression", loop.AssignExpression);
                    });
                }
                if (container is DtsRuntime.IDTSSequence sequence)
                {
                    Read(container.ID, "ConstraintExpression", coverage, () =>
                    {
                        foreach (DtsRuntime.PrecedenceConstraint constraint in sequence.PrecedenceConstraints)
                        { Add(inputs, constraint.ID, container.ID, "ConstraintExpression", constraint.Expression); }
                    });
                }
                if (container is DtsRuntime.TaskHost task && task.InnerObject is Pipeline.IDTSPipeline100 pipeline)
                {
                    Read(container.ID, "DataFlowExpression", coverage, () =>
                    {
                        foreach (Pipeline.IDTSComponentMetaData100 component in pipeline.ComponentMetaDataCollection)
                        {
                            if (!PackageDataFlowInspector.IsDerivedColumnClass(component.ComponentClassID)) { continue; }
                            var ownerId = component.ID.ToString(CultureInfo.InvariantCulture);
                            foreach (Pipeline.IDTSInput100 input in component.InputCollection)
                            {
                                foreach (Pipeline.IDTSInputColumn100 column in input.InputColumnCollection)
                                { AddPipelineProperties(column.CustomPropertyCollection, ownerId, task.ID, inputs, coverage); }
                            }
                            foreach (Pipeline.IDTSOutput100 output in component.OutputCollection)
                            {
                                if (output.IsErrorOut) { continue; }
                                foreach (Pipeline.IDTSOutputColumn100 column in output.OutputColumnCollection)
                                { AddPipelineProperties(column.CustomPropertyCollection, ownerId, task.ID, inputs, coverage); }
                            }
                        }
                    });
                }
            }
            foreach (DtsRuntime.ConnectionManager connection in package.Connections)
            { Read(connection.ID, "ConnectionExpression", coverage, () => AddProperties(connection, connection.ID, package.ID, "ConnectionExpression", inputs)); }
            return inputs;
        }

        private static void AddProperties(object owner, string id, string scopeId, string category, ICollection<ExpressionDependencyInput> inputs)
        {
            if (!(owner is DtsRuntime.IDTSPropertiesProvider provider)) { return; }
            foreach (DtsRuntime.DtsProperty property in provider.Properties)
            { Add(inputs, id, scopeId, category, provider.GetExpression(property.Name)); }
        }

        private static void AddPipelineProperties(Pipeline.IDTSCustomPropertyCollection100 properties, string ownerId, string scopeId,
            ICollection<ExpressionDependencyInput> inputs, ICollection<UnsupportedItem> coverage)
        {
            foreach (Pipeline.IDTSCustomProperty100 property in properties)
            {
                if (!string.Equals(property.Name, "Expression", StringComparison.OrdinalIgnoreCase)) { continue; }
                if (property.EncryptionRequired)
                { coverage.Add(Gap(ownerId, "DataFlowExpression")); continue; }
                Add(inputs, ownerId, scopeId, "DataFlowExpression", property.Value as string);
            }
        }
        private static void Add(ICollection<ExpressionDependencyInput> inputs, string id, string scopeId, string category, string text)
        { if (!string.IsNullOrWhiteSpace(text)) { inputs.Add(new ExpressionDependencyInput(id, scopeId, category, text)); } }
        private static void Read(string id, string category, ICollection<UnsupportedItem> coverage, Action read)
        { try { read(); } catch { coverage.Add(Gap(id, category)); } }
        private static UnsupportedItem Gap(string id, string category) => new UnsupportedItem(id, category, "ExpressionDependency",
            "Expression metadata could not be projected.", "expression.target_not_projected");
    }
}