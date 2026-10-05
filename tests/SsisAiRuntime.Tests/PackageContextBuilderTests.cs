using SsisAiRuntime.Inspectors;

namespace SsisAiRuntime.Tests;

public class PackageContextBuilderTests
{
    [Fact]
    public void SqlContextContainsOnlySqlProjectionAndPropagatesUnsupportedItems()
    {
        var package = CreatePackageOverview();
        var statement = new SqlStatementOverview(
            "task-id",
            "Load rows",
            "ExecuteSQLTask",
            "DirectInput",
            "SELECT '<redacted>'",
            true,
            "connection-id",
            "Warehouse",
            2);
        var unsupported = new UnsupportedItem("task-id", "Load rows", "ExecuteSQLTask", "Binding metadata is incomplete.");
        var sql = new InspectionResult<SqlStatementOverview>(new[] { statement }, new[] { unsupported });

        var context = new PackageContextBuilder().BuildSql(package, sql);

        Assert.Equal(PackageContextKind.Sql, context.Kind);
        Assert.Same(package, context.Package);
        Assert.Same(statement, Assert.Single(context.SqlStatements));
        Assert.Empty(context.DataFlows);
        Assert.Empty(context.Connections);
        Assert.False(context.IsComplete);
        Assert.Same(unsupported, Assert.Single(context.UnsupportedItems));
    }

    [Fact]
    public void LineageContextIncludesOnlyDataFlows()
    {
        var package = CreatePackageOverview();
        var flow = new DataFlowOverview("flow-id", "Load", Array.Empty<DataFlowComponentOverview>(), Array.Empty<DataFlowPathOverview>());

        var context = new PackageContextBuilder().BuildLineage(
            package,
            InspectionResult<DataFlowOverview>.Complete(new[] { flow }));

        Assert.Equal(PackageContextKind.Lineage, context.Kind);
        Assert.Same(flow, Assert.Single(context.DataFlows));
        Assert.Empty(context.SqlStatements);
        Assert.True(context.IsComplete);
    }

    [Fact]
    public void ConfigurationContextCombinesCoverageAndSafeMetadata()
    {
        var package = CreatePackageOverview();
        var connection = new ConnectionOverview("Warehouse", "connection-id", "OLEDB");
        var variable = new VariableOverview("Endpoint", "User", "String", false, false, false, false);
        var parameter = new ParameterOverview("parameter-id", "Mode", "PackageParameter", string.Empty, "String", false, false);
        var expression = new ExpressionOverview("Variable", "variable-id", "Computed", "UserVariable");
        var incompleteExpressions = new InspectionResult<ExpressionOverview>(
            new[] { expression },
            new[] { new UnsupportedItem("variable-id", "Computed", "UserVariable", "Expression text omitted.") });

        var context = new PackageContextBuilder().BuildConfiguration(
            package,
            InspectionResult<ConnectionOverview>.Complete(new[] { connection }),
            InspectionResult<VariableOverview>.Complete(new[] { variable }),
            InspectionResult<ParameterOverview>.Complete(new[] { parameter }),
            incompleteExpressions);

        Assert.Equal(PackageContextKind.Configuration, context.Kind);
        Assert.Single(context.Connections);
        Assert.Single(context.Variables);
        Assert.Single(context.Parameters);
        Assert.Single(context.Expressions);
        Assert.False(context.IsComplete);
        Assert.Single(context.UnsupportedItems);
    }

    private static PackageOverview CreatePackageOverview()
    {
        return new PackageOverview(
            Guid.NewGuid(),
            "Package",
            "package-id",
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