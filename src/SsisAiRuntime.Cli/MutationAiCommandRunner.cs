#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.SqlServer.Dts.Pipeline.Wrapper;
using SsisAiRuntime.AI;
using SsisAiRuntime.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using SsisAiRuntime.Corpus;
using SsisAiRuntime.Inspectors;
using SsisAiRuntime.MutationHost;
using SsisAiRuntime.Mutations;
using SsisAiRuntime.Ssis16;
using RuntimeWrapper = Microsoft.SqlServer.Dts.Runtime.Wrapper;
using Host = SsisAiRuntime.MutationHost.MutationHost;

namespace SsisAiRuntime.Cli
{
    internal sealed class MutationAiCommandRunner
    {
        private static readonly string[] PlacementRelations = { "Below", "OpenSpace", "RightOf" };
        private static readonly JsonSerializerSettings RequestSerializerSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            MissingMemberHandling = MissingMemberHandling.Error,
            NullValueHandling = NullValueHandling.Include
        };

        private sealed class MutationCommandTarget
        {
            [JsonProperty(Required = Required.Always)] public string Kind { get; set; } = string.Empty;
            [JsonProperty(Required = Required.Always)] public string NativeId { get; set; } = string.Empty;
            [JsonProperty] public string? OwnerNativeId { get; set; }
        }

        private sealed class TaskPropertyRequest
        {
            [JsonProperty(Required = Required.Always)] public string PropertyName { get; set; } = string.Empty;
            [JsonProperty(Required = Required.Always)] public JToken? Value { get; set; }
        }

        private sealed class RenameTaskRequest
        {
            [JsonProperty(Required = Required.Always)] public string ProposedName { get; set; } = string.Empty;
        }

        private sealed class PreparedMutationPlan
        {
            public PreparedMutationPlan(string operation, string sourceArtifactHash,
                MutationExecutionRequirements requirements, MutationAdapterIdentity adapter,
                JObject preview, IEnumerable<string> coverage, IPackageMutationPlan? packagePlan,
                MutationExecutionPlan? renameTaskPlan, bool layoutVerificationRequired)
            {
                Operation = operation;
                SourceArtifactHash = sourceArtifactHash;
                Requirements = requirements;
                Adapter = adapter;
                Preview = preview;
                Coverage = coverage.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
                PackagePlan = packagePlan;
                RenameTaskPlan = renameTaskPlan;
                LayoutVerificationRequired = layoutVerificationRequired;
            }

            public string Operation { get; }
            public string SourceArtifactHash { get; }
            public MutationExecutionRequirements Requirements { get; }
            public MutationAdapterIdentity Adapter { get; }
            public JObject Preview { get; }
            public IReadOnlyList<string> Coverage { get; }
            public IPackageMutationPlan? PackagePlan { get; }
            public MutationExecutionPlan? RenameTaskPlan { get; }
            public bool LayoutVerificationRequired { get; }
        }

        private sealed class TaskCollectionRequest
        {
            [JsonProperty(Required = Required.Always)] public string PropertyName { get; set; } = string.Empty;
            [JsonProperty(Required = Required.Always)] public string Mode { get; set; } = string.Empty;
            [JsonProperty(Required = Required.Always)] public string[]? Values { get; set; }
        }

        private sealed class TransferTablesRequest
        {
            [JsonProperty(Required = Required.Always)] public string Mode { get; set; } = string.Empty;
            [JsonProperty(Required = Required.Always)] public string[]? TableNames { get; set; }
        }

        private sealed class DerivedColumnRequest
        {
            [JsonProperty(Required = Required.Always)] public int SourceLineageId { get; set; }
            [JsonProperty(Required = Required.Always)] public string ComponentName { get; set; } = string.Empty;
            [JsonProperty(Required = Required.Always)] public string OutputName { get; set; } = string.Empty;
            [JsonProperty(Required = Required.Always)] public string Expression { get; set; } = string.Empty;
            [JsonProperty(Required = Required.Always)] public MutationPlacementRequest? Placement { get; set; }
            [JsonProperty] public string? SourceComponentId { get; set; }
            [JsonProperty] public string? DestinationComponentId { get; set; }
        }

        private sealed class DataConversionRequest
        {
            [JsonProperty(Required = Required.Always)] public int SourceLineageId { get; set; }
            [JsonProperty(Required = Required.Always)] public string ComponentName { get; set; } = string.Empty;
            [JsonProperty(Required = Required.Always)] public string OutputName { get; set; } = string.Empty;
            [JsonProperty(Required = Required.Always)] public string OutputDataType { get; set; } = string.Empty;
            [JsonProperty(Required = Required.Always)] public MutationPlacementRequest? Placement { get; set; }
            [JsonProperty] public int? Length { get; set; }
            [JsonProperty] public int? Precision { get; set; }
            [JsonProperty] public int? Scale { get; set; }
            [JsonProperty] public int? CodePage { get; set; }
            [JsonProperty] public string? SourceComponentId { get; set; }
            [JsonProperty] public string? DestinationComponentId { get; set; }
        }

        private sealed class MutationPlacementRequest
        {
            [JsonProperty(Required = Required.Always)] public string Relation { get; set; } = string.Empty;
            [JsonProperty(Required = Required.Always)] public int Width { get; set; }
            [JsonProperty(Required = Required.Always)] public int Height { get; set; }
            [JsonProperty] public string? AnchorComponentId { get; set; }
        }

        public int Run(string[] args, TextReader input, TextWriter output)
        {
            var tool = args != null && args.Length > 1 ? args[1] : null;
            if (tool == "task.properties")
            {
                if (args!.Length != 5 || args[3] != "--task" || string.IsNullOrWhiteSpace(args[2]) ||
                    string.IsNullOrWhiteSpace(args[4]))
                { return WriteError(output, tool, 2, "ai.task_properties.usage"); }
                return RunTaskPropertyInspection(args[2], args[4], output);
            }
            if (tool == "mutation.capabilities")
            {
                if (args!.Length != 2) { return WriteError(output, tool, 2, "ai.mutation.usage"); }
                return WriteCapabilities(output);
            }
            var isApply = tool == "mutation.apply";
            if ((tool != "mutation.preview" && !isApply) || args!.Length != (isApply ? 5 : 3) ||
                string.IsNullOrWhiteSpace(args[2]) ||
                (isApply && (string.IsNullOrWhiteSpace(args[3]) || string.IsNullOrWhiteSpace(args[4]))))
            {
                return WriteError(output, tool, 2, "ai.mutation.usage");
            }
            if (!File.Exists(args[2])) { return WriteError(output, tool, 3, "package.path.not_found"); }

            JObject request;
            try { request = JObject.Parse(input.ReadToEnd()); }
            catch { return WriteError(output, tool, 2, "ai.mutation.request.invalid"); }
            if ((string?)request["schemaVersion"] != MutationContractSchema.CurrentVersion ||
                request.Properties().Any(property => property.Name != "schemaVersion" && property.Name != "operation" &&
                    property.Name != "target" && property.Name != "request" && property.Name != "sourceArtifactHash"))
            { return WriteError(output, tool, 2, "ai.mutation.request.schema_unsupported"); }

            var operation = (string?)request["operation"];
            if (operation != "rename_task" && operation != "insert_derived_column" && operation != "insert_data_conversion" &&
                operation != "update_transfer_tables" && operation != "set_task_property" &&
                operation != "update_task_collection")
            { return WriteError(output, tool, 2, "ai.mutation.operation.unsupported"); }

            try
            {
                using (var sourceGuard = new FileStream(args[2], FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var sourceHash = Hash(sourceGuard);
                    if (!string.Equals(sourceHash, RequiredString(request, "sourceArtifactHash"), StringComparison.OrdinalIgnoreCase))
                    { return WriteError(output, tool, 3, "mutation.source_hash_mismatch"); }
                    var load = new PackageLoader().Load(args[2]);
                    if (!load.Succeeded || load.Session == null)
                    { return WriteError(output, tool, 3, "ssis.package.load_failed"); }

                    PreparedMutationPlan plan;
                    try
                    {
                        plan = CreatePreparedPlan(load.Session, args[2], sourceHash, request, operation!);
                    }
                    finally { load.Session.Package.Dispose(); }

                    if (!isApply)
                    {
                        return WriteSuccess(output, tool!, operation!, plan, request);
                    }

                    var host = CreateHost(args[4]);
                    var hostRequest = plan.RenameTaskPlan != null
                        ? new MutationHostRequest(args[2], args[3], plan.RenameTaskPlan, sourceHash)
                        : new MutationHostRequest(args[2], args[3], plan.PackagePlan!, sourceHash);
                    var result = host.ExecuteAsync(hostRequest,
                        CancellationToken.None).GetAwaiter().GetResult();
                    return WriteApplyResult(output, operation!, plan, request, result);
                }
            }
            catch (Exception exception)
            {
                var code = SafeCode(exception.Message) ?? "ai.mutation.preview.failed";
                return WriteError(output, tool, 4, code);
            }
        }

        private static PreparedMutationPlan CreatePreparedPlan(PackageSession<Microsoft.SqlServer.Dts.Runtime.Package> session,
            string packagePath, string sourceHash, JObject envelope, string operation)
        {
            if (operation != "rename_task")
            {
                var packagePlan = CreatePlan(session.Package, packagePath, sourceHash, envelope, operation);
                return new PreparedMutationPlan(operation, sourceHash, packagePlan.Requirements,
                    AdapterIdentity(packagePlan), SafePreview(packagePlan), Array.Empty<string>(), packagePlan, null,
                    packagePlan is IComponentInsertionPlan);
            }

            var target = DeserializeRequest<MutationCommandTarget>(envelope["target"]);
            if (!string.Equals(target.Kind, "Executable", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(target.NativeId) || !string.IsNullOrWhiteSpace(target.OwnerNativeId))
            { throw new ArgumentException("target"); }
            var payload = DeserializeRequest<RenameTaskRequest>(envelope["request"]);
            var analysis = new PackageAnalysisSnapshotFactory().Create(session);
            if (analysis.Items.Count != 1) { throw new InvalidOperationException("mutation.source.analysis_failed"); }
            var mutationRequest = new MutationRequest(MutationKind.RenameTask,
                new MutationTarget(SemanticObjectKind.Executable, target.NativeId),
                RequiredValue(payload.ProposedName, "proposedName"));
            var preview = new MutationPreviewer().Preview(analysis.Items[0], mutationRequest);
            if (!preview.IsValid || preview.ExecutionPlan == null)
            { throw new InvalidOperationException("mutation.rename.preview.invalid"); }

            return new PreparedMutationPlan(operation, sourceHash, preview.ExecutionPlan.Requirements,
                new MutationAdapterIdentity("ssis16.task-host-rename", "1.0"), SafeRenamePreview(preview),
                preview.CoverageGaps.Select(item => item.ReasonCode), null, preview.ExecutionPlan, false);
        }

        private static JObject SafeRenamePreview(MutationPreview preview) => new JObject
        {
            ["status"] = preview.Status.ToString(),
            ["impact"] = new JObject
            {
                ["risk"] = preview.Impact!.Risk.ToString(),
                ["summary"] = preview.Impact.Summary,
                ["affectedNodeCount"] = preview.Impact.AffectedNodeKeys.Count
            }
        };

        private static IPackageMutationPlan CreatePlan(Microsoft.SqlServer.Dts.Runtime.Package package,
            string packagePath, string sourceHash, JObject envelope, string operation)
        {
            var target = DeserializeRequest<MutationCommandTarget>(envelope["target"]);
            if (operation == "set_task_property")
            {
                var taskId = RequireTaskTarget(target);
                var payload = DeserializeRequest<TaskPropertyRequest>(envelope["request"]);
                var propertyName = RequiredValue(payload.PropertyName, "propertyName");
                var type = TaskHostPropertyMutationPlan.GetPropertyTypeCode(package, taskId, propertyName);
                var valueToken = payload.Value ?? throw new ArgumentException("value");
                var enumValues = TaskHostPropertyMutationPlan.GetPropertyEnumValues(package, taskId, propertyName);
                var propertyValue = enumValues.Length > 0
                    ? valueToken.Type == JTokenType.String
                        ? TaskHostPropertyMutationPlan.ParsePropertyEnumValue(package, taskId, propertyName, valueToken.ToString())
                        : throw new ArgumentException("value.enum_name_required")
                    : ParsePropertyValue(valueToken, type);
                return TaskHostPropertyMutationPlan.CreateScalar(package, sourceHash, taskId, propertyName, propertyValue);
            }
            if (operation == "update_task_collection")
            {
                var taskId = RequireTaskTarget(target);
                var payload = DeserializeRequest<TaskCollectionRequest>(envelope["request"]);
                var propertyName = RequiredValue(payload.PropertyName, "propertyName");
                if (!Enum.TryParse(RequiredValue(payload.Mode, "mode"), true, out TaskStringCollectionEditMode mode) ||
                    !Enum.IsDefined(typeof(TaskStringCollectionEditMode), mode))
                { throw new ArgumentException("mode"); }
                var values = RequiredValues(payload.Values, "values");
                if (string.Equals(propertyName, "TablesList", StringComparison.OrdinalIgnoreCase) &&
                    TaskHostPropertyMutationPlan.IsTransferTask(package, taskId))
                {
                    var transferMode = (TransferTableUpdateMode)Enum.Parse(typeof(TransferTableUpdateMode), mode.ToString(), true);
                    return TransferTablesMutationPlan.Create(package, sourceHash, taskId, transferMode, values);
                }
                return TaskHostPropertyMutationPlan.CreateStringCollection(package, sourceHash, taskId,
                    propertyName, mode, values);
            }
            if (operation == "update_transfer_tables")
            {
                var taskId = RequireTaskTarget(target);
                var payload = DeserializeRequest<TransferTablesRequest>(envelope["request"]);
                if (!Enum.TryParse(RequiredValue(payload.Mode, "mode"), true, out TransferTableUpdateMode mode) ||
                    !Enum.IsDefined(typeof(TransferTableUpdateMode), mode))
                { throw new ArgumentException("mode"); }
                return TransferTablesMutationPlan.Create(package, sourceHash, taskId, mode,
                    RequiredValues(payload.TableNames, "tableNames"));
            }

            var pathTarget = RequirePathTarget(target);
            if (operation == "insert_derived_column")
            {
                var payload = DeserializeRequest<DerivedColumnRequest>(envelope["request"]);
                var placementIntent = CreatePlacement(pathTarget.OwnerNativeId, payload.Placement);
                var plan = DerivedColumnInsertionPlan.CreateForPath(package, sourceHash,
                    pathTarget.OwnerNativeId!, pathTarget.NativeId!, RequiredPositive(payload.SourceLineageId, "sourceLineageId"),
                    RequiredValue(payload.ComponentName, "componentName"), RequiredValue(payload.OutputName, "outputName"),
                    RequiredValue(payload.Expression, "expression"), placementIntent, packagePath);
                ValidateOptionalInsertionEndpoints(payload.SourceComponentId, payload.DestinationComponentId,
                    plan.SourceComponentNativeId, plan.DestinationComponentNativeId);
                return plan;
            }

            var conversion = DeserializeRequest<DataConversionRequest>(envelope["request"]);
            if (!Enum.TryParse(RequiredValue(conversion.OutputDataType, "outputDataType"), true,
                out RuntimeWrapper.DataType outputDataType))
            { throw new ArgumentException("outputDataType"); }
            var conversionPlacement = CreatePlacement(pathTarget.OwnerNativeId, conversion.Placement);
            var conversionPlan = DataConversionInsertionPlan.CreateForPath(package, sourceHash,
                pathTarget.OwnerNativeId!, pathTarget.NativeId!, RequiredPositive(conversion.SourceLineageId, "sourceLineageId"),
                RequiredValue(conversion.ComponentName, "componentName"), RequiredValue(conversion.OutputName, "outputName"),
                outputDataType, conversion.Length ?? 0, conversion.Precision ?? 0, conversion.Scale ?? 0,
                conversion.CodePage ?? 0,
                conversionPlacement, packagePath);
            ValidateOptionalInsertionEndpoints(conversion.SourceComponentId, conversion.DestinationComponentId,
                conversionPlan.SourceComponentNativeId, conversionPlan.DestinationComponentNativeId);
            return conversionPlan;
        }

        private static void ValidateOptionalInsertionEndpoints(string? sourceComponentId, string? destinationComponentId,
            string expectedSourceComponentId, string expectedDestinationComponentId)
        {
            if ((!string.IsNullOrWhiteSpace(sourceComponentId) && sourceComponentId != expectedSourceComponentId) ||
                (!string.IsNullOrWhiteSpace(destinationComponentId) && destinationComponentId != expectedDestinationComponentId))
            { throw new ArgumentException("path.endpoint_mismatch"); }
        }

        private static int RunTaskPropertyInspection(string packagePath, string taskId, TextWriter output)
        {
            if (!File.Exists(packagePath)) { return WriteError(output, "task.properties", 3, "package.path.not_found"); }
            var load = new PackageLoader().Load(packagePath);
            if (!load.Succeeded || load.Session == null)
            { return WriteError(output, "task.properties", 3, "ssis.package.load_failed"); }
            try
            {
                var inventory = TaskHostPropertyMutationPlan.Inspect(load.Session.Package, taskId);
                var properties = new JArray(inventory.Properties.Select(property =>
                {
                    var item = new JObject
                    {
                        ["name"] = property.Name,
                        ["type"] = property.Type,
                        ["typeConverter"] = property.TypeConverter,
                        ["canRead"] = property.CanRead,
                        ["canWrite"] = property.CanWrite,
                        ["valueKind"] = property.ValueKind,
                        ["valueRedacted"] = property.ValueRedacted,
                        ["mutationAction"] = MutationAction(property)
                    };
                    if (property.CollectionCount.HasValue) { item["collectionCount"] = property.CollectionCount.Value; }
                    if (property.EnumValues.Count > 0) { item["enumValues"] = new JArray(property.EnumValues); }
                    if (property.ValueKind == "StringCollection" && !property.ValueRedacted)
                    { item["currentItems"] = new JArray(property.CollectionItems); }
                    if (!string.IsNullOrEmpty(property.SafeValue))
                    { item["currentValue"] = JToken.FromObject(SafeCurrentValue(property.SafeValue, property.Type, property.ValueKind)); }
                    if (!string.IsNullOrEmpty(property.SpecializedOperation))
                    { item["specializedOperation"] = property.SpecializedOperation; }
                    return item;
                }));
                var envelope = new JObject
                {
                    ["schemaVersion"] = "1.0",
                    ["tool"] = "task.properties",
                    ["succeeded"] = true,
                    ["exitCode"] = 0,
                    ["results"] = new JObject
                    {
                        ["task"] = new JObject
                        {
                            ["nativeId"] = inventory.TaskId,
                            ["name"] = inventory.TaskName,
                            ["creationName"] = inventory.CreationName
                        },
                        ["propertyCount"] = properties.Count,
                        ["properties"] = properties
                    },
                    ["diagnostics"] = new JArray(),
                    ["packageExecuted"] = false,
                    ["nativeValidationPerformed"] = false,
                    ["redaction"] = new JObject
                    {
                        ["policy"] = "task-property-descriptor",
                        ["stringValuesOmitted"] = true,
                        ["sensitiveCollectionItemsOmitted"] = true,
                        ["packagePathOmitted"] = true
                    }
                };
                output.WriteLine(envelope.ToString(Formatting.None));
                return 0;
            }
            catch (Exception exception)
            {
                var code = SafeCode(exception.Message) ?? "ai.task_properties.failed";
                return WriteError(output, "task.properties", 4, code);
            }
            finally { load.Session.Package.Dispose(); }
        }

        private static string MutationAction(TaskHostPropertyDescriptor property)
        {
            if (!string.IsNullOrEmpty(property.SpecializedOperation)) { return property.SpecializedOperation; }
            if (!property.CanWrite) { return "read_only"; }
            if (property.ValueKind == "StringCollection") { return "update_task_collection"; }
            return Enum.TryParse(property.Type, out TypeCode type) && IsSupportedPropertyType(type)
                ? "set_task_property" : "unsupported_value_type";
        }

        private static object SafeCurrentValue(string value, string typeName, string valueKind)
        {
            if (valueKind == "Enum") { return value; }
            if (!Enum.TryParse(typeName, out TypeCode type)) { return value; }
            switch (type)
            {
                case TypeCode.Boolean: return bool.Parse(value);
                case TypeCode.Byte: return byte.Parse(value, CultureInfo.InvariantCulture);
                case TypeCode.SByte: return sbyte.Parse(value, CultureInfo.InvariantCulture);
                case TypeCode.Int16: return short.Parse(value, CultureInfo.InvariantCulture);
                case TypeCode.UInt16: return ushort.Parse(value, CultureInfo.InvariantCulture);
                case TypeCode.Int32: return int.Parse(value, CultureInfo.InvariantCulture);
                case TypeCode.UInt32: return uint.Parse(value, CultureInfo.InvariantCulture);
                case TypeCode.Int64: return long.Parse(value, CultureInfo.InvariantCulture);
                case TypeCode.UInt64: return ulong.Parse(value, CultureInfo.InvariantCulture);
                case TypeCode.Single: return float.Parse(value, CultureInfo.InvariantCulture);
                case TypeCode.Double: return double.Parse(value, CultureInfo.InvariantCulture);
                case TypeCode.Decimal: return decimal.Parse(value, CultureInfo.InvariantCulture);
                default: return value;
            }
        }

        private static object ParsePropertyValue(JToken token, TypeCode type)
        {
            if (token == null) { throw new ArgumentException("value"); }
            switch (type)
            {
                case TypeCode.String:
                    if (token.Type == JTokenType.String) { return (string)token!; }
                    break;
                case TypeCode.Boolean:
                    if (token.Type == JTokenType.Boolean) { return (bool)token; }
                    break;
                case TypeCode.Byte:
                    if (token.Type == JTokenType.Integer) { return checked((byte)(long)token); }
                    break;
                case TypeCode.SByte:
                    if (token.Type == JTokenType.Integer) { return checked((sbyte)(long)token); }
                    break;
                case TypeCode.Int16:
                    if (token.Type == JTokenType.Integer) { return checked((short)(long)token); }
                    break;
                case TypeCode.UInt16:
                    if (token.Type == JTokenType.Integer) { return checked((ushort)(long)token); }
                    break;
                case TypeCode.Int32:
                    if (token.Type == JTokenType.Integer) { return checked((int)(long)token); }
                    break;
                case TypeCode.UInt32:
                    if (token.Type == JTokenType.Integer) { return checked((uint)(long)token); }
                    break;
                case TypeCode.Int64:
                    if (token.Type == JTokenType.Integer) { return (long)token; }
                    break;
                case TypeCode.UInt64:
                    if (token.Type == JTokenType.Integer) { return token.ToObject<ulong>(); }
                    break;
                case TypeCode.Single:
                    if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float) { return (float)(double)token; }
                    break;
                case TypeCode.Double:
                    if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float) { return (double)token; }
                    break;
                case TypeCode.Decimal:
                    if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float) { return (decimal)token; }
                    break;
                case TypeCode.DateTime:
                    if (token.Type == JTokenType.String && DateTime.TryParse(token.ToString(), CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.RoundtripKind, out var dateTime)) { return dateTime; }
                    break;
            }
            throw new ArgumentException("value.type_mismatch");
        }

        private static bool IsSupportedPropertyType(TypeCode type) =>
            type == TypeCode.String || type == TypeCode.Boolean || type == TypeCode.Byte || type == TypeCode.SByte ||
            type == TypeCode.Int16 || type == TypeCode.UInt16 || type == TypeCode.Int32 || type == TypeCode.UInt32 ||
            type == TypeCode.Int64 || type == TypeCode.UInt64 || type == TypeCode.Single || type == TypeCode.Double ||
            type == TypeCode.Decimal || type == TypeCode.DateTime;

        private static int WriteCapabilities(TextWriter output)
        {
            var envelope = new JObject
            {
                ["schemaVersion"] = MutationContractSchema.CurrentVersion,
                ["tool"] = "mutation.capabilities",
                ["succeeded"] = true,
                ["exitCode"] = 0,
                ["results"] = new JObject
                {
                    ["schemaVersion"] = MutationContractSchema.CurrentVersion,
                    ["requestEnvelopeSchema"] = new JObject
                    {
                        ["type"] = "object",
                        ["required"] = new JArray("schemaVersion", "operation", "target", "request", "sourceArtifactHash"),
                        ["properties"] = new JObject
                        {
                            ["schemaVersion"] = new JObject { ["const"] = MutationContractSchema.CurrentVersion },
                            ["operation"] = new JObject { ["type"] = "string" },
                            ["target"] = new JObject { ["type"] = "object" },
                            ["request"] = new JObject { ["type"] = "object" },
                            ["sourceArtifactHash"] = new JObject { ["type"] = "string", ["pattern"] = "^[A-Fa-f0-9]{64}$" }
                        }
                    },
                    ["operations"] = new JArray
                    {
                        OperationCapability("insert_derived_column", "Insert a Derived Column on one selected Data Flow path.",
                            "DataFlowPath", new[] { "sourceLineageId", "componentName", "outputName", "expression", "placement" },
                            DerivedColumnInputProperties()),
                        OperationCapability("insert_data_conversion", "Insert a Data Conversion on one selected Data Flow path.",
                            "DataFlowPath", new[] { "sourceLineageId", "componentName", "outputName", "outputDataType", "placement" },
                            DataConversionInputProperties()),
                        OperationCapability("update_transfer_tables", "Replace, add, or remove tables in one Transfer SQL Server Objects Task.",
                            "TaskHost", new[] { "mode", "tableNames" }, TransferTablesInputProperties()),
                        OperationCapability("set_task_property", "Set a writable scalar property on a selected TaskHost.",
                            "TaskHost", new[] { "propertyName", "value" }, TaskPropertyInputProperties()),
                        OperationCapability("update_task_collection", "Replace, add, or remove values in a writable native string collection.",
                            "TaskHost", new[] { "propertyName", "mode", "values" }, TaskCollectionInputProperties()),
                        OperationCapability("rename_task", "Rename one exact non-container executable.",
                            "Executable", new[] { "proposedName" }, new JObject
                            {
                                ["proposedName"] = StringField("New task name; omitted from mutation previews and results.")
                            })
                    },
                    ["placementRelations"] = new JArray(PlacementRelations),
                    ["packageShape"] = "Insertion operations select one task-scoped Data Flow path and splice a single-input, single-regular-output component; unrelated tasks, components, and paths are preserved. User variables and event handlers are unsupported by insertion plans. Package connection managers are retained and validated offline.",
                    ["applyAvailable"] = true,
                    ["applyCommand"] = "ai mutation.apply <source.dtsx> <destination.dtsx> <checkpoint-directory>",
                    ["applyValidation"] = "Native Package.Validate runs offline and does not execute the package or connect to data sources."
                },
                ["diagnostics"] = new JArray(),
                ["packageExecuted"] = false,
                ["nativeValidationPerformed"] = false,
                ["redaction"] = RedactionStatus()
            };
            output.WriteLine(envelope.ToString(Formatting.None));
            return 0;
        }

        private static JObject OperationCapability(string name, string description, string targetKind,
            string[] requiredInputs, JObject inputProperties) => new JObject
        {
            ["name"] = name,
            ["description"] = description,
            ["targetKind"] = targetKind,
            ["actions"] = new JArray("preview", "apply"),
            ["requiredInputs"] = new JArray(requiredInputs),
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray(requiredInputs),
                ["properties"] = inputProperties
            }
        };

        private static JObject DerivedColumnInputProperties()
        {
            var properties = CommonInsertionInputProperties();
            properties["expression"] = StringField("SSIS expression for the new output; omitted from preview results.");
            return properties;
        }

        private static JObject DataConversionInputProperties()
        {
            var properties = CommonInsertionInputProperties();
            properties["outputDataType"] = new JObject
            {
                ["type"] = "string",
                ["enum"] = new JArray("DT_WSTR", "DT_STR", "DT_I2", "DT_I4", "DT_I8", "DT_R4", "DT_R8")
            };
            properties["length"] = IntegerField("Required from 1 through 4000 for DT_WSTR and 1 through 8000 for DT_STR; otherwise zero.", 0);
            properties["precision"] = IntegerField("Must be zero; decimal conversions are not supported.", 0);
            properties["scale"] = IntegerField("Must be zero; decimal conversions are not supported.", 0);
            properties["codePage"] = IntegerField("Required and positive for DT_STR; otherwise zero.", 0);
            return properties;
        }

        private static JObject TransferTablesInputProperties() => new JObject
        {
            ["mode"] = new JObject
            {
                ["type"] = "string",
                ["enum"] = new JArray("Replace", "Add", "Remove"),
                ["description"] = "Replace sets the exact table selection and disables CopyAllObjects and CopyAllTables. Add preserves existing names and appends missing ones; Remove preserves unlisted names. Add and Remove require both CopyAll flags to be false; use Replace when changing a catch-all selection."
            },
            ["tableNames"] = new JObject
            {
                ["type"] = "array",
                ["items"] = new JObject { ["type"] = "string" },
                ["description"] = "Exact table names as used by the task, including schema qualification when required."
            }
        };

        private static JObject TaskPropertyInputProperties() => new JObject
        {
            ["propertyName"] = StringField("Writable scalar property name returned by ai task.properties."),
            ["value"] = new JObject
            {
                ["description"] = "Typed JSON scalar matching the native property type; for enum-valued properties use an enumValues name from ai task.properties, not a numeric code.",
                ["oneOf"] = new JArray
                {
                    new JObject { ["type"] = "string" },
                    new JObject { ["type"] = "boolean" },
                    new JObject { ["type"] = "integer" },
                    new JObject { ["type"] = "number" }
                }
            }
        };

        private static JObject TaskCollectionInputProperties() => new JObject
        {
            ["propertyName"] = StringField("Writable StringCollection property name returned by ai task.properties."),
            ["mode"] = new JObject
            {
                ["type"] = "string",
                ["enum"] = new JArray("Replace", "Add", "Remove"),
                ["description"] = "Replace sets the exact list; Add preserves existing items; Remove preserves unlisted items. Transfer task collections check or update their matching CopyAll flags."
            },
            ["values"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "string" } }
        };

        private static JObject CommonInsertionInputProperties() => new JObject
        {
            ["sourceComponentId"] = StringField("Optional legacy endpoint assertion; the selected path provides this identity."),
            ["destinationComponentId"] = StringField("Optional legacy endpoint assertion; the selected path provides this identity."),
            ["sourceLineageId"] = IntegerField("Positive native lineage ID for the source column.", 1),
            ["componentName"] = StringField("Name for the new component."),
            ["outputName"] = StringField("Name for the new output column."),
            ["placement"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray("relation", "width", "height"),
                ["properties"] = new JObject
                {
                    ["relation"] = new JObject { ["type"] = "string", ["enum"] = new JArray(PlacementRelations) },
                    ["anchorComponentId"] = new JObject { ["type"] = "string", ["description"] = "Required for RightOf and Below; omit for OpenSpace." },
                    ["width"] = IntegerField("Desired designer width in pixels.", 1),
                    ["height"] = IntegerField("Desired designer height in pixels.", 1)
                }
            }
        };

        private static JObject StringField(string description) => new JObject
        {
            ["type"] = "string",
            ["description"] = description
        };

        private static JObject IntegerField(string description, int minimum) => new JObject
        {
            ["type"] = "integer",
            ["minimum"] = minimum,
            ["description"] = description
        };

        private static int WriteSuccess(TextWriter output, string tool, string operation,
            PreparedMutationPlan plan, JObject request)
        {
            var envelope = CreateEnvelope(tool, operation, plan, request, BuildPreviewResult(plan),
                Array.Empty<string>(), succeeded: true, exitCode: 0, nativeValidationPerformed: false);
            output.WriteLine(envelope.ToString(Formatting.None));
            return 0;
        }

        private static Host CreateHost(string checkpointDirectory) =>
            new Host(new PackageLoader(), new PackageAnalysisSnapshotFactory(), new CorpusSnapshotBuilder(),
                new CorpusFingerprintProvider(), new MutationPreviewer(),
                new FileMutationCheckpointService(checkpointDirectory), new NativePackageMutationValidator(),
                new MutationArtifactStager());

        private static int WriteApplyResult(TextWriter output, string operation, PreparedMutationPlan plan,
            JObject request, MutationHostResult result)
        {
            var completed = result.Completed;
            var code = completed ? "mutation.completed" : SafeCode(result.Code) ?? "mutation.apply.failed";
            var diagnostics = result.DiagnosticCodes.Select(SafeCode).Where(value => value != null)
                .Distinct(StringComparer.Ordinal).Select(value => value!).ToArray();
            var envelope = CreateEnvelope("mutation.apply", operation, plan, request,
                BuildApplyResult(plan, result, code), diagnostics, completed, completed ? 0 : 4,
                result.ReloadSucceeded);
            output.WriteLine(envelope.ToString(Formatting.None));
            return completed ? 0 : 4;
        }

        private static JObject CreateEnvelope(string tool, string operation, PreparedMutationPlan plan,
            JObject request, MutationEnvelopeResult<JObject> result, IEnumerable<string> diagnostics,
            bool succeeded, int exitCode, bool nativeValidationPerformed)
        {
            var target = ToCamelCase(request["target"] as JObject ?? new JObject()) as JObject ?? new JObject();
            var contract = new MutationEnvelope<JObject, JObject, JObject, JObject>(operation, target, new JObject(),
                plan.SourceArtifactHash, plan.Requirements, plan.Preview, result, diagnostics,
                plan.Coverage, new MutationRedactionStatus("mutation-preview", true, true, true, true));
            var envelope = JObject.Parse(MutationContractSerializer.Serialize(contract));
            envelope["tool"] = tool;
            envelope["succeeded"] = succeeded;
            envelope["exitCode"] = exitCode;
            envelope["packageExecuted"] = false;
            envelope["nativeValidationPerformed"] = nativeValidationPerformed;
            return envelope;
        }

        private static JObject SafePreview(IPackageMutationPlan plan)
        {
            var preview = ToCamelCase(JObject.Parse(plan.SerializePreviewContract())) as JObject ?? new JObject();
            preview.Remove("schemaVersion");
            return preview;
        }

        private static MutationEnvelopeResult<JObject> BuildPreviewResult(PreparedMutationPlan plan)
        {
            var notApplicable = Stage(MutationStageStatus.NotApplicable);
            return new MutationEnvelopeResult<JObject>(RuntimeIdentity(), plan.Adapter,
                new MutationOperationResult<JObject>(MutationOutcome.PreviewReady, "mutation.preview.ready", new JObject()),
                Artifact(MutationStageStatus.NotApplicable, plan.SourceArtifactHash, string.Empty, string.Empty),
                notApplicable, notApplicable, notApplicable, notApplicable,
                new MutationRollbackResult(MutationRollbackStatus.NotRequired, null));
        }

        private static MutationEnvelopeResult<JObject> BuildApplyResult(PreparedMutationPlan plan,
            MutationHostResult result, string code)
        {
            var completed = result.Completed;
            var mutationOutcome = completed ? MutationOutcome.AppliedAndVerified
                : result.CheckpointCreated ? MutationOutcome.FailedAndRestorationUnverified
                : MutationOutcome.RejectedBeforeMutation;
            var artifactStatus = result.SaveAsCompleted ? MutationStageStatus.Succeeded
                : result.CheckpointCreated ? MutationStageStatus.Failed : MutationStageStatus.NotApplicable;
            var reloadStatus = result.ReloadSucceeded ? MutationStageStatus.Succeeded
                : result.SaveAsCompleted ? MutationStageStatus.Failed : MutationStageStatus.NotApplicable;
            var validationStatus = result.ValidationSucceeded ? MutationStageStatus.Succeeded
                : result.ReloadSucceeded ? MutationStageStatus.Failed : MutationStageStatus.NotApplicable;
            var semanticStatus = result.SemanticDiffMatched ? MutationStageStatus.Succeeded
                : result.ValidationSucceeded ? MutationStageStatus.Failed : MutationStageStatus.NotApplicable;
            var layoutStatus = plan.LayoutVerificationRequired
                ? completed ? MutationStageStatus.Succeeded
                    : result.ValidationSucceeded ? MutationStageStatus.Unverified : MutationStageStatus.NotApplicable
                : MutationStageStatus.NotApplicable;
            return new MutationEnvelopeResult<JObject>(RuntimeIdentity(), plan.Adapter,
                new MutationOperationResult<JObject>(mutationOutcome, code,
                    new JObject { ["operationId"] = result.OperationId }),
                Artifact(artifactStatus, result.SourceArtifactHash, result.DestinationArtifactHash, result.CheckpointId),
                Stage(reloadStatus), Stage(validationStatus), Stage(semanticStatus),
                Stage(layoutStatus, layoutStatus == MutationStageStatus.Unverified
                    ? new[] { "mutation.layout.result_unavailable" } : Array.Empty<string>()),
                new MutationRollbackResult(completed || !result.CheckpointCreated
                    ? MutationRollbackStatus.NotRequired : MutationRollbackStatus.RestorationUnverified, null));
        }

        private static MutationRuntimeIdentity RuntimeIdentity() =>
            new MutationRuntimeIdentity(16, Environment.Is64BitProcess ? "x64" : "x86");

        private static MutationAdapterIdentity AdapterIdentity(IPackageMutationPlan plan)
        {
            var kind = plan switch
            {
                DerivedColumnInsertionPlan => "ssis16.dataflow.derived-column",
                DataConversionInsertionPlan => "ssis16.dataflow.data-conversion",
                TransferTablesMutationPlan => "ssis16.transfer-tables",
                TaskHostPropertyMutationPlan => "ssis16.task-host-property",
                _ => "ssis16." + plan.GetType().Name
            };
            return new MutationAdapterIdentity(kind, "1.0");
        }

        private static MutationArtifactResult Artifact(MutationStageStatus status, string sourceHash,
            string destinationHash, string checkpointId) =>
            new MutationArtifactResult(status, sourceHash, destinationHash, checkpointId);

        private static MutationStageResult Stage(MutationStageStatus status, IEnumerable<string>? diagnosticCodes = null) =>
            new MutationStageResult(status, diagnosticCodes ?? Array.Empty<string>());

        private static int WriteError(TextWriter output, string? tool, int exitCode, string code)
        {
            var envelope = new JObject
            {
                ["schemaVersion"] = tool == "task.properties" ? "1.0" : MutationContractSchema.CurrentVersion,
                ["tool"] = tool,
                ["succeeded"] = false,
                ["exitCode"] = exitCode,
                ["results"] = (JObject?)null,
                ["diagnostics"] = new JArray(new JObject { ["code"] = code }),
                ["packageExecuted"] = false,
                ["nativeValidationPerformed"] = false,
                ["redaction"] = RedactionStatus()
            };
            output.WriteLine(envelope.ToString(Formatting.None));
            return exitCode;
        }

        private static JObject RedactionStatus() => new JObject
        {
            ["policy"] = "mutation-preview",
            ["requestValuesOmitted"] = true,
            ["expressionTextOmitted"] = true,
            ["secretInputsOmitted"] = true,
            ["packagePathOmitted"] = true
        };

        private static T DeserializeRequest<T>(JToken? token) where T : class
        {
            if (token?.Type != JTokenType.Object) { throw new ArgumentException("request"); }
            return token.ToObject<T>(JsonSerializer.Create(RequestSerializerSettings)) ??
                throw new ArgumentException("request");
        }

        private static string RequireTaskTarget(MutationCommandTarget target)
        {
            if (!string.Equals(target.Kind, "TaskHost", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(target.NativeId) || !string.IsNullOrWhiteSpace(target.OwnerNativeId))
            { throw new ArgumentException("target"); }
            return target.NativeId;
        }

        private static MutationCommandTarget RequirePathTarget(MutationCommandTarget target)
        {
            if (!string.Equals(target.Kind, "DataFlowPath", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(target.NativeId) || string.IsNullOrWhiteSpace(target.OwnerNativeId))
            { throw new ArgumentException("target"); }
            return target;
        }

        private static ComponentPlacementIntent CreatePlacement(string? ownerNativeId,
            MutationPlacementRequest? placement)
        {
            if (placement == null) { throw new ArgumentException("placement"); }
            if (!Enum.TryParse(RequiredValue(placement.Relation, "placement.relation"), true,
                out ComponentPlacementRelation relation) || !Enum.IsDefined(typeof(ComponentPlacementRelation), relation))
            { throw new ArgumentException("placement.relation"); }
            return new ComponentPlacementIntent(RequiredValue(ownerNativeId, "target.ownerNativeId"), relation,
                placement.AnchorComponentId ?? string.Empty, placement.Width, placement.Height);
        }

        private static int RequiredPositive(int value, string name)
        {
            if (value <= 0) { throw new ArgumentException(name); }
            return value;
        }

        private static string RequiredValue(string? value, string name)
        {
            if (string.IsNullOrWhiteSpace(value)) { throw new ArgumentException(name); }
            return value!;
        }

        private static string[] RequiredValues(string[]? values, string name)
        {
            if (values == null || values.Any(value => value == null)) { throw new ArgumentException(name); }
            return values;
        }

        private static JToken ToCamelCase(JToken token)
        {
            if (token is JObject objectToken)
            {
                var result = new JObject();
                foreach (var property in objectToken.Properties())
                {
                    var name = char.ToLowerInvariant(property.Name[0]) + property.Name.Substring(1);
                    result[name] = ToCamelCase(property.Value);
                }
                return result;
            }
            if (token is JArray arrayToken)
            {
                return new JArray(arrayToken.Select(ToCamelCase));
            }
            return token.DeepClone();
        }

        private static string RequiredString(JObject value, string name)
        {
            var token = value[name];
            if (token?.Type != JTokenType.String || string.IsNullOrWhiteSpace((string?)token))
            { throw new ArgumentException(name); }
            return (string)token!;
        }

        private static string OptionalString(JObject value, string name)
        {
            var token = value[name];
            return token?.Type == JTokenType.String ? (string)token! : string.Empty;
        }

        private static string[] RequiredStringArray(JObject value, string name)
        {
            if (!(value[name] is JArray array)) { throw new ArgumentException(name); }
            return array.Select(token => token.Type == JTokenType.String
                ? token.ToString()
                : throw new ArgumentException(name)).ToArray();
        }

        private static int RequiredInt(JObject value, string name)
        {
            var token = value[name];
            if (token?.Type != JTokenType.Integer) { throw new ArgumentException(name); }
            return (int)token;
        }

        private static int OptionalInt(JObject value, string name)
        {
            var token = value[name];
            if (token == null) { return 0; }
            if (token.Type != JTokenType.Integer) { throw new ArgumentException(name); }
            return (int)token;
        }

        private static string Hash(Stream stream)
        {
            stream.Position = 0;
            using (var algorithm = SHA256.Create())
            { return string.Concat(algorithm.ComputeHash(stream).Select(value => value.ToString("x2"))); }
        }

        private static string? SafeCode(string message) =>
            Regex.IsMatch(message ?? string.Empty, "^(mutation|designer|package|ssis)\\.[a-zA-Z0-9._-]{1,120}$") ? message : null;
    }
}