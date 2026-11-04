#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.SqlServer.Dts.Pipeline.Wrapper;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SsisAiRuntime.Corpus;
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

        public int Run(string[] args, TextReader input, TextWriter output)
        {
            var tool = args != null && args.Length > 1 ? args[1] : null;
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
            if ((string?)request["schemaVersion"] != "1.0")
            { return WriteError(output, tool, 2, "ai.mutation.request.schema_unsupported"); }

            var operation = (string?)request["operation"];
            if (operation != "insert_derived_column" && operation != "insert_data_conversion")
            { return WriteError(output, tool, 2, "ai.mutation.operation.unsupported"); }

            try
            {
                using (var sourceGuard = new FileStream(args[2], FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var sourceHash = Hash(sourceGuard);
                    var load = new PackageLoader().Load(args[2]);
                    if (!load.Succeeded || load.Session == null)
                    { return WriteError(output, tool, 3, "ssis.package.load_failed"); }

                    IComponentInsertionPlan plan;
                    try
                    {
                        plan = CreatePlan(load.Session.Package, args[2], sourceHash, request, operation!);
                    }
                    finally { load.Session.Package.Dispose(); }

                    if (!isApply)
                    {
                        return WriteSuccess(output, tool!, operation!, JObject.Parse(plan.SerializePreviewContract()));
                    }

                    var host = CreateHost(args[4]);
                    var result = host.ExecuteAsync(new MutationHostRequest(args[2], args[3], plan, sourceHash),
                        CancellationToken.None).GetAwaiter().GetResult();
                    return WriteApplyResult(output, operation!, result);
                }
            }
            catch (Exception exception)
            {
                var code = SafeCode(exception.Message) ?? "ai.mutation.preview.failed";
                return WriteError(output, tool, 4, code);
            }
        }

        private static IComponentInsertionPlan CreatePlan(Microsoft.SqlServer.Dts.Runtime.Package package,
            string packagePath, string sourceHash, JObject request, string operation)
        {
            var placement = request["placement"] as JObject ?? throw new ArgumentException("placement");
            var relationName = RequiredString(placement, "relation");
            if (!Enum.TryParse(relationName, true, out ComponentPlacementRelation relation) ||
                !Enum.IsDefined(typeof(ComponentPlacementRelation), relation))
            { throw new ArgumentException("placement.relation"); }

            var taskId = RequiredString(request, "taskId");
            var anchorId = OptionalString(placement, "anchorComponentId");
            var placementIntent = new ComponentPlacementIntent(taskId, relation, anchorId,
                RequiredInt(placement, "width"), RequiredInt(placement, "height"));

            var sourceComponentId = RequiredString(request, "sourceComponentId");
            var destinationComponentId = RequiredString(request, "destinationComponentId");
            var sourceLineageId = RequiredInt(request, "sourceLineageId");
            var componentName = RequiredString(request, "componentName");
            var outputName = RequiredString(request, "outputName");

            if (operation == "insert_derived_column")
            {
                return DerivedColumnInsertionPlan.Create(package, sourceHash, taskId, sourceComponentId,
                    destinationComponentId, sourceLineageId, componentName, outputName,
                    RequiredString(request, "expression"), placementIntent, packagePath);
            }

            if (!Enum.TryParse(RequiredString(request, "outputDataType"), true,
                out RuntimeWrapper.DataType outputDataType))
            { throw new ArgumentException("outputDataType"); }
            return DataConversionInsertionPlan.Create(package, sourceHash, taskId, sourceComponentId,
                destinationComponentId, sourceLineageId, componentName, outputName, outputDataType,
                OptionalInt(request, "length"), OptionalInt(request, "precision"), OptionalInt(request, "scale"),
                OptionalInt(request, "codePage"), placementIntent, packagePath);
        }

        private static int WriteCapabilities(TextWriter output)
        {
            var envelope = new JObject
            {
                ["schemaVersion"] = "1.0",
                ["tool"] = "mutation.capabilities",
                ["succeeded"] = true,
                ["exitCode"] = 0,
                ["results"] = new JObject
                {
                    ["schemaVersion"] = "1.0",
                    ["operations"] = new JArray
                    {
                        OperationCapability("insert_derived_column", "Preview a positioned Derived Column insertion.",
                            new[] { "taskId", "sourceComponentId", "destinationComponentId", "sourceLineageId", "componentName", "outputName", "expression", "placement" },
                            DerivedColumnInputProperties()),
                        OperationCapability("insert_data_conversion", "Preview a positioned Data Conversion insertion.",
                            new[] { "taskId", "sourceComponentId", "destinationComponentId", "sourceLineageId", "componentName", "outputName", "outputDataType", "placement" },
                            DataConversionInputProperties())
                    },
                    ["placementRelations"] = new JArray(PlacementRelations),
                    ["packageShape"] = "Current insertion plans support one pipeline task with two components and one path; user variables and event handlers are not yet supported. Package connection managers are retained and validated offline.",
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

        private static JObject OperationCapability(string name, string description, string[] requiredInputs,
            JObject inputProperties) => new JObject
        {
            ["name"] = name,
            ["description"] = description,
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

        private static JObject CommonInsertionInputProperties() => new JObject
        {
            ["taskId"] = StringField("Native ID of the data-flow task."),
            ["sourceComponentId"] = StringField("Native ID of the component producing the selected lineage."),
            ["destinationComponentId"] = StringField("Native ID of the component consuming the current path."),
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

        private static int WriteSuccess(TextWriter output, string tool, string operation, JObject preview)
        {
            var envelope = new JObject
            {
                ["schemaVersion"] = "1.0",
                ["tool"] = tool,
                ["succeeded"] = true,
                ["exitCode"] = 0,
                ["results"] = new JObject { ["operation"] = operation, ["preview"] = ToCamelCase(preview) },
                ["diagnostics"] = new JArray(),
                ["packageExecuted"] = false,
                ["nativeValidationPerformed"] = false,
                ["redaction"] = RedactionStatus()
            };
            output.WriteLine(envelope.ToString(Formatting.None));
            return 0;
        }

        private static Host CreateHost(string checkpointDirectory) =>
            new Host(new PackageLoader(), new PackageAnalysisSnapshotFactory(), new CorpusSnapshotBuilder(),
                new CorpusFingerprintProvider(), new MutationPreviewer(),
                new FileMutationCheckpointService(checkpointDirectory), new NativePackageMutationValidator(),
                new MutationArtifactStager());

        private static int WriteApplyResult(TextWriter output, string operation, MutationHostResult result)
        {
            var completed = result.Completed;
            var code = completed ? "mutation.completed" : SafeCode(result.Code) ?? "mutation.apply.failed";
            var diagnostics = new JArray(result.DiagnosticCodes
                .Select(SafeCode).Where(value => value != null).Distinct(StringComparer.Ordinal)
                .Select(value => new JObject { ["code"] = value }));
            var envelope = new JObject
            {
                ["schemaVersion"] = "1.0",
                ["tool"] = "mutation.apply",
                ["succeeded"] = completed,
                ["exitCode"] = completed ? 0 : 4,
                ["results"] = new JObject
                {
                    ["operation"] = operation,
                    ["status"] = result.Status.ToString(),
                    ["code"] = code,
                    ["operationId"] = result.OperationId,
                    ["checkpointId"] = result.CheckpointId,
                    ["checkpointCreated"] = result.CheckpointCreated,
                    ["saveAsCompleted"] = result.SaveAsCompleted,
                    ["reloadSucceeded"] = result.ReloadSucceeded,
                    ["validationSucceeded"] = result.ValidationSucceeded,
                    ["semanticDiffMatched"] = result.SemanticDiffMatched,
                    ["destinationArtifactHash"] = result.DestinationArtifactHash
                },
                ["diagnostics"] = diagnostics,
                ["packageExecuted"] = false,
                ["nativeValidationPerformed"] = result.ReloadSucceeded,
                ["redaction"] = RedactionStatus()
            };
            output.WriteLine(envelope.ToString(Formatting.None));
            return completed ? 0 : 4;
        }

        private static int WriteError(TextWriter output, string? tool, int exitCode, string code)
        {
            var envelope = new JObject
            {
                ["schemaVersion"] = "1.0",
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
            ["packagePathOmitted"] = true
        };

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