using SsisAiRuntime.Inspectors;

namespace SsisAiRuntime.Tests;

public class SemanticHandleCatalogTests
{
    [Fact]
    public void CatalogCreatesSessionScopedHierarchicalHandlesWithoutNativeIds()
    {
        var sessionId = Guid.NewGuid();
        var package = CreatePackageOverview(sessionId);
        var executables = new[]
        {
            new ExecutableOverview("native-sequence-id", string.Empty, "Load", "Sequence", string.Empty, 0, true, false),
            new ExecutableOverview("native-task-id", "native-sequence-id", "Transform", "Vendor.Transform", string.Empty, 1, false, false)
        };

        var catalog = BuildCatalog(package, executables: executables);
        var task = catalog.FindByName("Transform", SemanticObjectKind.Executable);

        Assert.Equal(SemanticHandleResolutionStatus.Resolved, task.Status);
        Assert.Equal(sessionId, task.ResolvedObject.Handle.SessionId);
        Assert.Contains("Load", task.ResolvedObject.Handle.Value);
        Assert.DoesNotContain("native-task-id", task.ResolvedObject.Handle.Value);
        Assert.DoesNotContain("native-sequence-id", task.ResolvedObject.Handle.Value);
        Assert.Equal(task.ResolvedObject, catalog.Resolve(task.ResolvedObject.Handle).ResolvedObject);
    }

    [Fact]
    public void DuplicateNamesReturnAmbiguousCandidatesInsteadOfChoosing()
    {
        var package = CreatePackageOverview(Guid.NewGuid());
        var connections = new[]
        {
            new ConnectionOverview("Warehouse", "native-connection-1", "OLEDB"),
            new ConnectionOverview("Warehouse", "native-connection-2", "OLEDB")
        };

        var catalog = BuildCatalog(package, connections: connections);
        var result = catalog.FindByName("warehouse", SemanticObjectKind.Connection);

        Assert.Equal(SemanticHandleResolutionStatus.Ambiguous, result.Status);
        Assert.Null(result.ResolvedObject);
        Assert.Equal(2, result.Candidates.Count);
        Assert.NotEqual(result.Candidates[0].Handle.Value, result.Candidates[1].Handle.Value);
        Assert.All(result.Candidates, candidate => Assert.DoesNotContain("native-connection", candidate.Handle.Value));
    }

    [Fact]
    public void HandleFromAnotherSessionDoesNotResolve()
    {
        var firstCatalog = BuildCatalog(
            CreatePackageOverview(Guid.NewGuid()),
            connections: new[] { new ConnectionOverview("Warehouse", "connection-1", "OLEDB") });
        var secondCatalog = BuildCatalog(
            CreatePackageOverview(Guid.NewGuid()),
            connections: new[] { new ConnectionOverview("Warehouse", "connection-1", "OLEDB") });
        var handle = firstCatalog.FindByName("Warehouse", SemanticObjectKind.Connection).ResolvedObject.Handle;

        Assert.Equal(SemanticHandleResolutionStatus.NotFound, secondCatalog.Resolve(handle).Status);
    }

    [Fact]
    public void MetadataSearchReturnsDuplicateNamesAndSupportsKindAndNativeIdQueries()
    {
        var package = CreatePackageOverview(Guid.NewGuid());
        var catalog = BuildCatalog(package, connections: new[]
        {
            new ConnectionOverview("Warehouse", "connection-1", "OLEDB"),
            new ConnectionOverview("Warehouse", "connection-2", "OLEDB")
        }, executables: new[] { new ExecutableOverview("task-1", "", "Warehouse load", "SQL", "", 0, false, false) });
        Assert.Equal(3, catalog.Search("WAREHOUSE").Count);
        Assert.Equal(2, catalog.Search("warehouse", SemanticObjectKind.Connection).Count);
        var native = Assert.Single(catalog.Search("connection-2"));
        Assert.Equal(new[] { "connection-2" }, catalog.GetNativeIds(native.Handle));
        Assert.Empty(catalog.Search("not-present"));
        Assert.Throws<ArgumentException>(() => catalog.Search(" "));
    }

    [Fact]
    public void PackageSearchBoundsResultsAndNeverEchoesTheQuery()
    {
        var package = CreatePackageOverview(Guid.NewGuid());
        var catalog = BuildCatalog(package, connections: Enumerable.Range(0, 75).Select(index =>
            new ConnectionOverview("Warehouse " + index, "connection-" + index, "OLEDB")));
        var search = new PackageSearchResult(package, catalog, "Warehouse", SemanticObjectKind.Connection);
        Assert.Equal(75, search.TotalMatches);
        Assert.Equal(50, search.Matches.Count);
        Assert.Equal(25, search.MatchesOmitted);
        Assert.Throws<ArgumentException>(() => new PackageSearchResult(CreatePackageOverview(Guid.NewGuid()), catalog, "Warehouse"));
    }

    private static SemanticHandleCatalog BuildCatalog(
        PackageOverview package,
        IEnumerable<ConnectionOverview>? connections = null,
        IEnumerable<VariableOverview>? variables = null,
        IEnumerable<ParameterOverview>? parameters = null,
        IEnumerable<ExecutableOverview>? executables = null,
        IEnumerable<DataFlowOverview>? dataFlows = null)
    {
        return new SemanticHandleCatalogBuilder().Build(
            package,
            connections ?? Array.Empty<ConnectionOverview>(),
            variables ?? Array.Empty<VariableOverview>(),
            parameters ?? Array.Empty<ParameterOverview>(),
            executables ?? Array.Empty<ExecutableOverview>(),
            dataFlows ?? Array.Empty<DataFlowOverview>());
    }

    private static PackageOverview CreatePackageOverview(Guid sessionId)
    {
        return new PackageOverview(
            sessionId,
            "Package",
            "native-package-id",
            string.Empty,
            DateTime.UnixEpoch,
            1,
            0,
            0,
            "EncryptSensitiveWithUserKey",
            "Default",
            0,
            0,
            0,
            0,
            0,
            false);
    }
}