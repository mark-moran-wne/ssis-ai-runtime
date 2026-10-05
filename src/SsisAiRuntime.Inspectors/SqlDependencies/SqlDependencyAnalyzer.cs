using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SsisAiRuntime.Inspectors.SqlDependencies
{
    public sealed class SqlDependencyAnalyzer
    {
        public const int MaximumSqlLength = 65536;
        public SqlDependencyResolution Analyze(string sql, SqlDialect dialect = SqlDialect.TSql160)
        {
            if (sql == null) { throw new ArgumentNullException(nameof(sql)); }
            if (dialect != SqlDialect.TSql160) { return Failed("sql.dialect_unsupported"); }
            if (string.IsNullOrWhiteSpace(sql)) { return Failed("sql.text_unavailable"); }
            if (sql.Length > MaximumSqlLength) { return Failed("sql.length_exceeded"); }
            var fragment = new SqlScriptParser().Parse(sql);
            if (fragment == null) { return Failed("sql.parse_failed"); }
            var visitor = new ReferenceVisitor();
            fragment.Accept(visitor);
            return new SqlDependencyResolution(visitor.References, visitor.Gaps, true);
        }
        private static SqlDependencyResolution Failed(string code) => new SqlDependencyResolution(Array.Empty<SqlObjectReference>(), new[] { code }, false);

        private sealed class ReferenceVisitor : TSqlFragmentVisitor
        {
            internal readonly List<SqlObjectReference> References = new List<SqlObjectReference>();
            internal readonly List<string> Gaps = new List<string>();
            private readonly Stack<HashSet<string>> cteScopes = new Stack<HashSet<string>>();
            private readonly HashSet<TSqlFragment> writeTargets = new HashSet<TSqlFragment>();
            private readonly HashSet<TSqlFragment> aliasTargets = new HashSet<TSqlFragment>();
            private bool contextChanged;

            public override void ExplicitVisit(SelectStatement node) => WithCtes(node.WithCtesAndXmlNamespaces, () =>
            {
                if (node.Into != null) { Add(node.Into.Identifiers.Select(part => part.Value), SqlDependencyAccess.Write, SqlDependencyEvidence.ParsedSchemaObject); }
                node.AcceptChildren(this);
            });
            public override void ExplicitVisit(InsertStatement node) => WithCtes(node.WithCtesAndXmlNamespaces, () => node.AcceptChildren(this));
            public override void ExplicitVisit(UpdateStatement node) => WithCtes(node.WithCtesAndXmlNamespaces, () => node.AcceptChildren(this));
            public override void ExplicitVisit(DeleteStatement node) => WithCtes(node.WithCtesAndXmlNamespaces, () => node.AcceptChildren(this));
            public override void ExplicitVisit(MergeStatement node) => WithCtes(node.WithCtesAndXmlNamespaces, () => node.AcceptChildren(this));

            private void WithCtes(WithCtesAndXmlNamespaces clause, Action visit)
            {
                cteScopes.Push(new HashSet<string>(clause == null ? Enumerable.Empty<string>() : clause.CommonTableExpressions.Select(cte => cte.ExpressionName.Value), StringComparer.Ordinal));
                try { visit(); } finally { cteScopes.Pop(); }
            }

            public override void ExplicitVisit(InsertSpecification node) { writeTargets.Add(node.Target); node.AcceptChildren(this); }
            public override void ExplicitVisit(MergeSpecification node) { writeTargets.Add(node.Target); node.AcceptChildren(this); }
            public override void ExplicitVisit(UpdateSpecification node) { MarkWrite(node.Target, node.FromClause); node.AcceptChildren(this); }
            public override void ExplicitVisit(DeleteSpecification node) { MarkWrite(node.Target, node.FromClause); node.AcceptChildren(this); }

            private void MarkWrite(TableReference target, FromClause from)
            {
                writeTargets.Add(target);
                if (!(target is NamedTableReference named) || named.SchemaObject.Identifiers.Count != 1 || from == null) { return; }
                var aliases = new AliasVisitor(); from.Accept(aliases);
                var matches = aliases.Tables.Where(table => table.Alias != null && string.Equals(table.Alias.Value, named.SchemaObject.BaseIdentifier.Value, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matches.Length == 0)
                {
                    if (aliases.DerivedAliases.Contains(named.SchemaObject.BaseIdentifier.Value)) { aliasTargets.Add(target); Gaps.Add("sql.reference_unresolved"); }
                    return;
                }
                aliasTargets.Add(target);
                if (matches.Length == 1 && matches[0].Alias.Value == named.SchemaObject.BaseIdentifier.Value)
                { Add(matches[0].SchemaObject.Identifiers.Select(part => part.Value), SqlDependencyAccess.Write, SqlDependencyEvidence.ParsedSchemaObject); }
                else { Gaps.Add("sql.reference_unresolved"); }
            }

            public override void ExplicitVisit(NamedTableReference node)
            {
                if (!aliasTargets.Contains(node)) { Add(node.SchemaObject.Identifiers.Select(part => part.Value), writeTargets.Contains(node) ? SqlDependencyAccess.Write : SqlDependencyAccess.Read, SqlDependencyEvidence.ParsedSchemaObject); }
                node.AcceptChildren(this);
            }
            public override void ExplicitVisit(TruncateTableStatement node) => Add(node.TableName.Identifiers.Select(part => part.Value), SqlDependencyAccess.Write, SqlDependencyEvidence.ParsedSchemaObject);
            public override void ExplicitVisit(SchemaObjectFunctionTableReference node)
            { Add(node.SchemaObject.Identifiers.Select(part => part.Value), SqlDependencyAccess.Function, SqlDependencyEvidence.ParsedFunctionReference); node.AcceptChildren(this); }
            public override void ExplicitVisit(FunctionCall node)
            {
                if (node.CallTarget is MultiPartIdentifierCallTarget target)
                { Add(target.MultiPartIdentifier.Identifiers.Select(part => part.Value).Concat(new[] { node.FunctionName.Value }), SqlDependencyAccess.Function, SqlDependencyEvidence.ParsedFunctionReference); }
                node.AcceptChildren(this);
            }
            public override void ExplicitVisit(ExecutableStringList node) { Gaps.Add("sql.dynamic_sql"); }
            public override void ExplicitVisit(ExecutableProcedureReference node)
            {
                var name = node.ProcedureReference?.ProcedureReference?.Name;
                if (name == null || name.BaseIdentifier.Value.Equals("sp_executesql", StringComparison.OrdinalIgnoreCase))
                { Gaps.Add("sql.dynamic_sql"); return; }
                Add(name.Identifiers.Select(part => part.Value), SqlDependencyAccess.Execute, SqlDependencyEvidence.ParsedExecuteTarget);
                node.AcceptChildren(this);
            }
            public override void ExplicitVisit(UseStatement node) { contextChanged = true; Gaps.Add("sql.context_changed"); }
            public override void ExplicitVisit(OpenRowsetTableReference node) { Gaps.Add("sql.unsupported_construct"); }
            public override void ExplicitVisit(OpenQueryTableReference node) { Gaps.Add("sql.unsupported_construct"); }

            public override void Visit(TSqlFragment node)
            {
                if (node is TSqlStatement && !(node is SelectStatement || node is InsertStatement || node is UpdateStatement || node is DeleteStatement ||
                    node is MergeStatement || node is ExecuteStatement || node is TruncateTableStatement || node is UseStatement ||
                    node is DeclareVariableStatement || node is SetVariableStatement || node is BeginEndBlockStatement || node is IfStatement ||
                    node is WhileStatement || node is PrintStatement || node is ReturnStatement)) { Gaps.Add("sql.unsupported_construct"); }
            }

            private void Add(IEnumerable<string> identifiers, SqlDependencyAccess access, SqlDependencyEvidence evidence)
            {
                var parts = identifiers.ToArray();
                if (contextChanged) { Gaps.Add("sql.context_changed"); return; }
                if (parts.Length == 0 || parts.Length > 4 || string.IsNullOrWhiteSpace(parts.Last())) { Gaps.Add("sql.reference_unresolved"); return; }
                if (parts.Last().StartsWith("#", StringComparison.Ordinal) || parts.Last().StartsWith("@", StringComparison.Ordinal)) { return; }
                if (parts.Length == 1 && evidence == SqlDependencyEvidence.ParsedSchemaObject)
                {
                    if (cteScopes.Any(scope => scope.Contains(parts[0])))
                    { if (access == SqlDependencyAccess.Write) { Gaps.Add("sql.reference_unresolved"); } return; }
                    if (cteScopes.Any(scope => scope.Any(name => name.Equals(parts[0], StringComparison.OrdinalIgnoreCase))))
                    { Gaps.Add("sql.reference_unresolved"); return; }
                }
                References.Add(new SqlObjectReference(parts, access, evidence));
            }
        }

        private sealed class AliasVisitor : TSqlFragmentVisitor
        {
            internal readonly List<NamedTableReference> Tables = new List<NamedTableReference>();
            internal readonly HashSet<string> DerivedAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public override void ExplicitVisit(NamedTableReference node) { Tables.Add(node); }
            public override void ExplicitVisit(QueryDerivedTable node) { if (node.Alias != null) { DerivedAliases.Add(node.Alias.Value); } }
            public override void ExplicitVisit(ScalarSubquery node) { }
        }
    }
}