using SsisAiRuntime.Inspectors;

namespace SsisAiRuntime.Tests;

public class ExecutableOverviewTests
{
    [Fact]
    public void OverviewPreservesExecutableIdentityAndHierarchy()
    {
        var overview = new ExecutableOverview(
            "native-id",
            "parent-id",
            "Unknown task",
            "Custom.UnknownTask, Vendor.Component",
            "Task description",
            2,
            false,
            true);

        Assert.Equal("native-id", overview.Id);
        Assert.Equal("parent-id", overview.ParentId);
        Assert.Equal("Unknown task", overview.Name);
        Assert.Equal("Custom.UnknownTask, Vendor.Component", overview.CreationName);
        Assert.Equal("Task description", overview.Description);
        Assert.Equal(2, overview.Depth);
        Assert.False(overview.IsContainer);
        Assert.True(overview.HasExpressions);
    }

    [Fact]
    public void InspectionResultReportsCompletenessAndUnsupportedItems()
    {
        var overview = new ExecutableOverview(
            "task-id",
            "parent-id",
            "Task",
            "Vendor.Task",
            string.Empty,
            1,
            false,
            false);
        var unsupported = new UnsupportedItem(
            "task-id",
            "Task",
            "Vendor.Task",
            "Task-specific properties are not inspected yet.");

        var complete = InspectionResult<ExecutableOverview>.Complete(new[] { overview });
        var incomplete = new InspectionResult<ExecutableOverview>(new[] { overview }, new[] { unsupported });

        Assert.True(complete.IsComplete);
        Assert.Empty(complete.UnsupportedItems);
        Assert.False(incomplete.IsComplete);
        Assert.Same(unsupported, Assert.Single(incomplete.UnsupportedItems));
        Assert.Same(overview, Assert.Single(incomplete.Items));
    }
}