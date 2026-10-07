using Newtonsoft.Json.Linq;
using SsisAiRuntime.FlowRunner;

namespace SsisAiRuntime.Tests;

public sealed class SharedComponentCatalogTests
{
    [Fact]
    public void EmbeddedSharedDefinitionsLoadWithoutNativeSsis()
    {
        var catalog = SharedComponentCatalog.Load();

        Assert.True(catalog.Count >= 59);
        Assert.Contains(catalog, entry => (string?)entry["id"] == "microsoft.derived-column" &&
            (int)entry["ssisMajorVersion"]! == 16);
        Assert.Contains(catalog, entry => (string?)entry["vendor"] == "Attunity");
        Assert.All(catalog, entry => Assert.NotNull(entry["evidence"]));
    }

    [Theory]
    [InlineData("schemaVersion", "2.0")]
    [InlineData("id", " ")]
    [InlineData("componentType", "Unknown")]
    [InlineData("connectionString", "fixture-secret")]
    public void InvalidVersionIdentityTypeAndUnexpectedFieldsAreRejected(string field, string value)
    {
        var document = Definition();
        if (field == "schemaVersion") { document[field] = value; }
        else { document["components"]![0]![field] = value; }

        Assert.Throws<InvalidDataException>(() => SharedComponentCatalog.Parse(document.ToString()));
    }

    [Fact]
    public void DuplicateIdsAndCreationNamesWithinVersionAreRejected()
    {
        var document = Definition();
        var entries = (JArray)document["components"]!;
        var duplicate = entries[0].DeepClone();
        duplicate["creationName"] = "Example.Second.1";
        entries.Add(duplicate);
        Assert.Throws<InvalidDataException>(() => SharedComponentCatalog.Parse(document.ToString()));
        duplicate["id"] = "example.second";
        duplicate["creationName"] = "Example.First.1";
        Assert.Throws<InvalidDataException>(() => SharedComponentCatalog.Parse(document.ToString()));
    }

    [Fact]
    public void UnknownEvidenceAndMalformedMetadataAreRejected()
    {
        var document = Definition();
        document["components"]![0]!["evidence"] = new JArray("assumed-safe");
        Assert.Throws<InvalidDataException>(() => SharedComponentCatalog.Parse(document.ToString()));
        document["components"]![0]!["evidence"] = new JArray("documented");
        document["components"]![0]!["properties"] = new JArray(42);
        Assert.Throws<InvalidDataException>(() => SharedComponentCatalog.Parse(document.ToString()));
    }

    [Fact]
    public void ExecutionEvidenceRequiresRecipeButDoesNotEnableIt()
    {
        var document = Definition();
        document["components"]![0]!["evidence"] = new JArray("execution-tested");
        Assert.Throws<InvalidDataException>(() => SharedComponentCatalog.Parse(document.ToString()));
        document["components"]![0]!["recipe"] = "contributed-recipe";

        var definition = Assert.Single(SharedComponentCatalog.Parse(document.ToString()));
        Assert.Null(definition["configurable"]);
        Assert.Null(definition["executionTestedThisInvocation"]);
    }

    [Fact]
    public void ContributionsDefaultToDocumentedWithUnknownDetails()
    {
        var definition = Assert.Single(SharedComponentCatalog.Parse(Definition().ToString()));
        Assert.Equal(new[] { "documented" }, definition["evidence"]!.Values<string>());
        Assert.Null(definition["inputs"]);
        Assert.Null(definition["prerequisites"]);
    }

    private static JObject Definition() => new()
    {
        ["schemaVersion"] = "1.0",
        ["ssisMajorVersion"] = 16,
        ["components"] = new JArray(new JObject
        {
            ["id"] = "example.first",
            ["vendor"] = "Example",
            ["name"] = "First",
            ["creationName"] = "Example.First.1",
            ["componentType"] = "Transform"
        })
    };
}