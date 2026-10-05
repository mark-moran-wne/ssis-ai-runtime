using Newtonsoft.Json;
using SsisAiRuntime.Inspectors;
using SsisAiRuntime.Inspectors.Expressions;
using SsisAiRuntime.Inspectors.SqlDependencies;

namespace SsisAiRuntime.Tests;

public sealed class SanitizedTextTests
{
    [Fact]
    public void SqlSanitizerUsesTokensNotRegexAndMasksLiteralsAndComments()
    {
        var result = SanitizedTextFactory.Sql("task", "connection",
            "select N'password--/*escaped''secret*/' as Value, 12345, 4.567, 0xDEADBEEF from dbo.Student; -- comment-secret\n/* block-secret */");
        Assert.True(result.IsAvailable);
        Assert.Contains("dbo.Student", result.Text);
        Assert.Contains("<redacted>", result.Text);
        Assert.All(new[] { "password", "secret", "12345", "4.567", "DEADBEEF" }, secret => Assert.DoesNotContain(secret, result.Text));
        Assert.Equal("SanitizedSyntaxOnly", result.Evidence);
        Assert.Equal("untrusted-package-content", result.Trust);
    }

    [Fact]
    public void ExpressionsRetainReferencesButMaskEveryLiteral()
    {
        var result = SanitizedTextFactory.Expression(new ExpressionDependencyInput("owner", "scope", "UntrustedProperty",
            "@[User::Name] == \"password\\\"secret\" ? 12345 : 67890"));
        Assert.True(result.IsAvailable);
        Assert.Contains("@[User::Name]", result.Text);
        Assert.DoesNotContain("password", result.Text);
        Assert.DoesNotContain("secret", result.Text);
        Assert.DoesNotContain("12345", result.Text);
        Assert.DoesNotContain("67890", result.Text);
        Assert.Equal("PropertyExpression", result.PropertyCategory);
    }

    [Fact]
    public void InvalidOrUnsupportedTextIsOmittedRatherThanPartiallySanitized()
    {
        var sql = SanitizedTextFactory.Sql("owner", "connection", "select 'secret' from");
        var expression = SanitizedTextFactory.Expression(new ExpressionDependencyInput("owner", "scope", "PropertyExpression", "\"secret"));
        Assert.False(sql.IsAvailable);
        Assert.False(expression.IsAvailable);
        Assert.Null(sql.Text);
        Assert.Null(expression.Text);
        Assert.DoesNotContain("secret", JsonConvert.SerializeObject(sql));
        Assert.False(SanitizedTextFactory.Sql("owner", "connection", "select 1", SqlDialect.Unsupported).IsAvailable);
    }

    [Fact]
    public void DoubleQuotedContentIsMaskedRegardlessOfQuotedIdentifierMode()
    {
        var result = SanitizedTextFactory.Sql("owner", "connection", "set quoted_identifier off; select \"double-quoted-secret\";");
        Assert.True(result.IsAvailable);
        Assert.DoesNotContain("double-quoted-secret", result.Text);
    }

    [Fact]
    public void TextIsBoundedAfterSanitization()
    {
        var sql = "select " + string.Join(",", Enumerable.Repeat("'secret'", 1000));
        var result = SanitizedTextFactory.Sql("owner", "connection", sql);
        Assert.True(result.IsAvailable);
        Assert.Equal(SanitizedTextFactory.MaximumTextLength, result.Text.Length);
        Assert.True(result.CharactersOmitted > 0);
        Assert.DoesNotContain("secret", result.Text);
    }
}