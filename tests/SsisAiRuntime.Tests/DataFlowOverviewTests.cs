using SsisAiRuntime.Inspectors;

namespace SsisAiRuntime.Tests;

public class DataFlowOverviewTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ColumnTraceFollowsPortsAndLineageRatherThanNames(bool upstream)
    {
        var source = Column("source", "out", "s", "Output", 42, "Original");
        var destination = Column("target", "in", "t", "Input", 42, "Renamed");
        var wrongPort = Column("target", "other-in", "other", "Input", 42, "Original");
        var flow = new DataFlowOverview("flow", "Flow", new[]
        {
            Component("source", Array.Empty<DataFlowColumnOverview>(), new[] { source }),
            Component("target", new[] { destination, wrongPort }, Array.Empty<DataFlowColumnOverview>())
        }, new[] { new DataFlowPathOverview("path", "Path", "source", "out", "target", "in") });
        var result = new ColumnLineageQuery().Trace(flow, upstream ? "target" : "source", upstream ? "t" : "s", upstream);
        Assert.True(result.IsComplete);
        Assert.Equal(2, Assert.Single(result.Items).Columns.Count);
        Assert.Equal("path", Assert.Single(result.Items[0].Links).PathId);
        Assert.DoesNotContain(wrongPort, result.Items[0].Columns);
    }

    [Fact]
    public void ColumnTraceStopsAtOpaqueTransformationWithoutMatchingByName()
    {
        var input = Column("transform", "in", "i", "Input", 42, "SameName");
        var output = Column("transform", "out", "o", "Output", 43, "SameName");
        var flow = new DataFlowOverview("flow", "Flow", new[] { Component("transform", new[] { input }, new[] { output }) }, Array.Empty<DataFlowPathOverview>());
        var result = new ColumnLineageQuery().Trace(flow, "transform", "i");
        Assert.False(result.IsComplete);
        Assert.Single(result.Items[0].Columns);
        Assert.Empty(result.Items[0].Links);
        Assert.Equal(UnsupportedItem.UnsupportedMetadataCode, Assert.Single(result.UnsupportedItems).ReasonCode);
    }

    [Fact]
    public void ColumnTraceHandlesBranchesAndCyclesAndRejectsMissingSelection()
    {
        var input = Column("component", "in", "i", "Input", 42, "Value");
        var output = Column("component", "out", "o", "Output", 42, "Value");
        var branch = Column("target", "in", "t", "Input", 42, "Value");
        var flow = new DataFlowOverview("flow", "Flow", new[]
        {
            Component("component", new[] { input }, new[] { output }),
            Component("target", new[] { branch }, Array.Empty<DataFlowColumnOverview>())
        }, new[]
        {
            new DataFlowPathOverview("cycle", "Cycle", "component", "out", "component", "in"),
            new DataFlowPathOverview("branch", "Branch", "component", "out", "target", "in")
        });
        var result = new ColumnLineageQuery().Trace(flow, "component", "o");
        Assert.Equal(3, result.Items[0].Columns.Count);
        Assert.Equal(3, result.Items[0].Links.Count);
        Assert.Empty(new ColumnLineageQuery().Trace(flow, "component", "missing").Items);
    }

    [Fact]
    public void ColumnTraceReportsAnUnmappedBranchEvenWhenAnotherBranchMatches()
    {
        var source = Column("source", "out", "s", "Output", 42, "Value");
        var matched = Column("target", "in", "t", "Input", 42, "Value");
        var unmapped = Column("other", "in", "u", "Input", 43, "Value");
        var flow = new DataFlowOverview("flow", "Flow", new[]
        {
            Component("source", Array.Empty<DataFlowColumnOverview>(), new[] { source }),
            Component("target", new[] { matched }, Array.Empty<DataFlowColumnOverview>()),
            Component("other", new[] { unmapped }, Array.Empty<DataFlowColumnOverview>())
        }, new[]
        {
            new DataFlowPathOverview("matched", "Matched", "source", "out", "target", "in"),
            new DataFlowPathOverview("unmapped", "Unmapped", "source", "out", "other", "in")
        });
        var result = new ColumnLineageQuery().Trace(flow, "source", "s");
        Assert.False(result.IsComplete);
        Assert.Equal(2, result.Items[0].Columns.Count);
        Assert.Single(result.Items[0].Links);
        Assert.Single(result.UnsupportedItems);
    }

    [Fact]
    public void ColumnTraceRejectsAmbiguousSelectionsAndNeverLinksZeroLineage()
    {
        var input = Column("component", "in", "duplicate", "Input", 42, "Value");
        var output = Column("component", "out", "duplicate", "Output", 42, "Value");
        var ambiguous = new DataFlowOverview("flow", "Flow", new[] { Component("component", new[] { input }, new[] { output }) }, Array.Empty<DataFlowPathOverview>());
        Assert.Empty(new ColumnLineageQuery().Trace(ambiguous, "component", "duplicate").Items);

        var source = Column("source", "out", "s", "Output", 0, "Value");
        var target = Column("target", "in", "t", "Input", 0, "Value");
        var flow = new DataFlowOverview("flow", "Flow", new[]
        {
            Component("source", Array.Empty<DataFlowColumnOverview>(), new[] { source }),
            Component("target", new[] { target }, Array.Empty<DataFlowColumnOverview>())
        }, new[] { new DataFlowPathOverview("path", "Path", "source", "out", "target", "in") });
        var result = new ColumnLineageQuery().Trace(flow, "source", "s");
        Assert.False(result.IsComplete);
        Assert.Single(result.Items[0].Columns);
        Assert.Empty(result.Items[0].Links);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ColumnTraceFollowsExplicitSynchronousOutputWithoutInventingColumns(bool upstream)
    {
        var source = Column("source", "out", "s", "Output", 42, "Value");
        var input = Column("transform", "in", "i", "Input", 42, "Value");
        var target = Column("target", "in", "t", "Input", 42, "Value");
        var transform = new DataFlowComponentOverview("transform", "Transform", "Vendor.Transform", "", 1, 1,
            new[] { input }, Array.Empty<DataFlowColumnOverview>(), Array.Empty<DataFlowColumnOverview>(),
            Array.Empty<DataFlowRuntimeConnectionOverview>(), Array.Empty<DataFlowSettingOverview>(),
            new[] { new DataFlowOutputOverview("sync-out", "Output", "in", false) });
        var flow = new DataFlowOverview("flow", "Flow", new[]
        {
            Component("source", Array.Empty<DataFlowColumnOverview>(), new[] { source }),
            transform,
            Component("target", new[] { target }, Array.Empty<DataFlowColumnOverview>())
        }, new[]
        {
            new DataFlowPathOverview("before", "Before", "source", "out", "transform", "in"),
            new DataFlowPathOverview("after", "After", "transform", "sync-out", "target", "in")
        });
        var result = new ColumnLineageQuery().Trace(flow, upstream ? "target" : "source", upstream ? "t" : "s", upstream);
        Assert.True(result.IsComplete);
        Assert.Equal(3, result.Items[0].Columns.Count);
        Assert.Equal(2, result.Items[0].Links.Count);
        var bridge = Assert.Single(result.Items[0].Links, link => link.Kind == "SynchronousPassThrough");
        Assert.Same(input, bridge.Source);
        Assert.Same(target, bridge.Target);
        Assert.Equal("sync-out", bridge.SynchronousOutputId);
    }

    [Fact]
    public void ColumnTraceTreatsAnUnconnectedKnownErrorOutputAsATerminalPort()
    {
        var input = Column("target", "in", "t", "Input", 42, "Value");
        var target = new DataFlowComponentOverview("target", "Target", "Vendor.Target", "", 1, 1,
            new[] { input }, Array.Empty<DataFlowColumnOverview>(), Array.Empty<DataFlowColumnOverview>(),
            Array.Empty<DataFlowRuntimeConnectionOverview>(), Array.Empty<DataFlowSettingOverview>(),
            new[] { new DataFlowOutputOverview("error-out", "Error", "in", true) });
        var flow = new DataFlowOverview("flow", "Flow", new[] { target }, Array.Empty<DataFlowPathOverview>());
        Assert.True(new ColumnLineageQuery().Trace(flow, "target", "t").IsComplete);
    }

    [Fact]
    public void ColumnTraceDoesNotInferPassThroughForAnAsynchronousOutput()
    {
        var input = Column("transform", "in", "i", "Input", 42, "Value");
        var targetColumn = Column("target", "in", "t", "Input", 42, "Value");
        var transform = new DataFlowComponentOverview("transform", "Transform", "Vendor.Transform", "", 1, 1,
            new[] { input }, Array.Empty<DataFlowColumnOverview>(), Array.Empty<DataFlowColumnOverview>(),
            Array.Empty<DataFlowRuntimeConnectionOverview>(), Array.Empty<DataFlowSettingOverview>(),
            new[] { new DataFlowOutputOverview("async-out", "Output", "", false) });
        var flow = new DataFlowOverview("flow", "Flow", new[]
        {
            transform,
            Component("target", new[] { targetColumn }, Array.Empty<DataFlowColumnOverview>())
        }, new[] { new DataFlowPathOverview("path", "Path", "transform", "async-out", "target", "in") });
        var result = new ColumnLineageQuery().Trace(flow, "transform", "i");
        Assert.False(result.IsComplete);
        Assert.Single(result.Items[0].Columns);
        Assert.Empty(result.Items[0].Links);
    }

    [Fact]
    public void ColumnTraceIncludesUnselectedVirtualBufferColumns()
    {
        var source = Column("source", "out", "s", "Output", 42, "Value");
        var buffer = Column("transform", "in", "virtual:in:42", "VirtualInput", 42, "Value");
        var target = Column("target", "in", "t", "Input", 42, "Value");
        var transform = new DataFlowComponentOverview("transform", "Transform", "Vendor.Transform", "", 1, 1,
            Array.Empty<DataFlowColumnOverview>(), Array.Empty<DataFlowColumnOverview>(), Array.Empty<DataFlowColumnOverview>(),
            Array.Empty<DataFlowRuntimeConnectionOverview>(), Array.Empty<DataFlowSettingOverview>(),
            new[] { new DataFlowOutputOverview("sync-out", "Output", "in", false) }, new[] { buffer });
        var flow = new DataFlowOverview("flow", "Flow", new[]
        {
            Component("source", Array.Empty<DataFlowColumnOverview>(), new[] { source }), transform,
            Component("target", new[] { target }, Array.Empty<DataFlowColumnOverview>())
        }, new[]
        {
            new DataFlowPathOverview("before", "Before", "source", "out", "transform", "in"),
            new DataFlowPathOverview("after", "After", "transform", "sync-out", "target", "in")
        });
        var result = new ColumnLineageQuery().Trace(flow, "source", "s");
        Assert.True(result.IsComplete);
        Assert.Contains(buffer, result.Items[0].Columns);
        Assert.Equal(3, result.Items[0].Columns.Count);
    }

    [Fact]
    public void ColumnTraceResolvesVirtualAliasesToSelectedInputColumns()
    {
        var selected = Column("component", "in", "selected", "Input", 42, "Value");
        var buffer = Column("component", "in", "virtual:in:42", "VirtualInput", 42, "Value");
        var component = new DataFlowComponentOverview("component", "Component", "Vendor.Component", "", 1, 0,
            new[] { selected }, Array.Empty<DataFlowColumnOverview>(), Array.Empty<DataFlowColumnOverview>(),
            Array.Empty<DataFlowRuntimeConnectionOverview>(), Array.Empty<DataFlowSettingOverview>(),
            Array.Empty<DataFlowOutputOverview>(), new[] { buffer });
        var flow = new DataFlowOverview("flow", "Flow", new[] { component }, Array.Empty<DataFlowPathOverview>());
        var result = new ColumnLineageQuery().Trace(flow, "component", buffer.Id);
        Assert.True(result.IsComplete);
        Assert.Same(selected, Assert.Single(result.Items[0].Columns));
    }

    [Fact]
    public void ColumnTraceReportsDanglingSynchronousInputReferences()
    {
        var input = Column("transform", "in", "i", "Input", 42, "Value");
        var target = Column("target", "in", "t", "Input", 42, "Value");
        var transform = new DataFlowComponentOverview("transform", "Transform", "Vendor.Transform", "", 1, 1,
            new[] { input }, Array.Empty<DataFlowColumnOverview>(), Array.Empty<DataFlowColumnOverview>(),
            Array.Empty<DataFlowRuntimeConnectionOverview>(), Array.Empty<DataFlowSettingOverview>(),
            new[] { new DataFlowOutputOverview("out", "Output", "missing-input", false) });
        var flow = new DataFlowOverview("flow", "Flow", new[]
        {
            transform, Component("target", new[] { target }, Array.Empty<DataFlowColumnOverview>())
        }, new[] { new DataFlowPathOverview("path", "Path", "transform", "out", "target", "in") });
        var result = new ColumnLineageQuery().Trace(flow, "transform", "i");
        Assert.False(result.IsComplete);
        Assert.Empty(result.Items[0].Links);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ColumnTraceFollowsExplicitMappingsAcrossChangedLineage(bool upstream)
    {
        var source = Column("convert", "in", "s", "Input", 42, "Original");
        var converted = new DataFlowColumnOverview("convert", "out", "Output", "Output", "c", "DifferentName", "String",
            30, 0, 0, 0, 73, 0, "", 42);
        var target = Column("target", "in", "t", "Input", 73, "Destination");
        var component = new DataFlowComponentOverview("convert", "Convert", "Vendor.Convert", "", 1, 1,
            new[] { source }, new[] { converted }, Array.Empty<DataFlowColumnOverview>(),
            Array.Empty<DataFlowRuntimeConnectionOverview>(), Array.Empty<DataFlowSettingOverview>(),
            new[] { new DataFlowOutputOverview("out", "Output", "in", false) });
        var flow = new DataFlowOverview("flow", "Flow", new[]
        {
            component, Component("target", new[] { target }, Array.Empty<DataFlowColumnOverview>())
        }, new[] { new DataFlowPathOverview("path", "Path", "convert", "out", "target", "in") });
        var result = new ColumnLineageQuery().Trace(flow, upstream ? "target" : "convert", upstream ? "t" : "s", upstream);
        Assert.True(result.IsComplete);
        Assert.Equal(3, result.Items[0].Columns.Count);
        Assert.Equal(2, result.Items[0].Links.Count);
        var mapping = Assert.Single(result.Items[0].Links, link => link.Kind == "ExplicitMapping");
        Assert.Same(source, mapping.Source);
        Assert.Same(converted, mapping.Target);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ColumnTraceDoesNotGuessMissingOrAmbiguousMappingSources(bool ambiguous)
    {
        var first = Column("convert", "in", "first", "Input", ambiguous ? 42 : 99, "Value");
        var second = Column("convert", "other", "second", "Input", 42, "Value");
        var converted = new DataFlowColumnOverview("convert", "out", "Output", "Output", "c", "Value", "String",
            30, 0, 0, 0, 73, 0, "", 42);
        var component = Component("convert", ambiguous ? new[] { first, second } : new[] { first }, new[] { converted });
        var flow = new DataFlowOverview("flow", "Flow", new[] { component }, Array.Empty<DataFlowPathOverview>());
        var result = new ColumnLineageQuery().Trace(flow, "convert", "c", true);
        Assert.False(result.IsComplete);
        Assert.Empty(result.Items[0].Links);
        Assert.Single(result.Items[0].Columns);
    }

    [Fact]
    public void InvalidExplicitMappingCannotFallBackToCoincidentalLineageIdentity()
    {
        var source = Column("convert", "in", "s", "Input", 42, "Value");
        var converted = new DataFlowColumnOverview("convert", "out", "Output", "Output", "c", "Value", "String",
            30, 0, 0, 0, 42, 0, "", 99);
        var flow = new DataFlowOverview("flow", "Flow", new[] { Component("convert", new[] { source }, new[] { converted }) }, Array.Empty<DataFlowPathOverview>());
        var result = new ColumnLineageQuery().Trace(flow, "convert", "c", true);
        Assert.False(result.IsComplete);
        Assert.Empty(result.Items[0].Links);
        Assert.Single(result.Items[0].Columns);
        Assert.Throws<ArgumentOutOfRangeException>(() => new DataFlowColumnOverview("", "", "", "Output", "", "", "",
            0, 0, 0, 0, 0, 0, "", 0));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void ExpressionTracesDistinguishResolvedReferencesConstantsAndFailures(bool resolved, bool constant)
    {
        var first = Column("derive", "in", "first", "Input", 42, "First");
        var second = Column("derive", "in", "second", "Input", 43, "Second");
        var ids = resolved && !constant ? new[] { 42, 43 } : Array.Empty<int>();
        var output = new DataFlowColumnOverview("derive", "out", "Output", "Output", "derived", "Derived", "Int32",
            4, 0, 0, 0, 70, 0, "", null, new DataFlowExpressionDependencies(resolved, ids));
        var flow = new DataFlowOverview("flow", "Flow", new[] { Component("derive", new[] { first, second }, new[] { output }) }, Array.Empty<DataFlowPathOverview>());
        var result = new ColumnLineageQuery().Trace(flow, "derive", "derived", true);
        Assert.Equal(resolved, result.IsComplete);
        Assert.Equal(resolved && !constant ? 3 : 1, result.Items[0].Columns.Count);
        Assert.Equal(resolved && !constant ? 2 : 0, result.Items[0].Links.Count);
        Assert.All(result.Items[0].Links, link => Assert.Equal("ExpressionResolved", link.Kind));
    }

    [Fact]
    public void PartialExpressionResolutionNeverClaimsCompleteCoverage()
    {
        var source = Column("derive", "in", "source", "Input", 42, "Source");
        var output = new DataFlowColumnOverview("derive", "out", "Output", "Output", "derived", "Derived", "Int32",
            4, 0, 0, 0, 70, 0, "", null, new DataFlowExpressionDependencies(true, new[] { 42, 99 }));
        var flow = new DataFlowOverview("flow", "Flow", new[] { Component("derive", new[] { source }, new[] { output }) }, Array.Empty<DataFlowPathOverview>());
        var result = new ColumnLineageQuery().Trace(flow, "derive", "derived", true);
        Assert.False(result.IsComplete);
        Assert.Single(result.Items[0].Links);
        Assert.Throws<ArgumentException>(() => new DataFlowExpressionDependencies(false, new[] { 42 }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReplacementTracesUseOriginalInputsAndDoNotInventSelfReferences(bool constant)
    {
        var original = Column("derive", "in", "original", "Input", 42, "Value");
        var adjustment = Column("derive", "in", "adjustment", "Input", 43, "Adjustment");
        var replacement = new DataFlowColumnOverview("derive", "out", "Output", "Output", "replaced:original:out", "Value", "Int32",
            4, 0, 0, 0, 42, 0, "Replacement", null, new DataFlowExpressionDependencies(true, constant ? Array.Empty<int>() : new[] { 42, 43 }), true);
        var target = Column("target", "in", "target", "Input", 42, "Value");
        var derive = new DataFlowComponentOverview("derive", "Derive", "Vendor.Derive", "", 1, 1,
            new[] { original, adjustment }, new[] { replacement }, Array.Empty<DataFlowColumnOverview>(),
            Array.Empty<DataFlowRuntimeConnectionOverview>(), Array.Empty<DataFlowSettingOverview>(),
            new[] { new DataFlowOutputOverview("out", "Output", "in", false) });
        var flow = new DataFlowOverview("flow", "Flow", new[] { derive, Component("target", new[] { target }, Array.Empty<DataFlowColumnOverview>()) },
            new[] { new DataFlowPathOverview("path", "Path", "derive", "out", "target", "in") });
        var upstream = new ColumnLineageQuery().Trace(flow, "target", "target", true);
        Assert.True(upstream.IsComplete);
        Assert.Contains(replacement, upstream.Items[0].Columns);
        Assert.Equal(constant ? 0 : 2, upstream.Items[0].Links.Count(link => link.Kind == "ExpressionResolved"));
        Assert.DoesNotContain(upstream.Items[0].Links, link => ReferenceEquals(link.Source, link.Target));
        var downstream = new ColumnLineageQuery().Trace(flow, "derive", "original");
        Assert.True(downstream.IsComplete);
        Assert.Equal(constant ? 1 : 3, downstream.Items[0].Columns.Count);
        Assert.DoesNotContain(downstream.Items[0].Links, link => link.Kind == "SynchronousPassThrough");
    }

    private static DataFlowColumnOverview Column(string component, string port, string id, string direction, int lineage, string name) =>
        new(component, port, port, direction, id, name, "Int32", 4, 0, 0, 0, lineage, 0, "");

    private static DataFlowComponentOverview Component(string id, DataFlowColumnOverview[] inputs, DataFlowColumnOverview[] outputs) =>
        new(id, id, "Vendor.Component", "", inputs.Length == 0 ? 0 : 1, outputs.Length == 0 ? 0 : 1,
            inputs, outputs, Array.Empty<DataFlowColumnOverview>(), Array.Empty<DataFlowRuntimeConnectionOverview>(), Array.Empty<DataFlowSettingOverview>());

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