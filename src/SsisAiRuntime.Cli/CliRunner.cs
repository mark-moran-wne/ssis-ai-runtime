#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using SsisAiRuntime.Inspectors;

namespace SsisAiRuntime.Cli
{
    public sealed class CliRunner
    {
        private readonly Func<CliInspectionRequest, CliInspection> inspect;

        public CliRunner(Func<string, string, CliInspection> inspect)
        {
            if (inspect == null) { throw new ArgumentNullException(nameof(inspect)); }
            this.inspect = invocation => inspect(invocation.Command, invocation.PackagePath);
        }

        public CliRunner(Func<string, string, CliTraceRequest?, CliInspection> inspect)
        {
            if (inspect == null) { throw new ArgumentNullException(nameof(inspect)); }
            this.inspect = invocation => inspect(invocation.Command, invocation.PackagePath, invocation.Trace);
        }

        public CliRunner(Func<string, string, CliTraceRequest?, CliTaskRequest?, CliInspection> inspect)
        {
            if (inspect == null) { throw new ArgumentNullException(nameof(inspect)); }
            this.inspect = invocation => inspect(invocation.Command, invocation.PackagePath, invocation.Trace, invocation.Task);
        }

        public CliRunner(Func<CliInspectionRequest, CliInspection> inspect)
        {
            this.inspect = inspect ?? throw new ArgumentNullException(nameof(inspect));
        }

        public int Run(string[]? args, TextWriter output)
        {
            string? command = null;
            var summary = true;
            try
            {
                CliTraceRequest? request = null;
                CliTaskRequest? taskRequest = null;
                CliSearchRequest? searchRequest = null;
                IReadOnlyList<string>? includedOperations = null;
                var inspectBatch = args != null && args.Length >= 2 && args[0] == "inspect";
                var trace = args != null && args.Length >= 2 && args[0] == "trace";
                var taskQuery = args != null && args.Length >= 2 && (args[0] == "predecessors" || args[0] == "successors");
                var search = args != null && args.Length >= 2 && args[0] == "search";
                var valid = false;
                if (inspectBatch)
                {
                    valid = TryParseInspectArguments(args!, out includedOperations, out summary);
                }
                else if (search)
                {
                    valid = CliSearchRequest.TryParse(args!, out searchRequest, out summary);
                }
                else if (trace)
                {
                    valid = CliTraceRequest.TryParse(args!, out request, out summary);
                }
                else if (taskQuery)
                {
                    valid = CliTaskRequest.TryParse(args!, out taskRequest, out summary);
                }
                else
                {
                    summary = args == null || args.Length != 3 || args[2] != "--details";
                    valid = args != null && (args.Length == 2 || args.Length == 3 && args[2] == "--details") &&
                        (CliInspectionBatch.Operations.Contains(args[0]) || args[0] == "control-flow");
                }
                if (!valid || args == null || string.IsNullOrWhiteSpace(args[1]))
                {
                    var usage = string.Join(Environment.NewLine, new[]
                    {
                        "Usage:",
                        "  SsisAiRuntime.Cli.exe <overview|sql|lineage|configuration|control-flow> <package.dtsx> [--details]",
                        "  SsisAiRuntime.Cli.exe inspect <package.dtsx> [--include <overview|sql|lineage|configuration>[,...]]... [--details]",
                        "  Repeat --include or comma-separate report names; omit it to include all four reports.",
                        "  SsisAiRuntime.Cli.exe trace <package.dtsx> --flow <id> --component <id> --column <id> [--direction upstream|downstream] [--details]",
                        "  SsisAiRuntime.Cli.exe <predecessors|successors> <package.dtsx> --task <id> [--recursive] [--details]",
                        "  SsisAiRuntime.Cli.exe search <package.dtsx> --query <text> [--kind <object-kind>] [--details]",
                        "",
                        "Output defaults to a bounded summary. Use --details for the full redacted projection."
                    });
                    return Write(output, null, 2, null, new JArray(Diagnostic("cli.usage", "Error",
                        usage)), new JArray());
                }

                command = args[0];
                if (!File.Exists(args[1]))
                {
                    return Write(output, command, 3, null, new JArray(Diagnostic("package.path.not_found", "Error",
                        "The package file is missing or inaccessible. The path is omitted.")), new JArray());
                }

                var inspection = inspect(new CliInspectionRequest(command, args[1], request, taskRequest, searchRequest, includedOperations));
                var envelope = command == "inspect" && inspection.Results is CliInspectionBatch batch && !inspection.Diagnostics.HasErrors
                    ? BuildBatch(batch, summary, includedOperations ?? CliInspectionBatch.Operations)
                    : BuildOperation(command, inspection, summary);
                output.WriteLine(envelope.ToString(Formatting.None));
                return (int)envelope["exitCode"]!;
            }
            catch (Exception)
            {
                return Write(output, command, 4, null, new JArray(Diagnostic("cli.inspection.failed", "Error",
                    "Inspection failed. Runtime dependencies may be unavailable. Exception details are omitted.")), new JArray());
            }
        }

        private static bool TryParseInspectArguments(string[] args, out IReadOnlyList<string>? includedOperations, out bool summary)
        {
            includedOperations = null;
            summary = true;
            var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var detailsSpecified = false;
            for (var index = 2; index < args.Length; index++)
            {
                if (args[index] == "--details")
                {
                    if (detailsSpecified) { return false; }
                    summary = false;
                    detailsSpecified = true;
                }
                else if (args[index] == "--include")
                {
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]) || args[index + 1].StartsWith("--"))
                    {
                        return false;
                    }
                    foreach (var value in args[++index].Split(','))
                    {
                        var operation = value.Trim();
                        if (operation.Length == 0 || !CliInspectionBatch.Operations.Contains(operation, StringComparer.OrdinalIgnoreCase) ||
                            !selected.Add(operation)) { return false; }
                    }
                }
                else
                {
                    return false;
                }
            }
            if (selected.Count > 0)
            {
                includedOperations = CliInspectionBatch.Operations.Where(selected.Contains).ToArray();
            }
            return true;
        }

        private static JObject BuildOperation(string command, CliInspection inspection, bool summary)
        {
            try
            {
                var diagnostics = new JArray(inspection.Diagnostics.Items.Select(item => Diagnostic(item.Code,
                    item.Severity.ToString(), "Native diagnostic text omitted.")));
                if (inspection.Diagnostics.HasErrors)
                {
                    return CreateEnvelope(command, inspection.FailureExitCode, null, diagnostics, new JArray(),
                        summary ? CliSummary.Coverage(Array.Empty<UnsupportedItem>()) : null);
                }
                if (inspection.Results == null || command == "inspect")
                {
                    throw new InvalidOperationException("Missing or unexpected inspection results.");
                }

                var unsupported = new JArray(inspection.UnsupportedItems.Take(summary ? 5 : int.MaxValue).Select(item => new JObject
                {
                    ["id"] = summary ? CliSummary.LimitText(item.Id) : item.Id,
                    ["name"] = summary ? CliSummary.LimitText(item.Name) : item.Name,
                    ["creationName"] = summary ? CliSummary.LimitText(item.CreationName) : item.CreationName,
                    ["reasonCode"] = item.ReasonCode,
                    ["reason"] = "Details omitted under the metadata-only redaction policy."
                }));
                var serializer = JsonSerializer.Create(new JsonSerializerSettings
                {
                    ContractResolver = new MetadataContractResolver(),
                    Converters = { new Newtonsoft.Json.Converters.StringEnumConverter() }
                });
                var results = summary ? CliSummary.Results(command, inspection.Results) : JToken.FromObject(inspection.Results, serializer);
                return CreateEnvelope(command, inspection.UnsupportedItems.Count == 0 ? 0 : 5, results, diagnostics, unsupported,
                    summary ? CliSummary.Coverage(inspection.UnsupportedItems) : null);
            }
            catch (Exception)
            {
                return CreateEnvelope(command, 4, null, new JArray(Diagnostic("cli.inspection.failed", "Error",
                    "Inspection failed. Exception details are omitted.")), new JArray(),
                    summary ? CliSummary.Coverage(Array.Empty<UnsupportedItem>()) : null);
            }
        }

        private static JObject BuildBatch(CliInspectionBatch batch, bool summary, IReadOnlyList<string> operations)
        {
            var reports = new JObject();
            var unsupported = new List<UnsupportedItem>();
            var diagnostics = new JArray();
            var skipped = new JArray();
            var exitCode = 0;
            foreach (var operation in operations)
            {
                if (exitCode == 3 || exitCode == 4)
                {
                    skipped.Add(operation);
                    continue;
                }
                if (!batch.Reports.TryGetValue(operation, out var inspection))
                {
                    throw new InvalidOperationException("Missing operation report.");
                }

                var report = BuildOperation(operation, inspection, summary);
                reports[operation] = report;
                var operationExitCode = (int)report["exitCode"]!;
                if (operationExitCode != 0)
                {
                    exitCode = operationExitCode;
                }
                if (operationExitCode == 0 || operationExitCode == 5)
                {
                    unsupported.AddRange(inspection.UnsupportedItems);
                }
                foreach (var item in inspection.Diagnostics.Items)
                {
                    diagnostics.Add(Diagnostic(item.Code, item.Severity.ToString(), "Native diagnostic text omitted."));
                }
                if (operationExitCode == 4 && !inspection.Diagnostics.HasErrors)
                {
                    diagnostics.Add(Diagnostic("cli.inspection.failed", "Error", "An operation report could not be produced."));
                }
            }
            if (batch.Reports.Count != reports.Count)
            {
                throw new InvalidOperationException("Unexpected operation reports.");
            }

            var coverage = summary ? CliSummary.Coverage(unsupported) : null;
            if (coverage != null)
            {
                coverage["examplesOmitted"] = unsupported.Count;
            }
            var envelope = CreateEnvelope("inspect", exitCode, reports, diagnostics, new JArray(), coverage);
            envelope["completedOperations"] = new JArray(reports.Properties().Select(property => property.Name));
            envelope["skippedOperations"] = skipped;
            return envelope;
        }

        private static JObject Diagnostic(string code, string severity, string message)
        {
            return new JObject { ["code"] = code, ["severity"] = severity, ["message"] = message };
        }

        private static int Write(TextWriter output, string? command, int exitCode, JToken? results, JArray diagnostics, JArray unsupported, JObject? coverage = null)
        {
            output.WriteLine(CreateEnvelope(command, exitCode, results, diagnostics, unsupported, coverage).ToString(Formatting.None));
            return exitCode;
        }

        private static JObject CreateEnvelope(string? command, int exitCode, JToken? results, JArray diagnostics, JArray unsupported, JObject? coverage = null)
        {
            if (coverage != null)
            {
                coverage["diagnosticCount"] = diagnostics.Count;
                coverage["diagnosticsOmitted"] = Math.Max(0, diagnostics.Count - 8);
                diagnostics = new JArray(diagnostics.OrderByDescending(item => (string?)item["severity"] == "Error")
                    .Take(8).Select(item => new JObject
                    {
                        ["code"] = CliSummary.LimitText((string)item["code"]!),
                        ["severity"] = item["severity"]!.DeepClone(),
                        ["message"] = item["message"]!.DeepClone()
                    }));
            }
            var envelope = new JObject
            {
                ["schemaVersion"] = "1.0",
                ["command"] = command,
                ["succeeded"] = exitCode == 0 || exitCode == 5,
                ["isComplete"] = exitCode == 0,
                ["exitCode"] = exitCode,
                ["results"] = results,
                ["diagnostics"] = diagnostics,
                ["unsupportedItems"] = unsupported,
                ["redaction"] = new JObject
                {
                    ["policy"] = "metadata-only",
                    ["applied"] = true,
                    ["connectionStringsOmitted"] = true,
                    ["variableValuesOmitted"] = true,
                    ["parameterValuesOmitted"] = true,
                    ["expressionTextOmitted"] = true,
                    ["sqlTextOmitted"] = true,
                    ["settingValuesOmitted"] = true,
                    ["descriptionsOmitted"] = true,
                    ["diagnosticDetailsOmitted"] = true
                }
            };
            if (coverage != null)
            {
                envelope["outputMode"] = "summary";
                envelope["coverage"] = coverage;
            }

            return envelope;
        }

        private sealed class MetadataContractResolver : CamelCasePropertyNamesContractResolver
        {
            protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
            {
                var property = base.CreateProperty(member, memberSerialization);
                if (member.Name == "Description" || member.Name == "UnsupportedItems" ||
                    (member.DeclaringType == typeof(SqlStatementOverview) && member.Name == "StatementText") ||
                    (member.DeclaringType == typeof(DataFlowSettingOverview) && member.Name == "Value"))
                {
                    property.Ignored = true;
                }

                return property;
            }
        }
    }
}