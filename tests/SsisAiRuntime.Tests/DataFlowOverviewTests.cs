using SsisAiRuntime.Inspectors;

namespace SsisAiRuntime.Tests;

public class DataFlowOverviewTests
{
    [Fact]
    public void OverviewRetainsComponentAndPathTopology()
    {
        var component = new DataFlowComponentOverview(
            "component-id",
            "Source",
            "Vendor.Source",
            "Source component",
            0,
            1);
        var path = new DataFlowPathOverview(
            "path-id",
            "Source to Destination",
            "component-id",
            "output-id",
            "destination-id",
            "input-id");
        var overview = new DataFlowOverview("flow-id", "Data Flow", new[] { component }, new[] { path });

        Assert.Equal("flow-id", overview.ExecutableId);
        Assert.Equal("Data Flow", overview.ExecutableName);
        Assert.Same(component, Assert.Single(overview.Components));
        Assert.Same(path, Assert.Single(overview.Paths));
    }

    [Fact]
    public void ComponentRetainsColumnLineageAndSanitizedSettingMetadata()
    {
        var inputColumn = new DataFlowColumnOverview(
            "component-id",
            "input-id",
            "Input",
            "Input",
            "column-id",
            "OrderId",
            "Int32",
            4,
            0,
            0,
            0,
            42,
            7,
            "ReadOnly");
        var outputColumn = new DataFlowColumnOverview(
            "component-id",
            "output-id",
            "Output",
            "Output",
            "output-column-id",
            "OrderId",
            "Int32",
            4,
            0,
            0,
            0,
            42,
            7,
            string.Empty);
        var externalColumn = new DataFlowColumnOverview(
            "component-id",
            "input-id",
            "Input",
            "InputExternalMetadata",
            "external-column-id",
            "OrderId",
            "Int32",
            4,
            0,
            0,
            0,
            0,
            7,
            "ExternalMetadata");
        var setting = new DataFlowSettingOverview("Component", "component-id", "SqlCommand", "SELECT <redacted>", true);
        var runtimeConnection = new DataFlowRuntimeConnectionOverview("runtime-connection-id", "Source", "package-connection-id");
        var component = new DataFlowComponentOverview(
            "component-id",
            "Source",
            "Vendor.Source",
            string.Empty,
            1,
            1,
            new[] { inputColumn },
            new[] { outputColumn },
            new[] { externalColumn },
            new[] { runtimeConnection },
            new[] { setting });

        Assert.Equal(42, Assert.Single(component.InputColumns).LineageId);
        Assert.Equal(42, Assert.Single(component.OutputColumns).LineageId);
        Assert.Equal(7, Assert.Single(component.ExternalMetadataColumns).ExternalMetadataColumnId);
        Assert.Equal("package-connection-id", Assert.Single(component.RuntimeConnections).ConnectionManagerId);
        Assert.True(Assert.Single(component.Settings).ValueRedacted);
    }
}