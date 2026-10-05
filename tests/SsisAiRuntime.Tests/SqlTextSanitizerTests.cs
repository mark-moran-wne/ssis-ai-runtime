using SsisAiRuntime.Inspectors;

namespace SsisAiRuntime.Tests;

public class SqlTextSanitizerTests
{
    [Fact]
    public void SanitizerPreservesQueryStructureAndRemovesLiteralsCommentsAndCredentials()
    {
        var result = SqlTextSanitizer.Sanitize(
            "SELECT 'private value', \"private identifier or value\" WHERE password = 'secret value'; -- comment secret\nSELECT 1 /* hidden note */");

        Assert.True(result.Redacted);
        Assert.Contains("SELECT", result.Text);
        Assert.Contains("password=<redacted>", result.Text);
        Assert.DoesNotContain("private value", result.Text);
        Assert.DoesNotContain("private identifier or value", result.Text);
        Assert.DoesNotContain("secret value", result.Text);
        Assert.DoesNotContain("comment secret", result.Text);
        Assert.DoesNotContain("hidden note", result.Text);
    }

    [Fact]
    public void SanitizerReportsUnchangedStatements()
    {
        var result = SqlTextSanitizer.Sanitize("SELECT 1");

        Assert.Equal("SELECT 1", result.Text);
        Assert.False(result.Redacted);
    }
}