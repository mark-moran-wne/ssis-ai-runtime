using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.SqlServer.Dts.Runtime;
using Microsoft.SqlServer.Dts.Tasks.TransferSqlServerObjectsTask;
using SsisAiRuntime.Mutations;

namespace SsisAiRuntime.MutationHost
{
    public enum TransferTableUpdateMode
    {
        Replace,
        Add,
        Remove
    }

    public sealed class TransferTablesMutationPlan : IPackageMutationPlan
    {
        private readonly string[] originalTables;
        private readonly string[] requestedTables;
        private readonly string[] resultingTables;
        private readonly string configurationHash;
        private readonly bool originalCopyAllObjects;
        private readonly bool originalCopyAllTables;
        private readonly bool resultingCopyAllObjects;
        private readonly bool resultingCopyAllTables;
        private readonly string packageId;
        private readonly string packageName;
        private readonly string taskId;
        private readonly string taskName;

        private TransferTablesMutationPlan(string sourceArtifactHash, string packageId, string packageName,
            string taskId, string taskName, TransferTableUpdateMode mode, string[] originalTables,
            string[] requestedTables, string[] resultingTables, string configurationHash,
            bool originalCopyAllObjects, bool originalCopyAllTables, bool resultingCopyAllObjects,
            bool resultingCopyAllTables)
        {
            SourceArtifactHash = sourceArtifactHash;
            this.packageId = packageId;
            this.packageName = packageName;
            this.taskId = taskId;
            this.taskName = taskName;
            Mode = mode;
            this.originalTables = originalTables;
            this.requestedTables = requestedTables;
            this.resultingTables = resultingTables;
            this.configurationHash = configurationHash;
            this.originalCopyAllObjects = originalCopyAllObjects;
            this.originalCopyAllTables = originalCopyAllTables;
            this.resultingCopyAllObjects = resultingCopyAllObjects;
            this.resultingCopyAllTables = resultingCopyAllTables;
            Requirements = MutationExecutionPolicies.TransferTablesUpdate();
        }

        public string SourceArtifactHash { get; }
        public string OperationName => "transfer_tables_update";
        public MutationExecutionRequirements Requirements { get; }
        public TransferTableUpdateMode Mode { get; }
        public string TaskName => taskName;
        public IReadOnlyList<string> OriginalTables => Array.AsReadOnly((string[])originalTables.Clone());
        public IReadOnlyList<string> RequestedTables => Array.AsReadOnly((string[])requestedTables.Clone());
        public IReadOnlyList<string> ResultingTables => Array.AsReadOnly((string[])resultingTables.Clone());
        public bool OriginalCopyAllObjects => originalCopyAllObjects;
        public bool OriginalCopyAllTables => originalCopyAllTables;
        public bool ResultingCopyAllObjects => resultingCopyAllObjects;
        public bool ResultingCopyAllTables => resultingCopyAllTables;

        public static TransferTablesMutationPlan Create(Package package, string sourceArtifactHash,
            string taskNativeId, TransferTableUpdateMode mode, IEnumerable<string> tableNames)
        {
            if (package == null) { throw new ArgumentNullException(nameof(package)); }
            if (!MutationHash.IsSha256(sourceArtifactHash)) { throw new ArgumentException("A SHA-256 source hash is required.", nameof(sourceArtifactHash)); }
            if (string.IsNullOrWhiteSpace(taskNativeId)) { throw new ArgumentException("A task native ID is required.", nameof(taskNativeId)); }
            if (!Enum.IsDefined(typeof(TransferTableUpdateMode), mode)) { throw new ArgumentOutOfRangeException(nameof(mode)); }

            var requested = NormalizeNames(tableNames, allowEmpty: mode == TransferTableUpdateMode.Replace);
            var match = FindTransferTask(package, taskNativeId);
            var transfer = match.Item2;
            var original = transfer.TablesList.Cast<string>().ToArray();
            EnsureUnique(original, "mutation.transfer.tables.current_duplicate");

            if (mode != TransferTableUpdateMode.Replace && (transfer.CopyAllObjects || transfer.CopyAllTables))
            { throw new InvalidOperationException("mutation.transfer.tables.copy_all_enabled_use_replace"); }

            var resulting = mode switch
            {
                TransferTableUpdateMode.Replace => requested,
                TransferTableUpdateMode.Add => original.Concat(requested.Where(name => !ContainsName(original, name))).ToArray(),
                TransferTableUpdateMode.Remove => original.Where(name => !ContainsName(requested, name)).ToArray(),
                _ => throw new InvalidOperationException("mutation.transfer.tables.mode_invalid")
            };
            var copyAllObjects = mode == TransferTableUpdateMode.Replace ? false : transfer.CopyAllObjects;
            var copyAllTables = mode == TransferTableUpdateMode.Replace ? false : transfer.CopyAllTables;
            if (original.SequenceEqual(resulting, StringComparer.Ordinal) &&
                transfer.CopyAllObjects == copyAllObjects && transfer.CopyAllTables == copyAllTables)
            { throw new InvalidOperationException("mutation.transfer.tables.no_change"); }

            return new TransferTablesMutationPlan(sourceArtifactHash, package.ID, package.Name, match.Item1.ID,
                match.Item1.Name, mode, original, requested, resulting, ConfigurationHash(transfer),
                transfer.CopyAllObjects, transfer.CopyAllTables, copyAllObjects, copyAllTables);
        }

        public IPackageMutationPlan RebindCurrent(Package package, string sourceArtifactHash, string packagePath = null)
        {
            if (!string.Equals(SourceArtifactHash, sourceArtifactHash, StringComparison.Ordinal))
            { throw new InvalidOperationException("mutation.source.hash_changed"); }
            if (package == null || package.ID != packageId || package.Name != packageName)
            { throw new InvalidOperationException("mutation.transfer.package_identity_changed"); }
            var current = Create(package, sourceArtifactHash, taskId, Mode, requestedTables);
            if (current.taskName != taskName ||
                !current.originalTables.SequenceEqual(originalTables, StringComparer.Ordinal) ||
                current.configurationHash != configurationHash ||
                current.originalCopyAllObjects != originalCopyAllObjects ||
                current.originalCopyAllTables != originalCopyAllTables)
            { throw new InvalidOperationException("mutation.transfer.task_changed"); }
            return current;
        }

        public void Apply(Package package)
        {
            var match = FindTransferTask(package, taskId);
            var transfer = match.Item2;
            if (package.ID != packageId || package.Name != packageName || match.Item1.Name != taskName ||
                ConfigurationHash(transfer) != configurationHash ||
                transfer.CopyAllObjects != originalCopyAllObjects || transfer.CopyAllTables != originalCopyAllTables ||
                !transfer.TablesList.Cast<string>().SequenceEqual(originalTables, StringComparer.Ordinal))
            { throw new InvalidOperationException("mutation.transfer.task_changed"); }

            transfer.CopyAllObjects = resultingCopyAllObjects;
            transfer.CopyAllTables = resultingCopyAllTables;
            transfer.TablesList.Clear();
            foreach (var table in resultingTables) { transfer.TablesList.Add(table); }
        }

        public string VerifyFailureCode(Package package)
        {
            if (package == null) { return "mutation.transfer.package_missing"; }
            try
            {
                if (package.ID != packageId || package.Name != packageName)
                { return "mutation.transfer.package_identity_changed"; }
                var match = FindTransferTask(package, taskId);
                var transfer = match.Item2;
                if (match.Item1.Name != taskName || ConfigurationHash(transfer) != configurationHash)
                { return "mutation.transfer.unrequested_setting_changed"; }
                if (transfer.CopyAllObjects != resultingCopyAllObjects || transfer.CopyAllTables != resultingCopyAllTables)
                { return "mutation.transfer.copy_all_mismatch"; }
                if (!transfer.TablesList.Cast<string>().SequenceEqual(resultingTables, StringComparer.Ordinal))
                { return "mutation.transfer.tables_mismatch"; }
                return string.Empty;
            }
            catch (InvalidOperationException exception)
            {
                return SafeFailureCode(exception.Message);
            }
            catch (Exception)
            {
                return "mutation.transfer.verification_failed";
            }
        }

        public string SerializePreviewContract()
        {
            var contract = new TransferTablesPreviewContract(SourceArtifactHash, taskName, Mode.ToString(),
                originalTables, requestedTables, resultingTables, originalCopyAllObjects, originalCopyAllTables,
                resultingCopyAllObjects, resultingCopyAllTables, Requirements.Requirements);
            var serializer = new DataContractJsonSerializer(typeof(TransferTablesPreviewContract));
            using (var stream = new System.IO.MemoryStream())
            {
                serializer.WriteObject(stream, contract);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static Tuple<TaskHost, ITransferSqlServerObjectsTask> FindTransferTask(Package package, string taskId)
        {
            var matches = FindTaskHosts(package).Where(task => task.ID == taskId).ToArray();
            if (matches.Length != 1) { throw new InvalidOperationException("mutation.transfer.task_not_found_or_ambiguous"); }
            if (!(matches[0].InnerObject is ITransferSqlServerObjectsTask transfer))
            { throw new InvalidOperationException("mutation.transfer.task_type_unsupported"); }
            return Tuple.Create(matches[0], transfer);
        }

        private static IEnumerable<TaskHost> FindTaskHosts(Package package)
        {
            foreach (var task in FindTaskHosts(package.Executables)) { yield return task; }
            foreach (DtsEventHandler handler in package.EventHandlers)
            {
                foreach (var task in FindTaskHosts(handler.Executables)) { yield return task; }
            }
        }

        private static IEnumerable<TaskHost> FindTaskHosts(Executables executables)
        {
            foreach (var executable in executables)
            {
                if (executable is TaskHost task) { yield return task; }
                if (executable is IDTSSequence sequence)
                {
                    foreach (var nested in FindTaskHosts(sequence.Executables)) { yield return nested; }
                }
                if (executable is EventsProvider provider)
                {
                    foreach (DtsEventHandler handler in provider.EventHandlers)
                    {
                        foreach (var nested in FindTaskHosts(handler.Executables)) { yield return nested; }
                    }
                }
            }
        }

        private static string[] NormalizeNames(IEnumerable<string> names, bool allowEmpty)
        {
            if (names == null) { throw new ArgumentNullException(nameof(names)); }
            var values = names.Select(name =>
            {
                var normalized = name?.Trim();
                if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 512 || normalized.Any(char.IsControl))
                { throw new ArgumentException("A bounded, non-empty table name is required.", nameof(names)); }
                return normalized;
            }).ToArray();
            if (!allowEmpty && values.Length == 0) { throw new ArgumentException("At least one table name is required.", nameof(names)); }
            EnsureUnique(values, "mutation.transfer.tables.request_duplicate");
            return values;
        }

        private static void EnsureUnique(IEnumerable<string> names, string diagnosticCode)
        {
            if (names.GroupBy(name => name, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            { throw new InvalidOperationException(diagnosticCode); }
        }

        private static bool ContainsName(IEnumerable<string> names, string value) =>
            names.Any(name => string.Equals(name, value, StringComparison.OrdinalIgnoreCase));

        private static string ConfigurationHash(ITransferSqlServerObjectsTask transfer)
        {
            var text = new StringBuilder();
            foreach (var property in typeof(ITransferSqlServerObjectsTask).GetProperties()
                .Where(property => property.Name != nameof(ITransferSqlServerObjectsTask.TablesList) &&
                    property.Name != nameof(ITransferSqlServerObjectsTask.CopyAllObjects) &&
                    property.Name != nameof(ITransferSqlServerObjectsTask.CopyAllTables))
                .OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                var value = property.GetValue(transfer, null);
                var normalized = value is StringCollection collection
                    ? string.Join("\u001e", collection.Cast<string>())
                    : Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
                text.Append(property.Name).Append('\u001f').Append(normalized).Append('\u001e');
            }
            using (var algorithm = SHA256.Create())
            {
                return string.Concat(algorithm.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))
                    .Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));
            }
        }

        private static string SafeFailureCode(string value) =>
            value != null && value.StartsWith("mutation.transfer.", StringComparison.Ordinal) &&
            value.All(character => char.IsLetterOrDigit(character) || character == '.' || character == '_')
                ? value : "mutation.transfer.verification_failed";
    }

    [DataContract]
    public sealed class TransferTablesPreviewContract
    {
        internal TransferTablesPreviewContract(string sourceArtifactHash, string taskName, string mode,
            IEnumerable<string> originalTables, IEnumerable<string> requestedTables, IEnumerable<string> resultingTables,
            bool originalCopyAllObjects, bool originalCopyAllTables, bool resultingCopyAllObjects,
            bool resultingCopyAllTables, IEnumerable<string> requirements)
        {
            SchemaVersion = "1.0";
            SourceArtifactHash = sourceArtifactHash;
            TaskName = taskName;
            Mode = mode;
            OriginalTables = originalTables.ToArray();
            RequestedTables = requestedTables.ToArray();
            ResultingTables = resultingTables.ToArray();
            OriginalCopyAllObjects = originalCopyAllObjects;
            OriginalCopyAllTables = originalCopyAllTables;
            ResultingCopyAllObjects = resultingCopyAllObjects;
            ResultingCopyAllTables = resultingCopyAllTables;
            Requirements = requirements.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        }

        [DataMember(Order = 0)] public string SchemaVersion { get; private set; }
        [DataMember(Order = 1)] public string Operation { get; private set; } = "UpdateTransferTables";
        [DataMember(Order = 2)] public string SourceArtifactHash { get; private set; }
        [DataMember(Order = 3)] public string TaskName { get; private set; }
        [DataMember(Order = 4)] public string Mode { get; private set; }
        [DataMember(Order = 5)] public string[] OriginalTables { get; private set; }
        [DataMember(Order = 6)] public string[] RequestedTables { get; private set; }
        [DataMember(Order = 7)] public string[] ResultingTables { get; private set; }
        [DataMember(Order = 8)] public bool OriginalCopyAllObjects { get; private set; }
        [DataMember(Order = 9)] public bool OriginalCopyAllTables { get; private set; }
        [DataMember(Order = 10)] public bool ResultingCopyAllObjects { get; private set; }
        [DataMember(Order = 11)] public bool ResultingCopyAllTables { get; private set; }
        [DataMember(Order = 12)] public string[] Requirements { get; private set; }
    }
}
