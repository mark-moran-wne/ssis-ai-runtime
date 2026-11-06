using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Newtonsoft.Json;

namespace SsisAiRuntime.Mutations
{
    public sealed class MutationExecutionRequirements
    {
        public MutationExecutionRequirements(bool checkpointRequired, bool saveAsRequired,
            bool reloadRequired, bool validationRequired, bool semanticDiffRequired,
            IEnumerable<string> requirements, bool requiresExistingSource = true)
        {
            CheckpointRequired = checkpointRequired;
            SaveAsRequired = saveAsRequired;
            ReloadRequired = reloadRequired;
            ValidationRequired = validationRequired;
            SemanticDiffRequired = semanticDiffRequired;
            RequiresExistingSource = requiresExistingSource;
            Requirements = new ReadOnlyCollection<string>(
                (requirements ?? throw new ArgumentNullException(nameof(requirements)))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(item => item, StringComparer.Ordinal)
                    .ToArray());
        }

        public bool CheckpointRequired { get; }
        public bool SaveAsRequired { get; }
        public bool ReloadRequired { get; }
        public bool ValidationRequired { get; }
        public bool SemanticDiffRequired { get; }
        public bool RequiresExistingSource { get; }
        public IReadOnlyList<string> Requirements { get; }
        [JsonIgnore]
        public bool RequiresLifecycle => CheckpointRequired || SaveAsRequired || ReloadRequired ||
            ValidationRequired || SemanticDiffRequired;
    }

    public static class MutationExecutionPolicies
    {
        public static MutationExecutionRequirements RenameTask() => new MutationExecutionRequirements(
            checkpointRequired: true,
            saveAsRequired: true,
            reloadRequired: true,
            validationRequired: true,
            semanticDiffRequired: true,
            requirements: new[]
            {
                "checkpoint.required",
                "saveas.required",
                "reload.required",
                "validation.required",
                "semanticdiff.required"
            });

        public static MutationExecutionRequirements ResizeColumn(bool dataLossAcknowledgementRequired) => new MutationExecutionRequirements(
            checkpointRequired: true,
            saveAsRequired: true,
            reloadRequired: true,
            validationRequired: true,
            semanticDiffRequired: true,
            requirements: new[]
            {
                "checkpoint.required",
                "saveas.required",
                "reload.required",
                "validation.required",
                "semanticdiff.required",
                "columnwidth.coordinated",
                "columnwidth.resize"
            }.Concat(dataLossAcknowledgementRequired
                ? new[] { "columnwidth.data_loss.acknowledged" }
                : Array.Empty<string>()));

        public static MutationExecutionRequirements ComponentInsertion() => new MutationExecutionRequirements(
            checkpointRequired: true,
            saveAsRequired: true,
            reloadRequired: true,
            validationRequired: true,
            semanticDiffRequired: true,
            requirements: new[]
            {
                "checkpoint.required",
                "saveas.required",
                "reload.required",
                "validation.required",
                "semanticdiff.required",
                "componentinsert.placement.required",
                "componentinsert.layout_preservation.required"
            });

        public static MutationExecutionRequirements TransferTablesUpdate() => new MutationExecutionRequirements(
            checkpointRequired: true,
            saveAsRequired: true,
            reloadRequired: true,
            validationRequired: true,
            semanticDiffRequired: true,
            requirements: new[]
            {
                "checkpoint.required",
                "saveas.required",
                "reload.required",
                "validation.required",
                "semanticdiff.required",
                "transfertask.tables.selection.required",
                "transfertask.other_settings.preserved"
            });

        public static MutationExecutionRequirements TaskPropertyUpdate(bool collectionUpdate) => new MutationExecutionRequirements(
            checkpointRequired: true,
            saveAsRequired: true,
            reloadRequired: true,
            validationRequired: true,
            semanticDiffRequired: true,
            requirements: new[]
            {
                "checkpoint.required",
                "saveas.required",
                "reload.required",
                "validation.required",
                "semanticdiff.required",
                collectionUpdate ? "taskproperty.collection.update" : "taskproperty.scalar.update",
                "taskproperty.other_settings.preserved"
            });

        public static MutationExecutionRequirements VariableMetadataUpdate() => new MutationExecutionRequirements(
            checkpointRequired: true,
            saveAsRequired: true,
            reloadRequired: true,
            validationRequired: true,
            semanticDiffRequired: true,
            requirements: new[]
            {
                "checkpoint.required",
                "saveas.required",
                "reload.required",
                "validation.required",
                "semanticdiff.required",
                "variable.metadata.description.only",
                "variable.metadata.other_variables_preserved"
            });

        public static MutationExecutionRequirements VariableExpressionUpdate() => new MutationExecutionRequirements(
            checkpointRequired: true,
            saveAsRequired: true,
            reloadRequired: true,
            validationRequired: true,
            semanticDiffRequired: true,
            requirements: new[]
            {
                "checkpoint.required",
                "saveas.required",
                "reload.required",
                "validation.required",
                "semanticdiff.required",
                "variable.expression.package_scope_only",
                "variable.expression.references_resolved",
                "variable.expression.other_variables_preserved"
            });

        public static MutationExecutionRequirements VariableValueUpdate() => new MutationExecutionRequirements(
            checkpointRequired: true,
            saveAsRequired: true,
            reloadRequired: true,
            validationRequired: true,
            semanticDiffRequired: true,
            requirements: new[]
            {
                "checkpoint.required",
                "saveas.required",
                "reload.required",
                "validation.required",
                "semanticdiff.required",
                "variable.value.trusted_environment_input",
                "variable.value.other_metadata_preserved"
            });
    }
}