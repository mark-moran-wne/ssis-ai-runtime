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
    public enum TaskStringCollectionEditMode
    {
        Replace,
        Add,
        Remove
    }

    public sealed class TaskHostPropertyInventory
    {
        internal TaskHostPropertyInventory(string taskId, string taskName, string creationName,
            IEnumerable<TaskHostPropertyDescriptor> properties)
        {
            TaskId = taskId;
            TaskName = taskName;
            CreationName = creationName;
            Properties = properties.ToArray();
        }

        public string TaskId { get; }
        public string TaskName { get; }
        public string CreationName { get; }
        public IReadOnlyList<TaskHostPropertyDescriptor> Properties { get; }
    }

    public sealed class TaskHostPropertyDescriptor
    {
        internal TaskHostPropertyDescriptor(string name, string type, string typeConverter, bool canRead,
            bool canWrite, string valueKind, string safeValue, bool valueRedacted,
            int? collectionCount, string[] collectionItems, string[] enumValues, string specializedOperation)
        {
            Name = name;
            Type = type;
            TypeConverter = typeConverter;
            CanRead = canRead;
            CanWrite = canWrite;
            ValueKind = valueKind;
            SafeValue = safeValue;
            ValueRedacted = valueRedacted;
            CollectionCount = collectionCount;
            CollectionItems = collectionItems;
            EnumValues = enumValues;
            SpecializedOperation = specializedOperation;
        }

        public string Name { get; }
        public string Type { get; }
        public string TypeConverter { get; }
        public bool CanRead { get; }
        public bool CanWrite { get; }
        public string ValueKind { get; }
        public string SafeValue { get; }
        public bool ValueRedacted { get; }
        public int? CollectionCount { get; }
        public IReadOnlyList<string> CollectionItems { get; }
        public IReadOnlyList<string> EnumValues { get; }
        public string SpecializedOperation { get; }
    }

    public sealed class TaskHostPropertyMutationPlan : IPackageMutationPlan
    {
        private readonly string packageId;
        private readonly string packageName;
        private readonly string taskId;
        private readonly string taskName;
        private readonly string creationName;
        private readonly string propertyName;
        private readonly TypeCode propertyType;
        private readonly string otherPropertiesHash;
        private readonly object originalScalarValue;
        private readonly object resultingScalarValue;
        private readonly string[] originalCollection;
        private readonly string[] requestedCollection;
        private readonly string[] resultingCollection;
        private readonly string[] changedFlagNames;
        private readonly bool[] originalFlagValues;
        private readonly bool[] resultingFlagValues;
        private readonly TaskStringCollectionEditMode collectionMode;
        private readonly bool isCollection;
        private readonly Type enumValueType;

        private TaskHostPropertyMutationPlan(string sourceArtifactHash, TaskHost task, string propertyName,
            TypeCode propertyType, string otherPropertiesHash, object originalScalarValue,
            object resultingScalarValue, string[] originalCollection, string[] requestedCollection,
            string[] resultingCollection, TaskStringCollectionEditMode collectionMode, bool isCollection,
            string[] changedFlagNames = null, bool[] originalFlagValues = null, bool[] resultingFlagValues = null,
            Type enumValueType = null)
        {
            SourceArtifactHash = sourceArtifactHash;
            var package = FindOwningPackage(task);
            packageId = package.ID;
            packageName = package.Name;
            taskId = task.ID;
            taskName = task.Name;
            creationName = task.CreationName;
            this.propertyName = propertyName;
            this.propertyType = propertyType;
            this.otherPropertiesHash = otherPropertiesHash;
            this.originalScalarValue = originalScalarValue;
            this.resultingScalarValue = resultingScalarValue;
            this.originalCollection = originalCollection ?? Array.Empty<string>();
            this.requestedCollection = requestedCollection ?? Array.Empty<string>();
            this.resultingCollection = resultingCollection ?? Array.Empty<string>();
            this.changedFlagNames = changedFlagNames ?? Array.Empty<string>();
            this.originalFlagValues = originalFlagValues ?? Array.Empty<bool>();
            this.resultingFlagValues = resultingFlagValues ?? Array.Empty<bool>();
            this.collectionMode = collectionMode;
            this.isCollection = isCollection;
            this.enumValueType = enumValueType;
            Requirements = MutationExecutionPolicies.TaskPropertyUpdate(isCollection);
        }

        public string SourceArtifactHash { get; }
        public string OperationName => isCollection ? "update_task_collection" : "set_task_property";
        public MutationExecutionRequirements Requirements { get; }
        public string TaskName => taskName;
        public string PropertyName => propertyName;
        public string PropertyType => propertyType.ToString();
        public bool IsCollection => isCollection;
        public TaskStringCollectionEditMode CollectionMode => collectionMode;
        public IReadOnlyList<string> OriginalCollection => Array.AsReadOnly((string[])originalCollection.Clone());
        public IReadOnlyList<string> RequestedCollection => Array.AsReadOnly((string[])requestedCollection.Clone());
        public IReadOnlyList<string> ResultingCollection => Array.AsReadOnly((string[])resultingCollection.Clone());
        public bool ValuesOmitted => !isCollection && (propertyType == TypeCode.String || IsSensitive(propertyName));
        public string SafeOriginalValue => ValuesOmitted ? string.Empty : FormatPreviewValue(originalScalarValue, enumValueType);
        public string SafeResultingValue => ValuesOmitted ? string.Empty : FormatPreviewValue(resultingScalarValue, enumValueType);

        public static TaskHostPropertyInventory Inspect(Package package, string taskNativeId)
        {
            var task = ResolveTask(package, taskNativeId);
            var properties = task.Properties.Cast<DtsProperty>().Select(property => DescribeProperty(task, property)).ToArray();
            return new TaskHostPropertyInventory(task.ID, task.Name, task.CreationName, properties);
        }

        public static bool IsTransferTask(Package package, string taskNativeId) =>
            ResolveTask(package, taskNativeId).InnerObject is ITransferSqlServerObjectsTask;

        public static TypeCode GetPropertyTypeCode(Package package, string taskNativeId, string propertyName)
        {
            var task = ResolveTask(package, taskNativeId);
            return FindProperty(task, propertyName).Type;
        }

        public static string[] GetPropertyEnumValues(Package package, string taskNativeId, string propertyName)
        {
            var task = ResolveTask(package, taskNativeId);
            var enumProperty = FindRuntimeEnumProperty(task, propertyName);
            return enumProperty == null ? Array.Empty<string>() : Enum.GetNames(enumProperty.PropertyType);
        }

        public static object ParsePropertyEnumValue(Package package, string taskNativeId, string propertyName,
            string requestedValue)
        {
            var task = ResolveTask(package, taskNativeId);
            var property = FindRuntimeEnumProperty(task, propertyName);
            var enumType = property?.PropertyType;
            if (enumType == null || string.IsNullOrWhiteSpace(requestedValue))
            { throw new InvalidOperationException("mutation.task_property.enum_type_invalid"); }
            try
            {
                var parsed = Enum.Parse(enumType, requestedValue, true);
                if (!Enum.IsDefined(enumType, parsed)) { throw new ArgumentException(); }
                return parsed;
            }
            catch (Exception) { throw new InvalidOperationException("mutation.task_property.enum_value_invalid"); }
        }

        public static TaskHostPropertyMutationPlan CreateScalar(Package package, string sourceArtifactHash,
            string taskNativeId, string propertyName, object resultingValue)
        {
            ValidateSource(package, sourceArtifactHash, taskNativeId, propertyName);
            var task = ResolveTask(package, taskNativeId);
            var property = FindProperty(task, propertyName);
            if (!property.Get || !property.Set || !IsSupportedScalar(property.Type))
            { throw new InvalidOperationException("mutation.task_property.not_supported"); }
            object originalValue;
            try { originalValue = property.GetValue(task); }
            catch (Exception) { throw new InvalidOperationException("mutation.task_property.read_failed"); }
            if (!ValueMatches(property.Type, originalValue))
            { throw new InvalidOperationException("mutation.task_property.value_unavailable"); }

            var enumProperty = FindRuntimeEnumProperty(task, propertyName);
            if (enumProperty != null)
            {
                if (resultingValue == null || resultingValue.GetType() != enumProperty.PropertyType)
                { throw new InvalidOperationException("mutation.task_property.enum_value_invalid"); }
                resultingValue = Convert.ChangeType(resultingValue, Enum.GetUnderlyingType(enumProperty.PropertyType), CultureInfo.InvariantCulture);
            }
            if (!ValueMatches(property.Type, resultingValue))
            { throw new InvalidOperationException("mutation.task_property.value_type_invalid"); }
            if (ValuesEqual(originalValue, resultingValue))
            { throw new InvalidOperationException("mutation.task_property.no_change"); }

            return new TaskHostPropertyMutationPlan(sourceArtifactHash, task, propertyName, property.Type,
                OtherPropertiesHash(task, new[] { propertyName }), originalValue, resultingValue, null, null, null,
                TaskStringCollectionEditMode.Replace, false, enumValueType: enumProperty?.PropertyType);
        }

        public static TaskHostPropertyMutationPlan CreateStringCollection(Package package, string sourceArtifactHash,
            string taskNativeId, string propertyName, TaskStringCollectionEditMode mode, IEnumerable<string> values)
        {
            ValidateSource(package, sourceArtifactHash, taskNativeId, propertyName);
            if (!Enum.IsDefined(typeof(TaskStringCollectionEditMode), mode)) { throw new ArgumentOutOfRangeException(nameof(mode)); }
            var task = ResolveTask(package, taskNativeId);
            var property = FindProperty(task, propertyName);
            if (!property.Get || !property.Set || property.Type != TypeCode.Object)
            { throw new InvalidOperationException("mutation.task_collection.not_supported"); }
            if (IsSensitive(propertyName)) { throw new InvalidOperationException("mutation.task_collection.sensitive_property_unsupported"); }
            object raw;
            try { raw = property.GetValue(task); }
            catch (Exception) { throw new InvalidOperationException("mutation.task_collection.read_failed"); }
            if (!(raw is StringCollection collection)) { throw new InvalidOperationException("mutation.task_collection.type_unsupported"); }

            var requested = NormalizeCollection(values, allowEmpty: mode == TaskStringCollectionEditMode.Replace);
            var original = collection.Cast<string>().ToArray();
            EnsureUnique(original, "mutation.task_collection.current_duplicate");
            var changedFlagNames = new List<string>();
            var originalFlagValues = new List<bool>();
            var resultingFlagValues = new List<bool>();
            if (task.InnerObject is ITransferSqlServerObjectsTask)
            {
                if (!property.Name.EndsWith("List", StringComparison.OrdinalIgnoreCase))
                { throw new InvalidOperationException("mutation.task_collection.transfer_property_unsupported"); }
                var catchAllNames = new[] { "CopyAllObjects", "CopyAll" + property.Name.Substring(0, property.Name.Length - 4) };
                foreach (var flagName in catchAllNames.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var flag = FindProperty(task, flagName);
                    if (!flag.Get || flag.Type != TypeCode.Boolean)
                    { throw new InvalidOperationException("mutation.task_collection.copy_all_flag_unavailable"); }
                    var enabled = (bool)flag.GetValue(task);
                    if (mode != TaskStringCollectionEditMode.Replace && enabled)
                    { throw new InvalidOperationException("mutation.task_collection.copy_all_enabled_use_replace"); }
                    if (mode == TaskStringCollectionEditMode.Replace && enabled)
                    {
                        if (!flag.Set) { throw new InvalidOperationException("mutation.task_collection.copy_all_flag_read_only"); }
                        changedFlagNames.Add(flagName);
                        originalFlagValues.Add(true);
                        resultingFlagValues.Add(false);
                    }
                }
            }
            var resulting = mode switch
            {
                TaskStringCollectionEditMode.Replace => requested,
                TaskStringCollectionEditMode.Add => original.Concat(requested.Where(value => !Contains(original, value))).ToArray(),
                TaskStringCollectionEditMode.Remove => original.Where(value => !Contains(requested, value)).ToArray(),
                _ => throw new InvalidOperationException("mutation.task_collection.mode_invalid")
            };
            if (original.SequenceEqual(resulting, StringComparer.Ordinal))
            { throw new InvalidOperationException("mutation.task_collection.no_change"); }

            var excludedSettings = new[] { propertyName }.Concat(changedFlagNames).ToArray();
            return new TaskHostPropertyMutationPlan(sourceArtifactHash, task, propertyName, property.Type,
                OtherPropertiesHash(task, excludedSettings), null, null, original, requested, resulting, mode, true,
                changedFlagNames.ToArray(), originalFlagValues.ToArray(), resultingFlagValues.ToArray());
        }

        public IPackageMutationPlan RebindCurrent(Package package, string sourceArtifactHash, string packagePath = null)
        {
            if (!string.Equals(SourceArtifactHash, sourceArtifactHash, StringComparison.Ordinal))
            { throw new InvalidOperationException("mutation.source.hash_changed"); }
            var task = ResolveTask(package, taskId);
            if (package.ID != packageId || package.Name != packageName || task.Name != taskName || task.CreationName != creationName)
            { throw new InvalidOperationException("mutation.task_property.target_changed"); }
            var current = isCollection
                ? CreateStringCollection(package, sourceArtifactHash, taskId, propertyName, collectionMode, requestedCollection)
                : CreateScalar(package, sourceArtifactHash, taskId, propertyName,
                    enumValueType == null ? resultingScalarValue : Enum.ToObject(enumValueType, resultingScalarValue));
            if (current.otherPropertiesHash != otherPropertiesHash ||
                (isCollection && !current.originalCollection.SequenceEqual(originalCollection, StringComparer.Ordinal)) ||
                (!isCollection && !ValuesEqual(current.originalScalarValue, originalScalarValue)) ||
                !current.changedFlagNames.SequenceEqual(changedFlagNames, StringComparer.OrdinalIgnoreCase) ||
                !current.originalFlagValues.SequenceEqual(originalFlagValues) ||
                !current.resultingFlagValues.SequenceEqual(resultingFlagValues))
            { throw new InvalidOperationException("mutation.task_property.current_state_changed"); }
            return current;
        }

        public void Apply(Package package)
        {
            var task = ResolveTask(package, taskId);
            if (package.ID != packageId || package.Name != packageName || task.Name != taskName || task.CreationName != creationName ||
                OtherPropertiesHash(task, new[] { propertyName }.Concat(changedFlagNames)) != otherPropertiesHash)
            { throw new InvalidOperationException("mutation.task_property.current_state_changed"); }
            var property = FindProperty(task, propertyName);
            if (isCollection)
            {
                var current = property.GetValue(task) as StringCollection;
                if (current == null || !current.Cast<string>().SequenceEqual(originalCollection, StringComparer.Ordinal))
                { throw new InvalidOperationException("mutation.task_collection.current_state_changed"); }
                for (var index = 0; index < changedFlagNames.Length; index++)
                {
                    var flag = FindProperty(task, changedFlagNames[index]);
                    if (!Equals(flag.GetValue(task), originalFlagValues[index]))
                    { throw new InvalidOperationException("mutation.task_collection.copy_all_flag_changed"); }
                }
                var next = new StringCollection();
                foreach (var value in resultingCollection) { next.Add(value); }
                for (var index = 0; index < changedFlagNames.Length; index++)
                { FindProperty(task, changedFlagNames[index]).SetValue(task, resultingFlagValues[index]); }
                property.SetValue(task, next);
            }
            else
            {
                var current = property.GetValue(task);
                if (!ValuesEqual(current, originalScalarValue))
                { throw new InvalidOperationException("mutation.task_property.current_state_changed"); }
                property.SetValue(task, resultingScalarValue);
            }
        }

        public string VerifyFailureCode(Package package)
        {
            try
            {
                var task = ResolveTask(package, taskId);
                if (package.ID != packageId || package.Name != packageName || task.Name != taskName || task.CreationName != creationName)
                { return "mutation.task_property.target_changed"; }
                if (OtherPropertiesHash(task, new[] { propertyName }.Concat(changedFlagNames)) != otherPropertiesHash)
                { return "mutation.task_property.unrequested_change"; }
                var property = FindProperty(task, propertyName);
                if (isCollection)
                {
                    var current = property.GetValue(task) as StringCollection;
                    if (current == null || !current.Cast<string>().SequenceEqual(resultingCollection, StringComparer.Ordinal))
                    { return "mutation.task_collection.result_mismatch"; }
                    for (var index = 0; index < changedFlagNames.Length; index++)
                    {
                        if (!Equals(FindProperty(task, changedFlagNames[index]).GetValue(task), resultingFlagValues[index]))
                        { return "mutation.task_collection.copy_all_flag_mismatch"; }
                    }
                    return string.Empty;
                }
                return ValuesEqual(property.GetValue(task), resultingScalarValue)
                    ? string.Empty : "mutation.task_property.result_mismatch";
            }
            catch (Exception) { return "mutation.task_property.verification_failed"; }
        }

        public string SerializePreviewContract()
        {
            var contract = new TaskHostPropertyPreviewContract(SourceArtifactHash, taskName, propertyName,
                propertyType.ToString(), isCollection ? collectionMode.ToString() : "Set", isCollection,
                ValuesOmitted, SafeOriginalValue, SafeResultingValue, originalCollection, requestedCollection,
                resultingCollection, changedFlagNames, originalFlagValues, resultingFlagValues, Requirements.Requirements);
            var serializer = new DataContractJsonSerializer(typeof(TaskHostPropertyPreviewContract));
            using (var stream = new System.IO.MemoryStream())
            {
                serializer.WriteObject(stream, contract);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static TaskHostPropertyDescriptor DescribeProperty(TaskHost task, DtsProperty property)
        {
            object value = null;
            string valueKind = "Unavailable";
            string safeValue = string.Empty;
            var valueRedacted = false;
            int? collectionCount = null;
            string[] collectionItems = null;
            var canRead = property.Get;
            if (canRead)
            {
                try
                {
                    value = property.GetValue(task);
                    if (value is StringCollection collection)
                    {
                        valueKind = "StringCollection";
                        collectionCount = collection.Count;
                        if (!IsSensitive(property.Name)) { collectionItems = collection.Cast<string>().ToArray(); }
                        else { valueRedacted = true; }
                    }
                    else if (IsSafeScalar(value))
                    {
                        valueKind = value.GetType().Name;
                        if (!(value is string) && !IsSensitive(property.Name)) { safeValue = FormatValue(value); }
                        else { valueRedacted = true; }
                    }
                    else
                    {
                        valueKind = value == null ? "Null" : "Object";
                        valueRedacted = value != null;
                    }
                }
                catch (Exception) { valueKind = "ReadFailed"; valueRedacted = true; }
            }
            var specialized = task.InnerObject is Microsoft.SqlServer.Dts.Tasks.TransferSqlServerObjectsTask.ITransferSqlServerObjectsTask &&
                property.Name == "TablesList" ? "update_transfer_tables" : string.Empty;
            var runtimeEnumProperty = FindRuntimeEnumProperty(task, property.Name);
            var enumValues = runtimeEnumProperty == null ? Array.Empty<string>() : Enum.GetNames(runtimeEnumProperty.PropertyType);
            if (runtimeEnumProperty != null)
            {
                valueKind = "Enum";
                try
                {
                    var enumValue = runtimeEnumProperty.GetValue(task.InnerObject, null);
                    if (!IsSensitive(property.Name)) { safeValue = enumValue?.ToString() ?? string.Empty; }
                    else { valueRedacted = true; }
                }
                catch (Exception) { valueKind = "ReadFailed"; valueRedacted = true; }
            }
            return new TaskHostPropertyDescriptor(property.Name, property.Type.ToString(), property.TypeConverter,
                property.Get, property.Set, valueKind, safeValue, valueRedacted, collectionCount,
                collectionItems ?? Array.Empty<string>(), enumValues, specialized);
        }

        private static TaskHost ResolveTask(Package package, string taskId)
        {
            if (package == null) { throw new ArgumentNullException(nameof(package)); }
            var matches = FindTaskHosts(package).Where(task => task.ID == taskId).ToArray();
            if (matches.Length != 1) { throw new InvalidOperationException("mutation.task_property.task_not_found_or_ambiguous"); }
            return matches[0];
        }

        private static Package FindOwningPackage(DtsContainer container)
        {
            var current = container;
            while (current.Parent != null)
            {
                if (current.Parent is Package package) { return package; }
                current = current.Parent;
            }
            throw new InvalidOperationException("mutation.task_property.package_owner_missing");
        }

        private static DtsProperty FindProperty(TaskHost task, string propertyName)
        {
            var matches = task.Properties.Cast<DtsProperty>()
                .Where(property => string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1) { throw new InvalidOperationException("mutation.task_property.property_not_found_or_ambiguous"); }
            return matches[0];
        }

        private static PropertyInfo FindRuntimeEnumProperty(TaskHost task, string propertyName)
        {
            var matches = task.InnerObject.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.CanRead && property.PropertyType.IsEnum &&
                    string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase)).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }

        private static void ValidateSource(Package package, string sourceArtifactHash, string taskId, string propertyName)
        {
            if (package == null) { throw new ArgumentNullException(nameof(package)); }
            if (!MutationHash.IsSha256(sourceArtifactHash)) { throw new ArgumentException("A SHA-256 source hash is required.", nameof(sourceArtifactHash)); }
            if (string.IsNullOrWhiteSpace(taskId)) { throw new ArgumentException("A task native ID is required.", nameof(taskId)); }
            if (string.IsNullOrWhiteSpace(propertyName)) { throw new ArgumentException("A native property name is required.", nameof(propertyName)); }
        }

        private static string[] NormalizeCollection(IEnumerable<string> values, bool allowEmpty)
        {
            if (values == null) { throw new ArgumentNullException(nameof(values)); }
            var result = values.Select(value =>
            {
                var item = value?.Trim();
                if (string.IsNullOrWhiteSpace(item) || item.Length > 512 || item.Any(char.IsControl))
                { throw new ArgumentException("Collection values must be bounded and non-empty.", nameof(values)); }
                return item;
            }).ToArray();
            if (!allowEmpty && result.Length == 0) { throw new ArgumentException("At least one collection value is required.", nameof(values)); }
            EnsureUnique(result);
            return result;
        }

        private static void EnsureUnique(IEnumerable<string> values)
        {
            EnsureUnique(values, "mutation.task_collection.duplicate_value");
        }

        private static void EnsureUnique(IEnumerable<string> values, string diagnosticCode)
        {
            if (values.GroupBy(value => value, StringComparer.Ordinal).Any(group => group.Count() > 1))
            { throw new InvalidOperationException(diagnosticCode); }
        }

        private static bool Contains(IEnumerable<string> values, string value) => values.Contains(value, StringComparer.Ordinal);

        private static string OtherPropertiesHash(TaskHost task, IEnumerable<string> excludedProperties)
        {
            var excluded = new HashSet<string>(excludedProperties, StringComparer.OrdinalIgnoreCase);
            var parts = new List<string>();
            foreach (var property in task.Properties.Cast<DtsProperty>()
                .Where(property => property.Get && property.Set &&
                    !RuntimeOnlyProperties.Contains(property.Name) &&
                    !excluded.Contains(property.Name))
                .OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                object value;
                try { value = property.GetValue(task); }
                catch (Exception) { throw new InvalidOperationException("mutation.task_property.snapshot_failed"); }
                if (value is StringCollection collection)
                {
                    parts.Add(property.Name + "\u001fcollection\u001f" + string.Join("\u001e", collection.Cast<string>()));
                }
                else if (value == null || IsSafeScalar(value))
                {
                    parts.Add(property.Name + "\u001f" + property.Type + "\u001f" + FormatValue(value));
                }
                else
                {
                    parts.Add(property.Name + "\u001fobject\u001f" + value.GetType().FullName);
                }
            }
            using (var algorithm = SHA256.Create())
            {
                return string.Concat(algorithm.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\u001d", parts)))
                    .Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));
            }
        }

        private static IEnumerable<TaskHost> FindTaskHosts(Package package)
        {
            foreach (var task in FindTaskHosts(package.Executables)) { yield return task; }
            foreach (DtsEventHandler handler in package.EventHandlers)
            { foreach (var task in FindTaskHosts(handler.Executables)) { yield return task; } }
        }

        private static IEnumerable<TaskHost> FindTaskHosts(Executables executables)
        {
            foreach (Executable executable in executables)
            {
                if (executable is TaskHost task) { yield return task; }
                if (executable is IDTSSequence sequence)
                { foreach (var nested in FindTaskHosts(sequence.Executables)) { yield return nested; } }
                if (executable is EventsProvider provider)
                {
                    foreach (DtsEventHandler handler in provider.EventHandlers)
                    { foreach (var nested in FindTaskHosts(handler.Executables)) { yield return nested; } }
                }
            }
        }

        private static bool IsSupportedScalar(TypeCode type) => type == TypeCode.String || type == TypeCode.Boolean ||
            type == TypeCode.Byte || type == TypeCode.SByte || type == TypeCode.Int16 || type == TypeCode.UInt16 ||
            type == TypeCode.Int32 || type == TypeCode.UInt32 || type == TypeCode.Int64 || type == TypeCode.UInt64 ||
            type == TypeCode.Single || type == TypeCode.Double || type == TypeCode.Decimal || type == TypeCode.DateTime;

        private static bool ValueMatches(TypeCode type, object value)
        {
            if (value == null) { return false; }
            if (type == TypeCode.String) { return value is string; }
            if (type == TypeCode.Boolean) { return value is bool; }
            if (type == TypeCode.DateTime) { return value is DateTime; }
            var valueType = value.GetType();
            if (valueType.IsEnum) { valueType = Enum.GetUnderlyingType(valueType); }
            return Type.GetTypeCode(valueType) == type;
        }

        private static bool ValuesEqual(object first, object second) =>
            string.Equals(FormatValue(first), FormatValue(second), StringComparison.Ordinal);

        private static bool IsSafeScalar(object value) => value == null || value is string || value is bool || value is char ||
            value is byte || value is sbyte || value is short || value is ushort || value is int || value is uint ||
            value is long || value is ulong || value is float || value is double || value is decimal || value is DateTime || value.GetType().IsEnum;

        private static string FormatValue(object value)
        {
            if (value == null) { return "<null>"; }
            if (value is DateTime dateTime) { return dateTime.ToString("O", CultureInfo.InvariantCulture); }
            if (value is IFormattable formattable) { return formattable.ToString(null, CultureInfo.InvariantCulture); }
            return value.ToString();
        }

        private static string FormatPreviewValue(object value, Type enumType) =>
            enumType == null || value == null ? FormatValue(value) : Enum.GetName(enumType, Enum.ToObject(enumType, value)) ?? FormatValue(value);

        private static bool IsSensitive(string name) => new[]
        { "password", "secret", "credential", "connection", "sql", "query", "expression", "token", "key", "login", "user" }
            .Any(part => name.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0);

        private static readonly HashSet<string> RuntimeOnlyProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "IsSavingXml", "SuspendRequired" };
    }

    [DataContract]
    public sealed class TaskHostPropertyPreviewContract
    {
        internal TaskHostPropertyPreviewContract(string sourceArtifactHash, string taskName, string propertyName,
            string propertyType, string mode, bool isCollection, bool valuesOmitted, string originalValue,
            string resultingValue, IEnumerable<string> originalCollection, IEnumerable<string> requestedCollection,
            IEnumerable<string> resultingCollection, IEnumerable<string> changedFlagNames,
            IEnumerable<bool> originalFlagValues, IEnumerable<bool> resultingFlagValues, IEnumerable<string> requirements)
        {
            SchemaVersion = "2.0";
            SourceArtifactHash = sourceArtifactHash;
            TaskName = taskName;
            PropertyName = propertyName;
            PropertyType = propertyType;
            Mode = mode;
            IsCollection = isCollection;
            ValuesOmitted = valuesOmitted;
            OriginalValue = originalValue;
            ResultingValue = resultingValue;
            OriginalCollection = originalCollection.ToArray();
            RequestedCollection = requestedCollection.ToArray();
            ResultingCollection = resultingCollection.ToArray();
            var flagNames = changedFlagNames.ToArray();
            var oldValues = originalFlagValues.ToArray();
            var newValues = resultingFlagValues.ToArray();
            ChangedFlags = flagNames.Select((name, index) =>
                new TaskHostPropertyFlagChange(name, oldValues[index], newValues[index])).ToArray();
            Requirements = requirements.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        }

        [DataMember(Order = 0)] public string SchemaVersion { get; private set; }
        [DataMember(Order = 1)] public string Operation { get; private set; } = "TaskPropertyMutation";
        [DataMember(Order = 2)] public string SourceArtifactHash { get; private set; }
        [DataMember(Order = 3)] public string TaskName { get; private set; }
        [DataMember(Order = 4)] public string PropertyName { get; private set; }
        [DataMember(Order = 5)] public string PropertyType { get; private set; }
        [DataMember(Order = 6)] public string Mode { get; private set; }
        [DataMember(Order = 7)] public bool IsCollection { get; private set; }
        [DataMember(Order = 8)] public bool ValuesOmitted { get; private set; }
        [DataMember(Order = 9)] public string OriginalValue { get; private set; }
        [DataMember(Order = 10)] public string ResultingValue { get; private set; }
        [DataMember(Order = 11)] public string[] OriginalCollection { get; private set; }
        [DataMember(Order = 12)] public string[] RequestedCollection { get; private set; }
        [DataMember(Order = 13)] public string[] ResultingCollection { get; private set; }
        [DataMember(Order = 14)] public string[] Requirements { get; private set; }
        [DataMember(Order = 15)] public TaskHostPropertyFlagChange[] ChangedFlags { get; private set; }
    }

    [DataContract]
    public sealed class TaskHostPropertyFlagChange
    {
        internal TaskHostPropertyFlagChange(string name, bool before, bool after)
        { Name = name; Before = before; After = after; }

        [DataMember(Order = 0)] public string Name { get; private set; }
        [DataMember(Order = 1)] public bool Before { get; private set; }
        [DataMember(Order = 2)] public bool After { get; private set; }
    }
}
