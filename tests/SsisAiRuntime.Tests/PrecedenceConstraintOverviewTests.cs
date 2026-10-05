using SsisAiRuntime.Inspectors;

namespace SsisAiRuntime.Tests;

public class PrecedenceConstraintOverviewTests
{
    [Fact]
    public void OverviewPreservesEndpointsAndOmitsExpressionText()
    {
        var overview = new PrecedenceConstraintOverview(
            "constraint-id",
            "On success",
            "from-id",
            "Extract",
            "to-id",
            "Load",
            "Constraint",
            "Success",
            true,
            true);

        Assert.Equal("from-id", overview.FromExecutableId);
        Assert.Equal("to-id", overview.ToExecutableId);
        Assert.Equal("Constraint", overview.EvaluationOperation);
        Assert.Equal("Success", overview.ConstraintValue);
        Assert.True(overview.LogicalAnd);
        Assert.True(overview.ExpressionTextOmitted);
        Assert.Null(typeof(PrecedenceConstraintOverview).GetProperty("Expression"));
    }
}