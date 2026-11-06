using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.SqlServer.Dts.Runtime;
using SsisAiRuntime.Mutations;
using Runtime = Microsoft.SqlServer.Dts.Runtime;

namespace SsisAiRuntime.MutationHost
{
    public sealed class VariableMetadataMutationPlan : IPackageMutationPlan
    {
        private readonly string packageId;
        private readonly string packageName;
        private readonly string variableId;
        private readonly string variableName;
        private readonly string variableNamespace;
        private readonly string originalDescription;
        private readonly string proposedDescription;
        private readonly string otherVariableMetadataHash;

        private VariableMetadataMutationPlan(string sourceArtifactHash, Package package, Variable variable,
            string proposedDescription, string otherVariableMetadataHash)
        {
            SourceArtifactHash = sourceArtifactHash;
            packageId = package.ID;
            packageName = package.Name;
            variableId = variable.ID;
            variableName = variable.Name;
            variableNamespace = variable.Namespace;
            originalDescription = variable.Description ?? string.Empty;
            this.proposedDescription = proposedDescription;
            this.otherVariableMetadataHash = otherVariableMetadataHash;
            Requirements = MutationExecutionPolicies.VariableMetadataUpdate();
        }

        public string SourceArtifactHash { get; }
        public string OperationName => "set_variable_metadata";
        public MutationExecutionRequirements Requirements { get; }
        public string VariableNativeId => variableId;
        public bool DescriptionOmitted => true;

        public static VariableMetadataMutationPlan Create(Package package, string sourceArtifactHash,
            string variableNativeId, string proposedDescription)
        {
            if (package == null) { throw new ArgumentNullException(nameof(package)); }
            if (!MutationHash.IsSha256(sourceArtifactHash))
            { throw new ArgumentException("A SHA-256 source hash is required.", nameof(sourceArtifactHash)); }
            if (string.IsNullOrWhiteSpace(variableNativeId))
            { throw new ArgumentException("A variable native ID is required.", nameof(variableNativeId)); }
            ValidateDescription(proposedDescription);
            var variable = FindPackageVariable(package, variableNativeId);
            if (variable.SystemVariable) { throw new InvalidOperationException("mutation.variable.metadata.system_variable_unsupported"); }
            if (string.Equals(variable.Description ?? string.Empty, proposedDescription, StringComparison.Ordinal))
            { throw new InvalidOperationException("mutation.variable.metadata.no_change"); }
            return new VariableMetadataMutationPlan(sourceArtifactHash, package, variable, proposedDescription,
                OtherVariableMetadataHash(package, variableNativeId));
        }

        public IPackageMutationPlan RebindCurrent(Package package, string sourceArtifactHash, string packagePath = null)
        {
            if (!string.Equals(SourceArtifactHash, sourceArtifactHash, StringComparison.Ordinal))
            { throw new InvalidOperationException("mutation.source.hash_changed"); }
            if (package == null || package.ID != packageId || package.Name != packageName)
            { throw new InvalidOperationException("mutation.variable.package_identity_changed"); }
            var current = Create(package, sourceArtifactHash, variableId, proposedDescription);
            if (current.variableName != variableName || current.variableNamespace != variableNamespace ||
                current.originalDescription != originalDescription ||
                current.otherVariableMetadataHash != otherVariableMetadataHash)
            { throw new InvalidOperationException("mutation.variable.metadata.changed"); }
            return current;
        }

        public void Apply(Package package)
        {
            if (package == null || package.ID != packageId || package.Name != packageName)
            { throw new InvalidOperationException("mutation.variable.package_identity_changed"); }
            var variable = FindPackageVariable(package, variableId);
            if (variable.SystemVariable || variable.Name != variableName || variable.Namespace != variableNamespace ||
                (variable.Description ?? string.Empty) != originalDescription ||
                OtherVariableMetadataHash(package, variableId) != otherVariableMetadataHash)
            { throw new InvalidOperationException("mutation.variable.metadata.changed"); }
            variable.Description = proposedDescription;
        }

        public string VerifyFailureCode(Package package)
        {
            if (package == null) { return "mutation.variable.package_missing"; }
            try
            {
                if (package.ID != packageId || package.Name != packageName)
                { return "mutation.variable.package_identity_changed"; }
                var variable = FindPackageVariable(package, variableId);
                if (variable.SystemVariable || variable.Name != variableName || variable.Namespace != variableNamespace ||
                    (variable.Description ?? string.Empty) != proposedDescription ||
                    OtherVariableMetadataHash(package, variableId) != otherVariableMetadataHash)
                { return "mutation.variable.metadata_mismatch"; }
                return string.Empty;
            }
            catch (InvalidOperationException exception)
            {
                return SafeFailureCode(exception.Message);
            }
            catch (Exception)
            {
                return "mutation.variable.metadata_verification_failed";
            }
        }

        public string SerializePreviewContract()
        {
            var contract = new VariableMetadataPreviewContract
            {
                SchemaVersion = "2.0",
                Operation = OperationName,
                SourceArtifactHash = SourceArtifactHash,
                VariableNativeId = variableId,
                Scope = "Package",
                RequestedFields = new[] { "Description" },
                DescriptionOmitted = true,
                Requirements = Requirements.Requirements.ToArray()
            };
            var serializer = new DataContractJsonSerializer(typeof(VariableMetadataPreviewContract));
            using (var stream = new System.IO.MemoryStream())
            {
                serializer.WriteObject(stream, contract);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static Variable FindPackageVariable(Package package, string variableNativeId)
        {
            var matches = package.Variables.Cast<Variable>()
                .Where(variable => string.Equals(variable.ID, variableNativeId, StringComparison.Ordinal)).ToArray();
            if (matches.Length != 1)
            { throw new InvalidOperationException("mutation.variable.target_not_found_or_ambiguous"); }
            return matches[0];
        }

        private static string OtherVariableMetadataHash(Package package, string exceptVariableId)
        {
            var builder = new StringBuilder();
            foreach (var variable in package.Variables.Cast<Variable>()
                .Where(item => !item.SystemVariable &&
                    !string.Equals(item.ID, exceptVariableId, StringComparison.Ordinal))
                .OrderBy(item => item.ID, StringComparer.Ordinal))
            {
                Append(builder, variable.ID);
                Append(builder, variable.Name);
                Append(builder, variable.Namespace);
                Append(builder, variable.DataType.ToString());
                Append(builder, variable.Description ?? string.Empty);
                Append(builder, variable.ReadOnly.ToString());
                Append(builder, variable.SystemVariable.ToString());
                Append(builder, variable.EvaluateAsExpression.ToString());
                Append(builder, string.IsNullOrWhiteSpace(variable.Expression) ? string.Empty : "expression-present");
            }
            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString())))
                    .Replace("-", string.Empty);
            }
        }

        private static void Append(StringBuilder builder, string value)
        {
            value = value ?? string.Empty;
            builder.Append(value.Length).Append(':').Append(value).Append(';');
        }

        private static void ValidateDescription(string description)
        {
            if (description == null) { throw new ArgumentNullException(nameof(description)); }
            if (description.Length > 1024 || description.Any(char.IsControl))
            { throw new ArgumentException("A bounded single-line description is required.", nameof(description)); }
        }

        private static string SafeFailureCode(string candidate) =>
            candidate.StartsWith("mutation.variable.", StringComparison.Ordinal) &&
            candidate.All(character => char.IsLetterOrDigit(character) || character == '.' || character == '_')
                ? candidate
                : "mutation.variable.metadata_verification_failed";

        [DataContract]
        private sealed class VariableMetadataPreviewContract
        {
            [DataMember(Order = 0)] public string SchemaVersion { get; set; } = string.Empty;
            [DataMember(Order = 1)] public string Operation { get; set; } = string.Empty;
            [DataMember(Order = 2)] public string SourceArtifactHash { get; set; } = string.Empty;
            [DataMember(Order = 3)] public string VariableNativeId { get; set; } = string.Empty;
            [DataMember(Order = 4)] public string Scope { get; set; } = string.Empty;
            [DataMember(Order = 5)] public string[] RequestedFields { get; set; } = Array.Empty<string>();
            [DataMember(Order = 6)] public bool DescriptionOmitted { get; set; }
            [DataMember(Order = 7)] public string[] Requirements { get; set; } = Array.Empty<string>();
        }
    }
}
