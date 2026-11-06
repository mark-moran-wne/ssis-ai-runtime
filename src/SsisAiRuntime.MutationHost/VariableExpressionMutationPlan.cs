using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.SqlServer.Dts.Runtime;
using SsisAiRuntime.Inspectors.Expressions;
using SsisAiRuntime.Mutations;
using SsisAiRuntime.Ssis16;

namespace SsisAiRuntime.MutationHost
{
    public sealed class VariableExpressionMutationPlan : IPackageMutationPlan
    {
        private readonly string packageId;
        private readonly string packageName;
        private readonly string variableId;
        private readonly string variableName;
        private readonly string variableNamespace;
        private readonly string originalExpression;
        private readonly bool originalEvaluatesAsExpression;
        private readonly string expression;
        private readonly string otherVariableMetadataHash;
        private readonly string[] expectedReferences;

        private VariableExpressionMutationPlan(string sourceArtifactHash, Package package, Variable variable,
            string expression, string otherVariableMetadataHash, string[] expectedReferences)
        {
            SourceArtifactHash = sourceArtifactHash;
            packageId = package.ID;
            packageName = package.Name;
            variableId = variable.ID;
            variableName = variable.Name;
            variableNamespace = variable.Namespace;
            originalExpression = variable.Expression ?? string.Empty;
            originalEvaluatesAsExpression = variable.EvaluateAsExpression;
            this.expression = expression;
            this.otherVariableMetadataHash = otherVariableMetadataHash;
            this.expectedReferences = expectedReferences;
            Requirements = MutationExecutionPolicies.VariableExpressionUpdate();
        }

        public string SourceArtifactHash { get; }
        public string OperationName => "set_variable_expression";
        public MutationExecutionRequirements Requirements { get; }
        public string VariableNativeId => variableId;
        public int ExpectedDependencyCount => expectedReferences.Length;
        public bool ExpressionOmitted => true;

        public static VariableExpressionMutationPlan Create(Package package, string sourceArtifactHash,
            string variableNativeId, string expression)
        {
            if (package == null) { throw new ArgumentNullException(nameof(package)); }
            if (!MutationHash.IsSha256(sourceArtifactHash))
            { throw new ArgumentException("A SHA-256 source hash is required.", nameof(sourceArtifactHash)); }
            if (string.IsNullOrWhiteSpace(variableNativeId))
            { throw new ArgumentException("A variable native ID is required.", nameof(variableNativeId)); }
            if (string.IsNullOrWhiteSpace(expression) || expression.Length > ExpressionReferenceParser.MaximumExpressionLength)
            { throw new InvalidOperationException("mutation.variable.expression.invalid_length"); }

            var variable = FindPackageVariable(package, variableNativeId);
            if (variable.SystemVariable) { throw new InvalidOperationException("mutation.variable.expression.system_variable_unsupported"); }
            if (!(variable.Parent is IDTSName parent) || parent.ID != package.ID)
            { throw new InvalidOperationException("mutation.variable.expression.package_scope_only"); }
            if (variable.EvaluateAsExpression && string.Equals(variable.Expression, expression, StringComparison.Ordinal))
            { throw new InvalidOperationException("mutation.variable.expression.no_change"); }

            var expectedReferences = ResolveReferences(package, variable, expression);
            return new VariableExpressionMutationPlan(sourceArtifactHash, package, variable, expression,
                OtherVariableMetadataHash(package, variableNativeId), expectedReferences);
        }

        public IPackageMutationPlan RebindCurrent(Package package, string sourceArtifactHash, string packagePath = null)
        {
            if (!string.Equals(SourceArtifactHash, sourceArtifactHash, StringComparison.Ordinal))
            { throw new InvalidOperationException("mutation.source.hash_changed"); }
            if (package == null || package.ID != packageId || package.Name != packageName)
            { throw new InvalidOperationException("mutation.variable.package_identity_changed"); }
            var current = Create(package, sourceArtifactHash, variableId, expression);
            if (current.variableName != variableName || current.variableNamespace != variableNamespace ||
                current.originalExpression != originalExpression ||
                current.originalEvaluatesAsExpression != originalEvaluatesAsExpression ||
                current.otherVariableMetadataHash != otherVariableMetadataHash ||
                !current.expectedReferences.SequenceEqual(expectedReferences, StringComparer.Ordinal))
            { throw new InvalidOperationException("mutation.variable.expression.changed"); }
            return current;
        }

        public void Apply(Package package)
        {
            if (package == null || package.ID != packageId || package.Name != packageName)
            { throw new InvalidOperationException("mutation.variable.package_identity_changed"); }
            var variable = FindPackageVariable(package, variableId);
            if (variable.SystemVariable || variable.Name != variableName || variable.Namespace != variableNamespace ||
                !(variable.Parent is IDTSName parent) || parent.ID != package.ID ||
                (variable.Expression ?? string.Empty) != originalExpression ||
                variable.EvaluateAsExpression != originalEvaluatesAsExpression ||
                OtherVariableMetadataHash(package, variableId) != otherVariableMetadataHash)
            { throw new InvalidOperationException("mutation.variable.expression.changed"); }

            variable.Expression = expression;
            variable.EvaluateAsExpression = true;
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
                    !(variable.Parent is IDTSName parent) || parent.ID != package.ID ||
                    (variable.Expression ?? string.Empty) != expression || !variable.EvaluateAsExpression ||
                    OtherVariableMetadataHash(package, variableId) != otherVariableMetadataHash)
                { return "mutation.variable.expression_mismatch"; }
                var actualReferences = ResolveReferences(package, variable, expression);
                if (!actualReferences.SequenceEqual(expectedReferences, StringComparer.Ordinal))
                { return "mutation.variable.expression_dependencies_mismatch"; }
                return string.Empty;
            }
            catch (InvalidOperationException exception)
            {
                return SafeFailureCode(exception.Message);
            }
            catch (Exception)
            {
                return "mutation.variable.expression_verification_failed";
            }
        }

        public string SerializePreviewContract()
        {
            var contract = new VariableExpressionPreviewContract
            {
                SchemaVersion = "2.0",
                Operation = OperationName,
                SourceArtifactHash = SourceArtifactHash,
                VariableNativeId = variableId,
                Scope = "Package",
                RequestedFields = new[] { "Expression", "EvaluateAsExpression" },
                ExpressionOmitted = true,
                ExpectedDependencyCount = expectedReferences.Length,
                Requirements = Requirements.Requirements.ToArray()
            };
            var serializer = new DataContractJsonSerializer(typeof(VariableExpressionPreviewContract));
            using (var stream = new System.IO.MemoryStream())
            {
                serializer.WriteObject(stream, contract);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static string[] ResolveReferences(Package package, Variable variable, string expression)
        {
            var catalog = new NativeExpressionScopeCatalogBuilder().Build(package);
            var analysis = new ExpressionDependencyAnalyzer(new ExpressionReferenceParser(), new ExpressionScopeResolver())
                .Analyze(new ExpressionDependencyInput(variable.ID, package.ID, "VariableExpression", expression), catalog);
            if (!analysis.Parsed) { throw new InvalidOperationException("mutation.variable.expression.parse_failed"); }
            if (!analysis.IsComplete)
            { throw new InvalidOperationException("mutation.variable.expression.references_unresolved"); }
            return analysis.Resolutions.Select(resolution =>
            {
                var symbol = resolution.ResolvedSymbol;
                return symbol.Kind + "\u001f" + symbol.NativeId + "\u001f" + symbol.QualifiedName;
            }).OrderBy(value => value, StringComparer.Ordinal).ToArray();
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
                : "mutation.variable.expression_verification_failed";

        [DataContract]
        private sealed class VariableExpressionPreviewContract
        {
            [DataMember(Order = 0)] public string SchemaVersion { get; set; } = string.Empty;
            [DataMember(Order = 1)] public string Operation { get; set; } = string.Empty;
            [DataMember(Order = 2)] public string SourceArtifactHash { get; set; } = string.Empty;
            [DataMember(Order = 3)] public string VariableNativeId { get; set; } = string.Empty;
            [DataMember(Order = 4)] public string Scope { get; set; } = string.Empty;
            [DataMember(Order = 5)] public string[] RequestedFields { get; set; } = Array.Empty<string>();
            [DataMember(Order = 6)] public bool ExpressionOmitted { get; set; }
            [DataMember(Order = 7)] public int ExpectedDependencyCount { get; set; }
            [DataMember(Order = 8)] public string[] Requirements { get; set; } = Array.Empty<string>();
        }
    }
}
