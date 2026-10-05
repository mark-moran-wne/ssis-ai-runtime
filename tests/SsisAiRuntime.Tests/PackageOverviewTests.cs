using SsisAiRuntime.Inspectors;

namespace SsisAiRuntime.Tests;

public class PackageOverviewTests
{
    [Fact]
    public void OverviewRetainsPortablePackageMetadata()
    {
        var sessionId = Guid.NewGuid();
        var creationDate = new DateTime(2024, 10, 4, 12, 30, 0, DateTimeKind.Utc);
        var overview = new PackageOverview(
            sessionId,
            "Sample",
            "package-id",
            "Description",
            creationDate,
            1,
            2,
            3,
            "EncryptSensitiveWithUserKey",
            "Default",
            4,
            5,
            6,
            7,
            8,
            true);

        Assert.Equal(sessionId, overview.SessionId);
        Assert.Equal("Sample", overview.PackageName);
        Assert.Equal("package-id", overview.PackageId);
        Assert.Equal("Description", overview.Description);
        Assert.Equal(creationDate, overview.CreationDate);
        Assert.Equal((1, 2, 3), (overview.VersionMajor, overview.VersionMinor, overview.VersionBuild));
        Assert.Equal("EncryptSensitiveWithUserKey", overview.ProtectionLevel);
        Assert.Equal("Default", overview.PackageType);
        Assert.Equal((4, 5, 6, 7, 8), (
            overview.ConnectionCount,
            overview.VariableCount,
            overview.ExecutableCount,
            overview.PrecedenceConstraintCount,
            overview.ParameterCount));
        Assert.True(overview.HasExpressions);
    }
}