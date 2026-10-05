namespace SsisAiRuntime.Inspectors
{
    public sealed class SqlTextSanitizerResult
    {
        public SqlTextSanitizerResult(string text, bool redacted)
        {
            Text = text ?? string.Empty;
            Redacted = redacted;
        }

        public string Text { get; }

        public bool Redacted { get; }
    }
}