using SsisAiRuntime.Inspectors;

namespace SsisAiRuntime.Tests;

public class ConnectionAndVariableOverviewTests
{
    [Fact]
    public void ConnectionOverviewExposesMetadataButNotConnectionString()
    {
        var overview = new ConnectionOverview("Warehouse", "connection-id", "OLEDB");

        Assert.Equal("Warehouse", overview.Name);
        Assert.Equal("connection-id", overview.Id);
        Assert.Equal("OLEDB", overview.CreationName);
        Assert.True(overview.ConnectionStringOmitted);
        Assert.Null(typeof(ConnectionOverview).GetProperty("ConnectionString"));
    }

    [Fact]
    public void VariableOverviewExposesFlagsButNotValueOrExpressionText()
    {
        var overview = new VariableOverview(
            "ApiToken",
            "User",
            "String",
            true,
            false,
            true,
            true);

        Assert.Equal("ApiToken", overview.Name);
        Assert.Equal("User", overview.Namespace);
        Assert.Equal("String", overview.DataType);
        Assert.True(overview.IsReadOnly);
        Assert.False(overview.IsSystemVariable);
        Assert.True(overview.EvaluatesAsExpression);
        Assert.True(overview.HasExpression);
        Assert.True(overview.ValueOmitted);
        Assert.True(overview.ExpressionOmitted);
        Assert.Null(typeof(VariableOverview).GetProperty("Value"));
        Assert.Null(typeof(VariableOverview).GetProperty("Expression"));
    }
}