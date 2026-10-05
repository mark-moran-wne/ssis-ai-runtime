using System;
using System.Linq;
using System.Text;
using Antlr4.Runtime;
using SsisAiRuntime.Inspectors.Expressions;
using SsisAiRuntime.Inspectors.Expressions.Generated;
using SsisAiRuntime.Inspectors.SqlDependencies;

namespace SsisAiRuntime.Inspectors
{
    public enum SanitizedTextKind { Sql, Expression }

    public sealed class SanitizedTextSnippet
    {
        internal SanitizedTextSnippet(string ownerId, string scopeId, string category, SanitizedTextKind kind, string text)
        {
            OwnerNativeId = ownerId ?? string.Empty; OwnerScopeId = scopeId ?? string.Empty; PropertyCategory = category;
            Kind = kind;
            IsAvailable = text != null;
            CharactersOmitted = Math.Max(0, (text?.Length ?? 0) - SanitizedTextFactory.MaximumTextLength);
            Text = text == null ? null : text.Substring(0, Math.Min(text.Length, SanitizedTextFactory.MaximumTextLength));
        }
        public string OwnerNativeId { get; }
        public string OwnerScopeId { get; }
        public string PropertyCategory { get; }
        public SanitizedTextKind Kind { get; }
        public string Text { get; }
        public bool IsAvailable { get; }
        public int CharactersOmitted { get; }
        public string CoverageCode => IsAvailable ? string.Empty : "context.text_unavailable";
        public string Trust => "untrusted-package-content";
        public string Evidence => "SanitizedSyntaxOnly";
    }

    public static class SanitizedTextFactory
    {
        public const int MaximumTextLength = 4096;

        public static SanitizedTextSnippet Sql(string ownerId, string connectionId, string sql, SqlDialect dialect = SqlDialect.TSql160)
        {
            var text = new StringBuilder();
            if (dialect != SqlDialect.TSql160 || sql == null || sql.Length > SqlDependencyAnalyzer.MaximumSqlLength)
            { return new SanitizedTextSnippet(ownerId, connectionId, "SqlStatement", SanitizedTextKind.Sql, null); }
            var parsed = new SqlScriptParser().Parse(sql);
            if (parsed == null) { return new SanitizedTextSnippet(ownerId, connectionId, "SqlStatement", SanitizedTextKind.Sql, null); }
            foreach (var token in parsed.ScriptTokenStream)
            {
                var kind = token.TokenType.ToString();
                if (kind == "EndOfFile" || kind == "SingleLineComment" || kind == "MultilineComment") { text.Append(' '); continue; }
                if (kind.IndexOf("StringLiteral", StringComparison.Ordinal) >= 0 || token.Text.StartsWith("\"", StringComparison.Ordinal)) { text.Append("'<redacted>'"); }
                else if (kind.IndexOf("Literal", StringComparison.Ordinal) >= 0 || kind == "Integer" || kind == "Numeric" || kind == "Real" || kind == "Money")
                { text.Append('0'); }
                else { text.Append(token.Text); }
            }
            return new SanitizedTextSnippet(ownerId, connectionId, "SqlStatement", SanitizedTextKind.Sql, text.ToString());
        }

        public static SanitizedTextSnippet Expression(ExpressionDependencyInput input)
        {
            if (input == null) { throw new ArgumentNullException(nameof(input)); }
            var category = ExpressionDependencyGraphBuilder.SafePropertyCategory(input.PropertyName);
            if (!new ExpressionReferenceParser().Analyze(input.Expression).Succeeded)
            { return new SanitizedTextSnippet(input.OwnerNativeId, input.OwnerScopeId, category, SanitizedTextKind.Expression, null); }
            var lexer = new SsisExpressionLexer(new AntlrInputStream(input.Expression));
            lexer.RemoveErrorListeners();
            var tokens = new CommonTokenStream(lexer); tokens.Fill();
            var text = new StringBuilder();
            foreach (var token in tokens.GetTokens().Where(token => token.Type != TokenConstants.EOF))
            {
                if (token.Type == SsisExpressionLexer.STRING) { text.Append("\"<redacted>\""); }
                else if (token.Type == SsisExpressionLexer.INTEGER || token.Type == SsisExpressionLexer.NUMBER || token.Type == SsisExpressionLexer.HEX)
                { text.Append('0'); }
                else if (token.Type == SsisExpressionLexer.BOOLEAN) { text.Append("FALSE"); }
                else { text.Append(token.Text); }
            }
            return new SanitizedTextSnippet(input.OwnerNativeId, input.OwnerScopeId, category, SanitizedTextKind.Expression, text.ToString());
        }
    }
}