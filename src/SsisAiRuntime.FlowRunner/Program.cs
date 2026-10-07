using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SsisAiRuntime.FlowRunner
{
    internal static class Program
    {
        private static string command = "flow.run";

        private static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--worker") { return Worker(args[1]); }
            if (args.Length == 1 && args[0] == "compare")
            {
                try { return Write(DeveloperToolResult.Success("flow.compare", StructuralXmlComparer.Compare(Console.In))); }
                catch (Exception error) { return Write(DeveloperToolResult.Failure("flow.compare", "flow.compare.failed", "compare", error, 2)); }
            }
            if (args.Length == 2 && args[0] == "describe")
            {
                var describer = new NativeComponentDescriber();
                try { return Write(DeveloperToolResult.Success("flow.describe", describer.Describe(args[1]))); }
                catch (Exception error) { return Write(DeveloperToolResult.Failure("flow.describe", "flow.describe.failed", describer.Stage, error)); }
            }
            if (args.Length >= 2 && args.Length <= 3 && args[0] == "catalog" && args[1] == "validate")
            {
                try
                {
                    var definitions = args.Length == 2 ? SharedComponentCatalog.Load() : SharedComponentCatalog.Parse(File.ReadAllText(args[2]));
                    return Write(DeveloperToolResult.Success("flow.catalog.validate", new JObject
                    {
                        ["definitionCount"] = definitions.Count,
                        ["scope"] = args.Length == 2 ? "embedded-catalog" : "single-file",
                        ["nativeSupportVerified"] = false
                    }));
                }
                catch (Exception error) { return Write(DeveloperToolResult.Failure("flow.catalog.validate", "flow.catalog.invalid", "catalog-validation", error, 2)); }
            }
            if (args.Length == 1 && args[0] == "components")
            {
                command = "flow.components";
                try
                {
                    Console.WriteLine(InstalledComponentCatalog.Discover().ToString(Formatting.None));
                    return 0;
                }
                catch (Exception) { return Emit(false, "flow.components.failed", 4, 0); }
            }
            var namedProbe = args.Length == 2 && args[0] == "probe";
            if (!namedProbe && (args.Length != 1 || (args[0] != "demo" && args[0] != "run")))
            {
                return Emit(false, "flow.usage", 2, 0);
            }
            command = "flow." + args[0];
            FlowProbeRequest request;
            try
            {
                request = args[0] == "demo" ? FlowProbeRequest.Demo() : FlowProbeRequest.Read(Console.In);
                if (namedProbe && request.Recipe != args[1]) { throw new ArgumentException("The named probe must match the request recipe."); }
            }
            catch (Exception error) { return Write(DeveloperToolResult.Failure(command, "flow.request.invalid", "request", error, 2)); }

            var directory = Path.Combine(Path.GetTempPath(), "SsisFlowProbe-" + Guid.NewGuid().ToString("N"));
            var report = Report(false, "flow.worker.failed", 4, 0);
            try
            {
                Directory.CreateDirectory(directory);
                using (var process = new Process
                {
                    StartInfo = new ProcessStartInfo(typeof(Program).Assembly.Location,
                        "--worker \"" + directory + "\"")
                    {
                        UseShellExecute = false,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                })
                {
                    process.Start();
                    var stdout = process.StandardOutput.ReadToEndAsync();
                    var stderr = process.StandardError.ReadToEndAsync();
                    var stdin = Task.Run(() =>
                    {
                        process.StandardInput.Write(request.ToJson());
                        process.StandardInput.Close();
                    });
                    if (!process.WaitForExit(60000))
                    {
                        process.Kill();
                        process.WaitForExit();
                        report = Report(false, "flow.timeout", 4, 0);
                    }
                    else
                    {
                        Task.WaitAll(stdin, stdout, stderr);
                        report = JObject.Parse(stdout.Result);
                        if ((int)report["exitCode"] != process.ExitCode) { throw new InvalidOperationException(); }
                        report["command"] = command;
                    }
                }
            }
            catch (Exception error) { report = DeveloperToolResult.Failure(command, "flow.worker.failed", "worker", error); }
            finally
            {
                try
                {
                    if (Directory.Exists(directory)) { Directory.Delete(directory, true); }
                    report["cleanupSucceeded"] = true;
                }
                catch (Exception error)
                {
                    if ((bool)report["succeeded"]) { report = DeveloperToolResult.Failure(command, "flow.cleanup.failed", "cleanup", error); }
                    else
                    {
                        if (!(report["diagnostics"] is JArray)) { report["diagnostics"] = new JArray(); }
                        ((JArray)report["diagnostics"]).Add(DeveloperToolResult.Failure(command, "flow.cleanup.failed", "cleanup", error)["diagnostics"][0].DeepClone());
                    }
                    report["cleanupSucceeded"] = false;
                }
            }
            Console.WriteLine(report.ToString(Formatting.None));
            return (int)report["exitCode"];
        }

        private static int Worker(string directory)
        {
            var probe = new NativeSyntheticFlowProbe();
            try
            {
                var fullPath = Path.GetFullPath(directory);
                if (!string.Equals(Path.GetDirectoryName(fullPath), Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar),
                        StringComparison.OrdinalIgnoreCase) ||
                    !Path.GetFileName(fullPath).StartsWith("SsisFlowProbe-", StringComparison.Ordinal) ||
                    !Directory.Exists(fullPath) || Directory.EnumerateFileSystemEntries(fullPath).Any() ||
                    (File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
                {
                    return Emit(false, "flow.worker.directory_invalid", 4, 0);
                }
                var request = FlowProbeRequest.Read(Console.In);
                probe.Run(directory, request);
                return Emit(true, "flow.completed", 0, probe.RowCount);
            }
            catch (Exception error)
            {
                var report = DeveloperToolResult.Failure(command, "flow." + probe.Stage + ".failed", probe.Stage, error);
                foreach (var diagnostic in probe.Diagnostics) { ((JArray)report["diagnostics"]).Add(diagnostic.DeepClone()); }
                return Write(report);
            }
        }

        private static int Emit(bool succeeded, string code, int exitCode, int rowCount)
        {
            Console.WriteLine(Report(succeeded, code, exitCode, rowCount).ToString(Formatting.None));
            return exitCode;
        }

        private static int Write(JObject report)
        {
            Console.WriteLine(report.ToString(Formatting.None));
            return (int)report["exitCode"];
        }

        private static JObject Report(bool succeeded, string code, int exitCode, int rowCount) => new JObject
        {
            ["schemaVersion"] = "1.0",
            ["command"] = command,
            ["succeeded"] = succeeded,
            ["code"] = code,
            ["exitCode"] = exitCode,
            ["rowCount"] = rowCount,
            ["executionMode"] = "trusted-developer",
            ["databaseConnections"] = false
        };
    }
}