using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SsisAiRuntime.Mutations
{
    public sealed class MutationExecutionRequirements
    {
        public MutationExecutionRequirements(bool checkpointRequired, bool saveAsRequired,
            bool reloadRequired, bool validationRequired, bool semanticDiffRequired,
            IEnumerable<string> requirements)
        {
            CheckpointRequired = checkpointRequired;
            SaveAsRequired = saveAsRequired;
            ReloadRequired = reloadRequired;
            ValidationRequired = validationRequired;
            SemanticDiffRequired = semanticDiffRequired;
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
        public IReadOnlyList<string> Requirements { get; }
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
    }
}