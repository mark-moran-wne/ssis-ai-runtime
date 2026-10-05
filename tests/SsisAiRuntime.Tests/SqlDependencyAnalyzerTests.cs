using SsisAiRuntime.Inspectors.SqlDependencies;
using Newtonsoft.Json;

namespace SsisAiRuntime.Tests;

public sealed class SqlDependencyAnalyzerTests
{
    [Fact]
    public void ParsesSchemaObjectsExecuteAndFunctionsWithoutClassifyingTablesOrViews()
    {
        var result = new SqlDependencyAnalyzer().Analyze("select dbo.GetTerm() from dbo.Student join dbo.Address a on 1=1; exec dbo.LoadStudent;");
        Assert.True(result.IsComplete);
        Assert.Contains(result.References, item => item.QualifiedName == "[dbo].[Student]" && item.Evidence == SqlDependencyEvidence.ParsedSchemaObject);
        Assert.Contains(result.References, item => item.Access == SqlDependencyAccess.Execute && item.Evidence == SqlDependencyEvidence.ParsedExecuteTarget);
        Assert.Contains(result.References, item => item.Access == SqlDependencyAccess.Function);
    }

    [Fact]
    public void CommentsAndLiteralsAreNotDependencySources()
    {
        var result = new SqlDependencyAnalyzer().Analyze("-- select * from dbo.Secret\n select 'select * from dbo.Secret' /* exec dbo.Secret */;");
        Assert.True(result.IsComplete);
        Assert.Empty(result.References);
        Assert.DoesNotContain("Secret", JsonConvert.SerializeObject(result));
    }

    [Fact]
    public void CtesAreStatementScopedAndLocalTablesAreNotPersistentObjects()
    {
        var result = new SqlDependencyAnalyzer().Analyze("with Student as (select * from dbo.Source) select * from Student; select * from Student; select * from #Scratch;");
        Assert.True(result.IsComplete);
        Assert.Equal(new[] { "[dbo].[Source]", "[Student]" }, result.References.Select(item => item.QualifiedName));
    }

    [Fact]
    public void WriteAliasesResolveOnlyToProjectedFromTargets()
    {
        var result = new SqlDependencyAnalyzer().Analyze("update target set Value=1 from dbo.Target target join dbo.Source source on 1=1; insert into dbo.Output select * from dbo.Source;");
        Assert.True(result.IsComplete);
        Assert.Contains(result.References, item => item.QualifiedName == "[dbo].[Target]" && item.Access == SqlDependencyAccess.Write);
        Assert.DoesNotContain(result.References, item => item.QualifiedName == "[target]");
        Assert.Contains(result.References, item => item.QualifiedName == "[dbo].[Output]" && item.Access == SqlDependencyAccess.Write);
    }

    [Theory]
    [InlineData("exec(@sql)")]
    [InlineData("exec sys.sp_executesql @statement")]
    [InlineData("exec @procedure")]
    public void DynamicSqlIsAGapNotAStringDependency(string sql)
    {
        var result = new SqlDependencyAnalyzer().Analyze(sql);
        Assert.True(result.Parsed);
        Assert.False(result.IsComplete);
        Assert.Contains("sql.dynamic_sql", result.CoverageCodes);
        Assert.Empty(result.References);
    }

    [Fact]
    public void ParseFailureDiscardsProvisionalReferencesAndMessages()
    {
        var result = new SqlDependencyAnalyzer().Analyze("select * from dbo.Valid; select 'secret' from");
        Assert.False(result.Parsed);
        Assert.Empty(result.References);
        Assert.Equal(new[] { "sql.parse_failed" }, result.CoverageCodes);
        Assert.DoesNotContain("secret", JsonConvert.SerializeObject(result));
    }

    [Fact]
    public void PreservesExplicitQualifiersWithoutGuessingDefaults()
    {
        var result = new SqlDependencyAnalyzer().Analyze("select * from [Server].[Database].[Schema].[Object]; select * from [Database]..[Object]; select * from Unqualified;");
        Assert.True(result.Parsed);
        Assert.Equal(new[] { "Server", "Database", "Schema", "Object" }, result.References[0].Identifiers);
        Assert.Equal(new[] { "Database", "", "Object" }, result.References[1].Identifiers);
        Assert.Equal(new[] { "Unqualified" }, result.References[2].Identifiers);
    }

    [Fact]
    public void RecursiveCtesDoNotBecomeObjectsAndWritesThroughCtesRemainUnresolved()
    {
        var recursive = new SqlDependencyAnalyzer().Analyze("with Cte as (select * from dbo.Source union all select * from Cte) select * from Cte;");
        Assert.True(recursive.IsComplete);
        Assert.Equal("[dbo].[Source]", Assert.Single(recursive.References).QualifiedName);
        var update = new SqlDependencyAnalyzer().Analyze("with Cte as (select * from dbo.Source) update Cte set Value=1;");
        Assert.Contains("sql.reference_unresolved", update.CoverageCodes);
        Assert.DoesNotContain(update.References, item => item.QualifiedName == "[Cte]");
        var collation = new SqlDependencyAnalyzer().Analyze("with Cte as (select * from dbo.Source) select * from cte;");
        Assert.Contains("sql.reference_unresolved", collation.CoverageCodes);
    }

    [Fact]
    public void DerivedUpdateAliasesAreNotInventedSchemaObjects()
    {
        var result = new SqlDependencyAnalyzer().Analyze("update target set Value=1 from (select * from dbo.Source) target;");
        Assert.True(result.Parsed);
        Assert.Contains("sql.reference_unresolved", result.CoverageCodes);
        Assert.DoesNotContain(result.References, item => item.QualifiedName == "[target]");
    }

    [Fact]
    public void ContextChangesAndLimitsAreExplicit()
    {
        Assert.Contains("sql.context_changed", new SqlDependencyAnalyzer().Analyze("use Other; select * from dbo.Object;").CoverageCodes);
        Assert.Empty(new SqlDependencyAnalyzer().Analyze("use Other; select * from dbo.Object;").References);
        Assert.Contains("sql.dialect_unsupported", new SqlDependencyAnalyzer().Analyze("select 1", SqlDialect.Unsupported).CoverageCodes);
        Assert.Contains("sql.length_exceeded", new SqlDependencyAnalyzer().Analyze(new string(' ', 65536) + "1").CoverageCodes);
    }
}