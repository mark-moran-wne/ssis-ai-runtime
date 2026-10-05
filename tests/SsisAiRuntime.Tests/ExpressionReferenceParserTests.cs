using SsisAiRuntime.Inspectors.Expressions;

namespace SsisAiRuntime.Tests;

public sealed class ExpressionReferenceParserTests
{
    [Theory]
    [InlineData("@[User::BatchDate]", ExpressionReferenceKind.Variable, "User", "BatchDate")]
    [InlineData("@[System::StartTime]", ExpressionReferenceKind.SystemVariable, "System", "StartTime")]
    [InlineData("@[$Package::BatchSize]", ExpressionReferenceKind.PackageParameter, "$Package", "BatchSize")]
    [InlineData("@[$Project::Warehouse]", ExpressionReferenceKind.ProjectParameter, "$Project", "Warehouse")]
    [InlineData("@Counter", ExpressionReferenceKind.Variable, "", "Counter")]
    public void ClassifiesCandidates(string text, ExpressionReferenceKind kind, string namespaceName, string name)
    {
        var result = new ExpressionReferenceParser().Analyze(text);
        Assert.True(result.Succeeded);
        var reference = Assert.Single(result.References);
        Assert.Equal(kind, reference.Kind);
        Assert.Equal(namespaceName, reference.NamespaceName);
        Assert.Equal(name, reference.Name);
        Assert.Equal(0, reference.StartIndex);
        Assert.Equal(text.Length, reference.Length);
    }

    [Theory]
    [InlineData("\"@[User::Secret]\"")]
    [InlineData("\"escaped \\\" @Counter\"")]
    [InlineData("42")]
    [InlineData("TRUE ? 1 : 2")]
    public void LiteralsAndConstantsHaveNoCandidates(string text)
    { var result = new ExpressionReferenceParser().Analyze(text); Assert.True(result.Succeeded); Assert.Empty(result.References); }

    [Fact]
    public void CastsFunctionsConditionalsAndRepeatedReferencesRetainPositions()
    {
        var result = new ExpressionReferenceParser().Analyze("(DT_WSTR,30)(@Counter > 0 ? ABS(@Counter) : 0)");
        Assert.True(result.Succeeded);
        Assert.Equal(2, result.References.Count);
        Assert.True(result.References[0].StartIndex < result.References[1].StartIndex);
    }

    [Theory]
    [InlineData("@[User::Value] +")]
    [InlineData("@Counter + 'secret'")]
    [InlineData("\"unterminated")]
    [InlineData("")]
    [InlineData("@[ ]")]
    public void FailureDiscardsCandidatesAndRawMessages(string text)
    {
        var result = new ExpressionReferenceParser().Analyze(text);
        Assert.False(result.Succeeded);
        Assert.Empty(result.References);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal("expression.syntax.invalid", diagnostic.Code));
    }

    [Fact]
    public void BoundsLengthAndRecursion()
    {
        Assert.False(new ExpressionReferenceParser().Analyze(new string('1', 65537)).Succeeded);
        Assert.False(new ExpressionReferenceParser().Analyze(new string('(', 129) + "1" + new string(')', 129)).Succeeded);
        Assert.False(new ExpressionReferenceParser().Analyze(string.Concat(Enumerable.Repeat("(DT_I4)", 129)) + "1").Succeeded);
        var assignment = new ExpressionReferenceParser().Analyze("@Counter = @Counter + 1");
        Assert.True(assignment.Succeeded);
        Assert.Equal(2, assignment.References.Count);
    }
}