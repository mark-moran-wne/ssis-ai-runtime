using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.SqlServer.Dts.Runtime;
using SsisAiRuntime.Mutations;

namespace SsisAiRuntime.MutationHost
{
    public sealed class VariableValueMutationPlan : IPackageMutationPlan
    {
        private readonly string packageId;
        private readonly string packageName;
        private readonly string variableId;
        private readonly string variableName;
        private readonly string variableNamespace;
        private readonly TypeCode variableType;
        private readonly string description;
        private readonly bool originalReadOnly;
        private readonly bool originalEvaluatesAsExpression;
        private readonly string otherVariableMetadataHash;
        private readonly object resultingValue;

        private VariableValueMutationPlan(string sourceArtifactHash, Package package, Variable variable, object resultingValue,
            string otherVariableMetadataHash)
        {
            SourceArtifactHash = sourceArtifactHash;
            packageId = package.ID;
            packageName = package.Name;
            variableId = variable.ID;
            variableName = variable.Name;
            variableNamespace = variable.Namespace;
            variableType = variable.DataType;
            description = variable.Description ?? string.Empty;
            originalReadOnly = variable.ReadOnly;
            originalEvaluatesAsExpression = variable.EvaluateAsExpression;
            this.resultingValue = resultingValue;
            this.otherVariableMetadataHash = otherVariableMetadataHash;
            Requirements = MutationExecutionPolicies.VariableValueUpdate();
        }

        public string SourceArtifactHash { get; }
        public string OperationName => "set_variable_value";
        public MutationExecutionRequirements Requirements { get; }
        public string VariableNativeId => variableId;
        public string VariableDataType => variableType.ToString();
        public bool ValueOmitted => true;

        public static VariableValueMutationPlan Create(Package package, string sourceArtifactHash,
            string variableNativeId, string trustedInputValue)
        {
            if (package == null) { throw new ArgumentNullException(nameof(package)); }
            if (!MutationHash.IsSha256(sourceArtifactHash))
            { throw new ArgumentException("A SHA-256 source hash is required.", nameof(sourceArtifactHash)); }
            if (string.IsNullOrWhiteSpace(variableNativeId))
            { throw new ArgumentException("A variable native ID is required.", nameof(variableNativeId)); }
            if (trustedInputValue == null) { throw new InvalidOperationException("mutation.variable.value.trusted_input_missing"); }

            var variable = FindPackageVariable(package, variableNativeId);
            ValidateTarget(package, variable);
            var value = ParseValue(variable.DataType, trustedInputValue);
            if (Equals(variable.Value, value)) { throw new InvalidOperationException("mutation.variable.value.no_change"); }
            return new VariableValueMutationPlan(sourceArtifactHash, package, variable, value,
                OtherVariableMetadataHash(package, variableNativeId));
        }

        public IPackageMutationPlan RebindCurrent(Package package, string sourceArtifactHash, string packagePath = null)
        {
            if (!string.Equals(SourceArtifactHash, sourceArtifactHash, StringComparison.Ordinal))
            { throw new InvalidOperationException("mutation.source.hash_changed"); }
            if (package == null || package.ID != packageId || package.Name != packageName)
            { throw new InvalidOperationException("mutation.variable.package_identity_changed"); }
            var current = Create(package, sourceArtifactHash, variableId, FormatValue(resultingValue));
            if (current.variableName != variableName || current.variableNamespace != variableNamespace ||
                current.variableType != variableType || current.description != description ||
                current.originalReadOnly != originalReadOnly ||
                current.originalEvaluatesAsExpression != originalEvaluatesAsExpression ||
                current.otherVariableMetadataHash != otherVariableMetadataHash)
            { throw new InvalidOperationException("mutation.variable.value.metadata_changed"); }
            return current;
        }

        public void Apply(Package package)
        {
            if (package == null || package.ID != packageId || package.Name != packageName)
            { throw new InvalidOperationException("mutation.variable.package_identity_changed"); }
            var variable = FindPackageVariable(package, variableId);
            ValidateTarget(package, variable);
            if (variable.Name != variableName || variable.Namespace != variableNamespace || variable.DataType != variableType ||
                (variable.Description ?? string.Empty) != description || variable.ReadOnly != originalReadOnly ||
                variable.EvaluateAsExpression != originalEvaluatesAsExpression ||
                OtherVariableMetadataHash(package, variableId) != otherVariableMetadataHash ||
                Equals(variable.Value, resultingValue))
            { throw new InvalidOperationException("mutation.variable.value.metadata_changed"); }
            variable.Value = resultingValue;
        }

        public string VerifyFailureCode(Package package)
        {
            if (package == null) { return "mutation.variable.package_missing"; }
            try
            {
                if (package.ID != packageId || package.Name != packageName)
                { return "mutation.variable.package_identity_changed"; }
                var variable = FindPackageVariable(package, variableId);
                if (variable.Name != variableName || variable.Namespace != variableNamespace || variable.DataType != variableType ||
                    (variable.Description ?? string.Empty) != description || variable.ReadOnly != originalReadOnly ||
                    variable.EvaluateAsExpression != originalEvaluatesAsExpression ||
                    OtherVariableMetadataHash(package, variableId) != otherVariableMetadataHash ||
                    !Equals(variable.Value, resultingValue))
                { return "mutation.variable.value_mismatch"; }
                return string.Empty;
            }
            catch (InvalidOperationException exception)
            {
                return SafeFailureCode(exception.Message);
            }
            catch (Exception)
            {
                return "mutation.variable.value_verification_failed";
            }
        }

        public string SerializePreviewContract()
        {
            var contract = new VariableValuePreviewContract
            {
                SchemaVersion = "2.0",
                Operation = OperationName,
                SourceArtifactHash = SourceArtifactHash,
                VariableNativeId = variableId,
                Scope = "Package",
                DataType = variableType.ToString(),
                InputChannel = "ProcessEnvironment",
                ValueOmitted = true,
                Requirements = Requirements.Requirements.ToArray()
            };
            var serializer = new DataContractJsonSerializer(typeof(VariableValuePreviewContract));
            using (var stream = new System.IO.MemoryStream())
            {
                serializer.WriteObject(stream, contract);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        public static string ValidateEnvironmentVariableName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 128 ||
                !(char.IsLetter(name[0]) || name[0] == '_') ||
                name.Skip(1).Any(character => !char.IsLetterOrDigit(character) && character != '_'))
            { throw new InvalidOperationException("mutation.variable.value.environment_name_invalid"); }
            return name;
        }

        private static void ValidateTarget(Package package, Variable variable)
        {
            if (package == null) { throw new ArgumentNullException(nameof(package)); }
            if (variable.SystemVariable) { throw new InvalidOperationException("mutation.variable.value.system_variable_unsupported"); }
            if (!(variable.Parent is IDTSName parent) || parent.ID != package.ID)
            { throw new InvalidOperationException("mutation.variable.value.package_scope_only"); }
            if (variable.ReadOnly) { throw new InvalidOperationException("mutation.variable.value.read_only"); }
            if (variable.EvaluateAsExpression) { throw new InvalidOperationException("mutation.variable.value.expression_owned"); }
        }

        private static object ParseValue(TypeCode type, string value)
        {
            try
            {
                switch (type)
                {
                    case TypeCode.String: return value;
                    case TypeCode.Boolean: return bool.Parse(value);
                    case TypeCode.Byte: return byte.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
                    case TypeCode.SByte: return sbyte.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
                    case TypeCode.Int16: return short.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
                    case TypeCode.UInt16: return ushort.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
                    case TypeCode.Int32: return int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
                    case TypeCode.UInt32: return uint.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
                    case TypeCode.Int64: return long.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
                    case TypeCode.UInt64: return ulong.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
                    case TypeCode.Single: return float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
                    case TypeCode.Double: return double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
                    case TypeCode.Decimal: return decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);
                    case TypeCode.DateTime: return DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                    default: throw new InvalidOperationException("mutation.variable.value.type_unsupported");
                }
            }
            catch (InvalidOperationException) { throw; }
            catch (Exception) { throw new InvalidOperationException("mutation.variable.value.type_invalid"); }
        }

        private static string FormatValue(object value) => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;

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
                .Where(item => !item.SystemVariable && !string.Equals(item.ID, exceptVariableId, StringComparison.Ordinal))
                .OrderBy(item => item.ID, StringComparer.Ordinal))
            {
                Append(builder, variable.ID);
                Append(builder, variable.Name);
                Append(builder, variable.Namespace);
                Append(builder, variable.DataType.ToString());
                Append(builder, variable.Description ?? string.Empty);
                Append(builder, variable.ReadOnly.ToString());
                Append(builder, variable.EvaluateAsExpression.ToString());
                Append(builder, variable.Expression ?? string.Empty);
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

        private static string SafeFailureCode(string candidate) =>
            candidate.StartsWith("mutation.variable.", StringComparison.Ordinal) &&
            candidate.All(character => char.IsLetterOrDigit(character) || character == '.' || character == '_')
                ? candidate
                : "mutation.variable.value_verification_failed";

        [DataContract]
        private sealed class VariableValuePreviewContract
        {
            [DataMember(Order = 0)] public string SchemaVersion { get; set; } = string.Empty;
            [DataMember(Order = 1)] public string Operation { get; set; } = string.Empty;
            [DataMember(Order = 2)] public string SourceArtifactHash { get; set; } = string.Empty;
            [DataMember(Order = 3)] public string VariableNativeId { get; set; } = string.Empty;
            [DataMember(Order = 4)] public string Scope { get; set; } = string.Empty;
            [DataMember(Order = 5)] public string DataType { get; set; } = string.Empty;
            [DataMember(Order = 6)] public string InputChannel { get; set; } = string.Empty;
            [DataMember(Order = 7)] public bool ValueOmitted { get; set; }
            [DataMember(Order = 8)] public string[] Requirements { get; set; } = Array.Empty<string>();
        }
    }
}
