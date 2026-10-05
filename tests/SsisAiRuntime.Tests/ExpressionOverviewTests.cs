using SsisAiRuntime.Inspectors;

namespace SsisAiRuntime.Tests;

public class ExpressionOverviewTests
{
    [Fact]
    public void OverviewIdentifiesExpressionOwnerWithoutExposingExpressionText()
    {
        var overview = new ExpressionOverview("Variable", "variable-id", "ComputedValue", "UserVariable");

        Assert.Equal("Variable", overview.ObjectType);
        Assert.Equal("variable-id", overview.ObjectId);
        Assert.Equal("ComputedValue", overview.ObjectName);
        Assert.Equal("UserVariable", overview.CreationName);
        Assert.True(overview.HasExpression);
        Assert.True(overview.ExpressionTextOmitted);
        Assert.True(overview.PropertyAssociationOmitted);
        Assert.Null(typeof(ExpressionOverview).GetProperty("Expression"));
    }
}