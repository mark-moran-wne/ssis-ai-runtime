using System.Text;

namespace SsisAiRuntime.Mutations.Tests;

public sealed class MutationArtifactStagerTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "SsisMutationStaging-" + Guid.NewGuid().ToString("N"));
    private readonly string source;
    private readonly string destination;

    public MutationArtifactStagerTests()
    {
        Directory.CreateDirectory(directory);
        source = Path.Combine(directory, "source.dtsx");
        destination = Path.Combine(directory, "modified.dtsx");
        File.WriteAllText(source, "original");
    }

    [Fact]
    public void PublishesOnlyAfterReadOnlyVerificationAndPreservesSource()
    {
        new MutationArtifactStager().StageAndPublish(source, destination, WriteArtifact, staged =>
        {
            Assert.False(File.Exists(destination));
            Assert.False(staged.CanWrite);
            Assert.Equal("modified", new StreamReader(staged, Encoding.UTF8, false, 1024, true).ReadToEnd());
            Assert.Single(Directory.GetFiles(directory, ".ssis-mutation-*.dtsx"));
            return true;
        });

        Assert.Equal("modified", File.ReadAllText(destination));
        Assert.Equal("original", File.ReadAllText(source));
        Assert.Empty(Directory.GetFiles(directory, ".ssis-mutation-*.dtsx"));
    }

    [Theory]
    [InlineData("source.dtsx")]
    [InlineData("existing.dtsx")]
    [InlineData("invalid.txt")]
    public void InvalidDestinationsNeverInvokeWriter(string name)
    {
        var target = Path.Combine(directory, name);
        if (name == "existing.dtsx") { File.WriteAllText(target, "existing"); }
        var calls = 0;

        Assert.ThrowsAny<Exception>(() => new MutationArtifactStager().StageAndPublish(source, target,
            staged => { calls++; WriteArtifact(staged); }, staged => true));

        Assert.Equal(0, calls);
        Assert.Equal("original", File.ReadAllText(source));
        Assert.Empty(Directory.GetFiles(directory, ".ssis-mutation-*.dtsx"));
    }

    [Fact]
    public void FailedVerificationCleansStagingAndDoesNotPublish()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new MutationArtifactStager().StageAndPublish(source, destination, WriteArtifact, staged => false));
        AssertCleanFailure();
    }

    [Fact]
    public void WriterFailureCleansStagingAndDoesNotInvokeVerifier()
    {
        var verificationCalls = 0;
        Assert.Throws<InvalidOperationException>(() => new MutationArtifactStager().StageAndPublish(source, destination,
            staged => { WriteArtifact(staged); throw new InvalidOperationException("test.failure"); },
            staged => { verificationCalls++; return true; }));
        Assert.Equal(0, verificationCalls);
        AssertCleanFailure();
    }

    [Fact]
    public void VerifierExceptionCleansStagingWithoutPublishing()
    {
        Assert.Throws<InvalidOperationException>(() => new MutationArtifactStager().StageAndPublish(source, destination,
            WriteArtifact, staged => throw new InvalidOperationException("test.failure")));
        AssertCleanFailure();
    }

    [Fact]
    public void ConcurrentDestinationCreationIsNeverOverwritten()
    {
        Assert.Throws<IOException>(() => new MutationArtifactStager().StageAndPublish(source, destination,
            WriteArtifact, staged => { File.WriteAllText(destination, "other-writer"); return true; }));
        Assert.Equal("other-writer", File.ReadAllText(destination));
        Assert.Equal("original", File.ReadAllText(source));
        Assert.Empty(Directory.GetFiles(directory, ".ssis-mutation-*.dtsx"));
    }

    [Fact]
    public void EmptyArtifactIsNotPublished()
    {
        Assert.Throws<InvalidOperationException>(() => new MutationArtifactStager().StageAndPublish(source, destination,
            staged => { }, staged => true));
        AssertCleanFailure();
    }

    [Fact]
    public void SourceIsLockedAgainstWritesDuringWindowsStaging()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        new MutationArtifactStager().StageAndPublish(source, destination, staged =>
        {
            Assert.Throws<IOException>(() => File.WriteAllText(source, "unexpected"));
            WriteArtifact(staged);
        }, staged =>
        {
            Assert.Throws<IOException>(() => File.Delete(source));
            return true;
        });
        Assert.Equal("original", File.ReadAllText(source));
    }

    private static void WriteArtifact(Stream stream)
    {
        var bytes = Encoding.UTF8.GetBytes("modified");
        stream.Write(bytes, 0, bytes.Length);
    }

    private void AssertCleanFailure()
    {
        Assert.False(File.Exists(destination));
        Assert.Equal("original", File.ReadAllText(source));
        Assert.Empty(Directory.GetFiles(directory, ".ssis-mutation-*.dtsx"));
    }

    public void Dispose() => Directory.Delete(directory, true);
}