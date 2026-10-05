using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SsisAiRuntime.Inspectors.SqlDependencies
{
    public enum SqlDependencyEvidence { ParsedSchemaObject, ParsedExecuteTarget, ParsedFunctionReference, DynamicSql, Unsupported }
    public enum SqlDependencyAccess { Read, Write, Execute, Function }
    public enum SqlDialect { TSql160, Unsupported }

    public static class SqlDependencyCoverage
    {
        public static bool IsSupported(string code) => code == "sql.parse_failed" || code == "sql.dynamic_sql" ||
            code == "sql.dialect_unsupported" || code == "sql.text_unavailable" || code == "sql.length_exceeded" ||
            code == "sql.context_changed" || code == "sql.unsupported_construct" || code == "sql.reference_unresolved" ||
            code == "sql.connection_unresolved";
        public static UnsupportedItem Gap(string ownerId, string code) => new UnsupportedItem(ownerId, "SqlDependency", "SqlDependency",
            "SQL dependency coverage is incomplete. Details are omitted.", code);
    }

    public sealed class SqlObjectReference
    {
        public SqlObjectReference(IEnumerable<string> identifiers, SqlDependencyAccess access, SqlDependencyEvidence evidence)
        {
            var parts = (identifiers ?? throw new ArgumentNullException(nameof(identifiers))).ToList();
            if (parts.Count < 1 || parts.Count > 4 || string.IsNullOrWhiteSpace(parts.Last()))
            { throw new ArgumentException("An object name is required.", nameof(identifiers)); }
            Identifiers = new ReadOnlyCollection<string>(parts);
            Access = access; Evidence = evidence;
        }
        public IReadOnlyList<string> Identifiers { get; }
        public string ObjectName => Identifiers.Last();
        public string QualifiedName => string.Join(".", Identifiers.Select(part => "[" + part.Replace("]", "]]") + "]"));
        public SqlDependencyAccess Access { get; }
        public SqlDependencyEvidence Evidence { get; }
    }

    public sealed class SqlDependencyResolution
    {
        public SqlDependencyResolution(IEnumerable<SqlObjectReference> references, IEnumerable<string> coverageCodes, bool parsed)
        {
            if (references == null) { throw new ArgumentNullException(nameof(references)); }
            if (coverageCodes == null) { throw new ArgumentNullException(nameof(coverageCodes)); }
            var codes = coverageCodes.ToArray();
            if (codes.Any(code => !SqlDependencyCoverage.IsSupported(code))) { throw new ArgumentException("A fixed SQL coverage code is required.", nameof(coverageCodes)); }
            Parsed = parsed;
            References = new ReadOnlyCollection<SqlObjectReference>((parsed ? references : Array.Empty<SqlObjectReference>()).ToList());
            CoverageCodes = new ReadOnlyCollection<string>(codes.Distinct(StringComparer.Ordinal).OrderBy(code => code, StringComparer.Ordinal).ToList());
        }
        public bool Parsed { get; }
        public bool IsComplete => Parsed && CoverageCodes.Count == 0;
        public IReadOnlyList<SqlObjectReference> References { get; }
        public IReadOnlyList<string> CoverageCodes { get; }
        public SqlDialect Dialect => SqlDialect.TSql160;
    }
}