using SsisAiRuntime.Inspectors;

namespace SsisAiRuntime.Tests;

public class ParameterOverviewTests
{
    [Fact]
    public void OverviewReportsSensitivityAndAlwaysOmitsValue()
    {
        var overview = new ParameterOverview(
            "parameter-id",
            "ApiToken",
            "PackageParameter",
            "Token used by the package",
            "String",
            true,
            true);

        Assert.Equal("parameter-id", overview.Id);
        Assert.Equal("ApiToken", overview.Name);
        Assert.Equal("PackageParameter", overview.CreationName);
        Assert.Equal("Token used by the package", overview.Description);
        Assert.Equal("String", overview.DataType);
        Assert.True(overview.IsRequired);
        Assert.True(overview.IsSensitive);
        Assert.True(overview.ValueOmitted);
        Assert.Null(typeof(ParameterOverview).GetProperty("Value"));
    }
}