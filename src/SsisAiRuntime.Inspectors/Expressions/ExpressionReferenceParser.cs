using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Antlr4.Runtime;
using SsisAiRuntime.Inspectors.Expressions.Generated;

namespace SsisAiRuntime.Inspectors.Expressions
{
    public sealed class ExpressionReferenceParser
    {
        public const int MaximumExpressionLength = 65536;
        public ExpressionAnalysisResult Analyze(string expression)
        {
            if (expression == null) { throw new ArgumentNullException(nameof(expression)); }
            if (expression.Length > MaximumExpressionLength) { return Failed("expression.length.exceeded"); }
            var diagnostics = new List<ExpressionParseDiagnostic>();
            var listener = new ErrorListener(diagnostics);
            var lexer = new SsisExpressionLexer(new AntlrInputStream(expression));
            lexer.RemoveErrorListeners();
            lexer.AddErrorListener(listener);
            var tokens = new CommonTokenStream(lexer);
            tokens.Fill();
            var allTokens = tokens.GetTokens();
            if (allTokens.Count(item => item.Text == "=") > 128) { return Failed("expression.depth.exceeded"); }
            var depth = 0;
            var groups = 0;
            var unaryRun = 0;
            foreach (var token in allTokens.Where(item => item.Channel == TokenConstants.DefaultChannel))
            {
                if (token.Text == "(" || token.Text == "?") { depth++; groups++; }
                if (token.Text == ")") { depth--; }
                unaryRun = token.Text == "!" || token.Text == "~" || token.Text == "+" || token.Text == "-" ? unaryRun + 1 : 0;
                if (depth > 128 || groups > 128 || unaryRun > 128)
                { return Failed("expression.depth.exceeded"); }
            }
            var parser = new SsisExpressionParser(tokens) { BuildParseTree = false };
            parser.RemoveErrorListeners();
            parser.AddErrorListener(listener);
            parser.parse();
            if (diagnostics.Count != 0) { return new ExpressionAnalysisResult(Array.Empty<ExpressionReference>(), diagnostics); }
            var references = allTokens.Where(token => token.Type == SsisExpressionLexer.WRAPPED_REFERENCE ||
                token.Type == SsisExpressionLexer.UNWRAPPED_REFERENCE).Select(Candidate).ToArray();
            if (references.Any(reference => reference == null)) { return Failed("expression.syntax.invalid"); }
            return new ExpressionAnalysisResult(references, diagnostics);
        }

        private static ExpressionReference Candidate(IToken token)
        {
            var wrapped = token.Type == SsisExpressionLexer.WRAPPED_REFERENCE;
            var content = wrapped ? token.Text.Substring(2, token.Text.Length - 3).Replace("\\]", "]") : token.Text.Substring(1);
            if (string.IsNullOrWhiteSpace(content)) { return null; }
            var separator = wrapped ? content.IndexOf("::", StringComparison.Ordinal) : -1;
            var namespaceName = separator > 0 ? content.Substring(0, separator) : string.Empty;
            var name = separator > 0 ? content.Substring(separator + 2) : content;
            var kind = wrapped && separator <= 0 ? ExpressionReferenceKind.Unknown : ExpressionReferenceKind.Variable;
            if (namespaceName.Equals("System", StringComparison.OrdinalIgnoreCase)) { kind = ExpressionReferenceKind.SystemVariable; }
            if (namespaceName.Equals("$Package", StringComparison.OrdinalIgnoreCase)) { kind = ExpressionReferenceKind.PackageParameter; }
            if (namespaceName.Equals("$Project", StringComparison.OrdinalIgnoreCase)) { kind = ExpressionReferenceKind.ProjectParameter; }
            if (string.IsNullOrWhiteSpace(name)) { kind = ExpressionReferenceKind.Unknown; name = content; }
            return new ExpressionReference(kind, namespaceName, name, token.StartIndex, token.StopIndex - token.StartIndex + 1);
        }

        private static ExpressionAnalysisResult Failed(string code) => new ExpressionAnalysisResult(Array.Empty<ExpressionReference>(),
            new[] { new ExpressionParseDiagnostic(1, 0, code) });

        private sealed class ErrorListener : BaseErrorListener, IAntlrErrorListener<int>
        {
            private readonly ICollection<ExpressionParseDiagnostic> diagnostics;
            public ErrorListener(ICollection<ExpressionParseDiagnostic> diagnostics) { this.diagnostics = diagnostics; }
            public override void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol,
                int line, int charPositionInLine, string msg, RecognitionException exception)
            { diagnostics.Add(new ExpressionParseDiagnostic(line, charPositionInLine, "expression.syntax.invalid")); }
            public void SyntaxError(TextWriter output, IRecognizer recognizer, int offendingSymbol,
                int line, int charPositionInLine, string msg, RecognitionException exception)
            { diagnostics.Add(new ExpressionParseDiagnostic(line, charPositionInLine, "expression.syntax.invalid")); }
        }
    }
}