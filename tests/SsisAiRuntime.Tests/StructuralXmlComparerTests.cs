using Newtonsoft.Json.Linq;
using SsisAiRuntime.FlowRunner;

namespace SsisAiRuntime.Tests;

public sealed class StructuralXmlComparerTests
{
    [Fact]
    public void FormattingPrefixesAndAttributeOrderDoNotCauseChanges()
    {
        var result = Compare("<a:flow xmlns:a='urn:test' width='4' name='Value'><a:column /></a:flow>",
            "<b:flow xmlns:b='urn:test' name='Value' width='4'>\n  <b:column />\n</b:flow>");
        Assert.True((bool)result["isMatch"]!);
        Assert.False((bool)result["semanticsVerified"]!);
    }

    [Fact]
    public void ChangedWidthsAddedColumnsAndRemovedMappingsAreReported()
    {
        var result = Compare("<flow><column width='4'/><mapping source='old'/></flow>",
            "<flow><column width='64'/><column width='8'/></flow>");
        var changes = (JArray)result["changes"]!;
        Assert.False((bool)result["isMatch"]!);
        Assert.Contains(changes, item => (string?)item["kind"] == "Changed" &&
            (string?)item["before"] == "4" && (string?)item["after"] == "64");
        Assert.Contains(changes, item => (string?)item["kind"] == "Added");
        Assert.Contains(changes, item => (string?)item["kind"] == "Removed");
    }

    [Fact]
    public void MeaningfulLeafWhitespaceAndGeneratedIdsAreNotHidden()
    {
        Assert.False((bool)Compare("<property> </property>", "<property></property>")["isMatch"]!);
        Assert.False((bool)Compare("<flow id='one'/>", "<flow id='two'/>")["isMatch"]!);
    }

    [Fact]
    public void DtdAndDeepXmlAreRejected()
    {
        Assert.Throws<System.Xml.XmlException>(() => Compare("<!DOCTYPE flow [<!ENTITY x 'value'>]><flow>&x;</flow>", "<flow/>"));
        var deep = string.Concat(Enumerable.Repeat("<node>", 66)) + string.Concat(Enumerable.Repeat("</node>", 66));
        Assert.Throws<InvalidDataException>(() => Compare(deep, "<flow/>"));
    }

    [Fact]
    public void LargeChangeListsKeepExactTotalsAndBoundExamples()
    {
        var after = "<flow>" + string.Concat(Enumerable.Range(0, 600).Select(index => "<column name='" + index + "'/>")) + "</flow>";
        var result = Compare("<flow/>", after);
        Assert.Equal(500, ((JArray)result["changes"]!).Count);
        Assert.Equal((int)result["changeCount"]! - 500, (int)result["changesOmitted"]!);
    }

    private static JObject Compare(string before, string after) => StructuralXmlComparer.Compare(new StringReader(new JObject
    {
        ["schemaVersion"] = "1.0", ["beforeXml"] = before, ["afterXml"] = after
    }.ToString()));
}