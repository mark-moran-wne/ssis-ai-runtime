using System;
using System.IO;
using SsisAiRuntime.Core;

namespace SsisAiRuntime.Tests;

public class PackageSessionTests
{
    [Fact]
    public void SessionRetainsNativePackageAndCreatesUniqueIdentity()
    {
        var package = new FakePackage();
        var path = Path.GetFullPath("sample.dtsx");

        var first = new PackageSession<FakePackage>(path, "Sample", package);
        var second = new PackageSession<FakePackage>(path, "Sample", new FakePackage());

        Assert.NotEqual(Guid.Empty, first.SessionId);
        Assert.NotEqual(first.SessionId, second.SessionId);
        Assert.Equal(path, first.PackagePath);
        Assert.Equal("Sample", first.PackageName);
        Assert.Same(package, first.Package);
    }

    [Fact]
    public void SessionRejectsMissingPathAndPackage()
    {
        Assert.Throws<ArgumentException>(() => new PackageSession<FakePackage>(" ", "Sample", new FakePackage()));
        Assert.Throws<ArgumentNullException>(() => new PackageSession<FakePackage>("sample.dtsx", "Sample", null!));
    }

    [Fact]
    public void LoadResultRequiresSessionOnSuccessAndErrorDiagnosticsOnFailure()
    {
        var session = new PackageSession<FakePackage>("sample.dtsx", "Sample", new FakePackage());
        var successDiagnostics = new RuntimeDiagnostics("16.0", "x64", Array.Empty<RuntimeDiagnostic>());
        var success = PackageLoadResult<FakePackage>.Success(session, successDiagnostics);

        var error = new RuntimeDiagnostic("load.failed", RuntimeDiagnosticSeverity.Error, "Load failed.");
        var failureDiagnostics = new RuntimeDiagnostics("16.0", "x64", new[] { error });
        var failure = PackageLoadResult<FakePackage>.Failure(failureDiagnostics);

        Assert.True(success.Succeeded);
        Assert.Same(session, success.Session);
        Assert.False(failure.Succeeded);
        Assert.Null(failure.Session);
        Assert.True(failure.Diagnostics.HasErrors);
    }

    [Fact]
    public void LoadResultRejectsInconsistentSessionAndDiagnostics()
    {
        var session = new PackageSession<FakePackage>("sample.dtsx", "Sample", new FakePackage());
        var successDiagnostics = new RuntimeDiagnostics("16.0", "x64", Array.Empty<RuntimeDiagnostic>());
        var error = new RuntimeDiagnostic("load.failed", RuntimeDiagnosticSeverity.Error, "Load failed.");
        var failureDiagnostics = new RuntimeDiagnostics("16.0", "x64", new[] { error });

        Assert.Throws<ArgumentException>(() => PackageLoadResult<FakePackage>.Failure(successDiagnostics));
        Assert.Throws<ArgumentException>(() => PackageLoadResult<FakePackage>.Success(session, failureDiagnostics));
    }

    private sealed class FakePackage
    {
    }
}