using System.Text.RegularExpressions;

namespace SsisAiRuntime.Inspectors
{
    public static class SqlTextSanitizer
    {
        private static readonly Regex BlockComments = new Regex(@"/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline);
        private static readonly Regex LineComments = new Regex(@"--[^\r\n]*", RegexOptions.Compiled);
        private static readonly Regex CredentialAssignments = new Regex(
            @"(?i)\b(password|pwd|passphrase|secret|client_secret|api[_-]?key|access[_-]?token|refresh[_-]?token)\s*=\s*('[^']*'|""[^""]*""|[^,\s;]+)",
            RegexOptions.Compiled);
        private static readonly Regex StringLiterals = new Regex(@"'(?:''|[^'])*'", RegexOptions.Compiled);
        private static readonly Regex QuotedLiterals = new Regex("\"(?:\"\"|[^\"])*\"", RegexOptions.Compiled);

        public static SqlTextSanitizerResult Sanitize(string sql)
        {
            var original = sql ?? string.Empty;
            var sanitized = BlockComments.Replace(original, string.Empty);
            sanitized = LineComments.Replace(sanitized, string.Empty);
            sanitized = CredentialAssignments.Replace(sanitized, "$1=<redacted>");
            sanitized = StringLiterals.Replace(sanitized, "'<redacted>'");
            sanitized = QuotedLiterals.Replace(sanitized, "\"<redacted>\"");
            return new SqlTextSanitizerResult(sanitized, sanitized != original);
        }
    }
}