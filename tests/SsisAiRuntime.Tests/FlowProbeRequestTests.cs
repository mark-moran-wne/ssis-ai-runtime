using Newtonsoft.Json.Linq;
using SsisAiRuntime.FlowRunner;

namespace SsisAiRuntime.Tests;

public sealed class FlowProbeRequestTests
{
    [Fact]
    public void RequestRoundTripsAndCollectionsAreReadOnly()
    {
        var request = FlowProbeRequest.Read(new StringReader(Valid().ToString()));
        var restored = FlowProbeRequest.Read(new StringReader(request.ToJson()));

        Assert.Equal(new[] { -2, 0, 3 }, restored.Values);
        Assert.Equal(new[] { -4, 0, 6 }, restored.ExpectedValues);
        Assert.Equal("Value * 2", restored.Expression);
        Assert.Equal(request.ToJson(), restored.ToJson());
        Assert.Throws<NotSupportedException>(() => ((IList<int>)request.Values).Clear());
    }

    [Theory]
    [InlineData("schemaVersion", "2.0")]
    [InlineData("expression", " ")]
    [InlineData("connectionString", "fixture-secret")]
    public void UnknownVersionsEmptyExpressionsAndExtraFieldsAreRejected(string field, string value)
    {
        var document = Valid();
        document[field] = value;
        Assert.Throws<ArgumentException>(() => FlowProbeRequest.Read(new StringReader(document.ToString())));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[1.5, 2, 3]")]
    [InlineData("[\"1\", 2, 3]")]
    [InlineData("[null, 2, 3]")]
    public void InvalidRowTypesAndEmptyRowsAreRejected(string rows)
    {
        var document = Valid();
        document["values"] = JArray.Parse(rows);
        Assert.Throws<ArgumentException>(() => FlowProbeRequest.Read(new StringReader(document.ToString())));
    }

    [Fact]
    public void BoundsAndDuplicatePropertiesAreRejected()
    {
        Assert.Throws<ArgumentException>(() => FlowProbeRequest.Read(new StringReader(new string(' ', 65537))));
        var tooManyRows = Valid();
        tooManyRows["values"] = new JArray(Enumerable.Range(0, 1001));
        Assert.Throws<ArgumentException>(() => FlowProbeRequest.Read(new StringReader(tooManyRows.ToString())));
        var tooLongExpression = Valid();
        tooLongExpression["expression"] = new string('X', 4097);
        Assert.Throws<ArgumentException>(() => FlowProbeRequest.Read(new StringReader(tooLongExpression.ToString())));
        Assert.Throws<Newtonsoft.Json.JsonReaderException>(() => FlowProbeRequest.Read(new StringReader(
            "{\"schemaVersion\":\"1.0\",\"schemaVersion\":\"2.0\"}")));
    }

    [Fact]
    public void ExpectedRowsMustMatchInputCountAndUseInt32()
    {
        var mismatch = Valid();
        mismatch["expectedValues"] = new JArray(1);
        Assert.Throws<ArgumentException>(() => FlowProbeRequest.Read(new StringReader(mismatch.ToString())));
        var outOfRange = Valid();
        outOfRange["values"]![0] = long.MaxValue;
        Assert.Throws<OverflowException>(() => FlowProbeRequest.Read(new StringReader(outOfRange.ToString())));
    }

    [Fact]
    public void DataConversionRequestsRoundTripAndRejectUnsupportedTypes()
    {
        var document = new JObject
        {
            ["schemaVersion"] = "1.1", ["recipe"] = "data-conversion",
            ["values"] = new JArray(-2, 0, 3), ["conversionType"] = "Int16",
            ["expectedValues"] = new JArray(-2, 0, 3)
        };
        var request = FlowProbeRequest.Read(new StringReader(document.ToString()));
        Assert.Equal("data-conversion", request.Recipe);
        Assert.Equal("Int16", FlowProbeRequest.Read(new StringReader(request.ToJson())).ConversionType);
        document["conversionType"] = "ArbitraryType";
        Assert.Throws<ArgumentException>(() => FlowProbeRequest.Read(new StringReader(document.ToString())));
    }

    private static JObject Valid() => new()
    {
        ["schemaVersion"] = "1.0",
        ["values"] = new JArray(-2, 0, 3),
        ["expression"] = "Value * 2",
        ["expectedValues"] = new JArray(-4, 0, 6)
    };

    [Fact]
    public void TextWidthRequestsRoundTripAndRejectShrinkingOrInvalidCsvRows()
    {
        var document = new JObject
        {
            ["schemaVersion"] = "1.1", ["recipe"] = "flat-file-text",
            ["values"] = new JArray("Long value"), ["expectedValues"] = new JArray("Long value"),
            ["sourceWidth"] = 4, ["destinationWidth"] = 4, ["widenTo"] = 64
        };
        var request = FlowProbeRequest.Read(new StringReader(document.ToString()));
        var restored = FlowProbeRequest.Read(new StringReader(request.ToJson()));
        Assert.Equal(64, restored.WidenTo);
        Assert.Equal("Long value", Assert.Single(restored.TextValues));
        Assert.Throws<NotSupportedException>(() => ((IList<string>)restored.TextValues).Clear());
        document["widenTo"] = 3;
        Assert.Throws<ArgumentException>(() => FlowProbeRequest.Read(new StringReader(document.ToString())));
        document["widenTo"] = 64;
        document["values"] = new JArray("unexpected,second-column");
        Assert.Throws<ArgumentException>(() => FlowProbeRequest.Read(new StringReader(document.ToString())));
        document["values"] = new JArray("Long value");
        document["sourceWidth"] = 4001;
        Assert.Throws<ArgumentException>(() => FlowProbeRequest.Read(new StringReader(document.ToString())));
    }
}