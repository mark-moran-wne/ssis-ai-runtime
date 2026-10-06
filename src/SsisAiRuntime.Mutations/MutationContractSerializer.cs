using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SsisAiRuntime.Inspectors;

namespace SsisAiRuntime.Mutations
{
    public static class MutationContractSchema
    {
        public const string CurrentVersion = "1.1";

        public static bool IsCompatible(string version)
        {
            if (string.IsNullOrWhiteSpace(version)) { return false; }
            var parts = version.Split('.');
            if (parts.Length != 2) { return false; }
            int major;
            int minor;
            return int.TryParse(parts[0], out major) && int.TryParse(parts[1], out minor) &&
                major == 1 && minor >= 0;
        }
    }

    public static class MutationContractSerializer
    {
        public static string Serialize(MutationRequest contract)
        {
            if (contract == null) { throw new ArgumentNullException(nameof(contract)); }
            return Document("MutationRequest", new JObject
            {
                ["kind"] = contract.Kind.ToString(),
                ["target"] = Target(contract.Target),
                ["proposedName"] = contract.ProposedName
            });
        }

        public static string Serialize(MutationPreview contract)
        {
            if (contract == null) { throw new ArgumentNullException(nameof(contract)); }
            return Document("MutationPreview", new JObject
            {
                ["status"] = contract.Status.ToString(),
                ["request"] = Request(contract.Request),
                ["currentName"] = contract.CurrentName,
                ["proposedName"] = contract.ProposedName,
                ["impact"] = contract.Impact == null ? null : Impact(contract.Impact),
                ["executionPlan"] = contract.ExecutionPlan == null ? null : Plan(contract.ExecutionPlan),
                ["coverageGaps"] = new JArray(contract.CoverageGaps.Select(Coverage))
            });
        }

        public static string Serialize(MutationExecutionPlan contract)
        {
            if (contract == null) { throw new ArgumentNullException(nameof(contract)); }
            return Document("MutationExecutionPlan", Plan(contract));
        }

        public static string Serialize(MutationResult contract)
        {
            if (contract == null) { throw new ArgumentNullException(nameof(contract)); }
            return Document("MutationResult", new JObject
            {
                ["status"] = contract.Status.ToString(),
                ["code"] = contract.Code
            });
        }

        private static string Document(string contractType, JObject payload) => new JObject
        {
            ["schemaVersion"] = MutationContractSchema.CurrentVersion,
            ["contractType"] = contractType,
            ["payload"] = payload
        }.ToString(Formatting.None);

        private static JObject Request(MutationRequest request) => new JObject
        {
            ["kind"] = request.Kind.ToString(),
            ["target"] = Target(request.Target),
            ["proposedName"] = request.ProposedName
        };

        private static JObject Target(MutationTarget target) => new JObject
        {
            ["kind"] = target.Kind.ToString(),
            ["nativeId"] = target.NativeId
        };

        private static JObject Plan(MutationExecutionPlan plan) => new JObject
        {
            ["request"] = Request(plan.Request),
            ["expectedCurrentName"] = plan.ExpectedCurrentName,
            ["proposedName"] = plan.ProposedName,
            ["impact"] = Impact(plan.Impact),
            ["requirements"] = Requirements(plan.Requirements)
        };

        private static JObject Requirements(MutationExecutionRequirements requirements) => new JObject
        {
            ["checkpointRequired"] = requirements.CheckpointRequired,
            ["saveAsRequired"] = requirements.SaveAsRequired,
            ["reloadRequired"] = requirements.ReloadRequired,
            ["validationRequired"] = requirements.ValidationRequired,
            ["semanticDiffRequired"] = requirements.SemanticDiffRequired,
            ["requirements"] = new JArray(requirements.Requirements)
        };

        private static JObject Impact(MutationImpact impact) => new JObject
        {
            ["risk"] = impact.Risk.ToString(),
            ["summary"] = impact.Summary,
            ["affectedNodeKeys"] = new JArray(impact.AffectedNodeKeys)
        };

        private static JObject Coverage(UnsupportedItem item) => new JObject
        {
            ["id"] = item.Id,
            ["name"] = item.Name,
            ["creationName"] = item.CreationName,
            ["reasonCode"] = item.ReasonCode
        };
    }
}