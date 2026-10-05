using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SsisAiRuntime.Inspectors.SqlDependencies
{
    internal sealed class SqlScriptLexer
    {
        internal IList<TSqlParserToken> Tokenize(string sql, out IList<ParseError> errors) =>
            new TSql160Parser(true).GetTokenStream(new StringReader(sql), out errors);
    }

    internal sealed class SqlScriptParser
    {
        internal TSqlFragment Parse(string sql)
        {
            var tokens = new SqlScriptLexer().Tokenize(sql, out var lexicalErrors);
            if (lexicalErrors.Count != 0 || tokens.Count > 16384) { return null; }
            var depth = 0;
            foreach (var token in tokens)
            {
                if ((token.TokenType == TSqlTokenType.LeftParenthesis || token.TokenType == TSqlTokenType.Begin || token.TokenType == TSqlTokenType.Case) && ++depth > 128) { return null; }
                if (token.TokenType == TSqlTokenType.RightParenthesis || token.TokenType == TSqlTokenType.End) { depth--; }
            }
            var fragment = new TSql160Parser(true).Parse(tokens, out var errors);
            return errors.Count == 0 ? fragment : null;
        }
    }
}