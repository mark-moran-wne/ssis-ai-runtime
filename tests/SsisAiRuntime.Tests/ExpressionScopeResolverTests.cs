using Newtonsoft.Json;
using SsisAiRuntime.Inspectors.Expressions;

namespace SsisAiRuntime.Tests;

public sealed class ExpressionScopeResolverTests
{
    private static ExpressionSymbol Variable(string id, string scope, string name = "Value", string ns = "User") =>
        new(id, scope, ExpressionSymbolKind.Variable, ns, name);
    private static ExpressionScope Scope(string id, string parent, params ExpressionSymbol[] symbols) =>
        new(id, parent, id, parent.Length == 0 ? ExpressionScopeKind.Package : ExpressionScopeKind.Task, symbols);
    private static ExpressionReferenceResolution Resolve(ExpressionScopeCatalog catalog, string owner, string expression) =>
        Assert.Single(new ExpressionScopeResolver().Resolve(owner, new ExpressionReferenceParser().Analyze(expression).References, catalog));

    [Theory]
    [InlineData("package", "package-var")]
    [InlineData("task", "task-var")]
    [InlineData("nested", "nested-var")]
    [InlineData("child", "nested-var")]
    public void ResolvesNearestDeclaration(string owner, string expected)
    {
        var catalog = new ExpressionScopeCatalog(new[] { Scope("package", "", Variable("package-var", "package")),
            Scope("task", "package", Variable("task-var", "task")), Scope("nested", "task", Variable("nested-var", "nested")), Scope("child", "nested") });
        var result = Resolve(catalog, owner, "@[user::value]");
        Assert.Equal(ExpressionReferenceResolutionStatus.Resolved, result.Status);
        Assert.Equal(expected, result.ResolvedSymbol.NativeId);
        Assert.Equal(ExpressionResolutionEvidence.LexicalAndScopeResolved, result.Evidence);
    }

    [Fact]
    public void SameScopeAmbiguityStopsParentLookup()
    {
        var catalog = new ExpressionScopeCatalog(new[] { Scope("package", "", Variable("parent", "package")),
            Scope("task", "package", Variable("first", "task"), Variable("second", "task")) });
        var result = Resolve(catalog, "task", "@[User::Value]");
        Assert.Equal(ExpressionReferenceResolutionStatus.Ambiguous, result.Status);
        Assert.Equal(new[] { "first", "second" }, result.Candidates.Select(item => item.NativeId));
    }

    [Fact]
    public void NamespacesAndUnwrappedNamesAreConservative()
    {
        var catalog = new ExpressionScopeCatalog(new[] { Scope("package", "", Variable("user", "package"), Variable("other", "package", ns: "Other")) });
        Assert.Equal("user", Resolve(catalog, "package", "@[User::Value]").ResolvedSymbol.NativeId);
        Assert.Equal(ExpressionReferenceResolutionStatus.Ambiguous, Resolve(catalog, "package", "@Value").Status);
        Assert.Equal(ExpressionReferenceResolutionStatus.NotFound, Resolve(catalog, "package", "@[Missing::Value]").Status);
        Assert.Equal(ExpressionReferenceResolutionStatus.NotFound, Resolve(catalog, "package", "@Absent").Status);
        Assert.Equal(ExpressionReferenceResolutionStatus.InvalidOwnerScope, Resolve(catalog, "missing", "@Value").Status);
    }

    [Fact]
    public void ParameterInventoriesAndSystemVariablesAreExplicit()
    {
        var catalog = new ExpressionScopeCatalog(new[] { Scope("package", "",
            new ExpressionSymbol("parameter", "package", ExpressionSymbolKind.PackageParameter, "$Package", "Size"),
            new ExpressionSymbol("project", "package", ExpressionSymbolKind.ProjectParameter, "$Project", "Size"),
            new ExpressionSymbol("system", "package", ExpressionSymbolKind.SystemVariable, "System", "StartTime")),
            Scope("task", "package", Variable("local", "task", "Size"), Variable("fake-system", "task", "StartTime", "System")) }, true);
        Assert.Equal("parameter", Resolve(catalog, "task", "@[$Package::Size]").ResolvedSymbol.NativeId);
        Assert.Equal("project", Resolve(catalog, "task", "@[$Project::Size]").ResolvedSymbol.NativeId);
        Assert.Equal("system", Resolve(catalog, "task", "@[System::StartTime]").ResolvedSymbol.NativeId);
        Assert.Equal(ExpressionReferenceResolutionStatus.NotFound, Resolve(catalog, "task", "@[System::Unknown]").Status);
        Assert.Equal(ExpressionReferenceResolutionStatus.NotFound, Resolve(catalog, "task", "@[$Project::Missing]").Status);
        var standalone = new ExpressionScopeCatalog(new[] { Scope("package", "") });
        Assert.Equal(ExpressionReferenceResolutionStatus.Unsupported, Resolve(standalone, "package", "@[$Project::Size]").Status);
        Assert.Equal(ExpressionReferenceResolutionStatus.NotFound, Resolve(standalone, "package", "@[$Package::Size]").Status);
    }

    [Fact]
    public void CatalogRejectsInvalidTopologyAndIdentity()
    {
        Assert.Throws<ExpressionScopeCatalogException>(() => new ExpressionScopeCatalog(new[] { Scope("package", ""), Scope("task", "missing") }));
        var cycle = Assert.Throws<ExpressionScopeCatalogException>(() => new ExpressionScopeCatalog(new[] { Scope("package", ""), Scope("a", "b"), Scope("b", "a") }));
        Assert.Equal("expression.scope_cycle", cycle.ReasonCode);
        Assert.Throws<ExpressionScopeCatalogException>(() => new ExpressionScopeCatalog(new[] { Scope("package", ""), Scope("second", "") }));
        Assert.Throws<ExpressionScopeCatalogException>(() => new ExpressionScopeCatalog(new[] { Scope("package", "", Variable("id", "wrong")) }));
        Assert.Throws<ExpressionScopeCatalogException>(() => new ExpressionScopeCatalog(new[] { Scope("package", "", Variable("id", "package")), Scope("task", "package", Variable("id", "task")) }));
    }

    [Fact]
    public void AnalyzerFailureSkipsResolverAndOutputIsRedacted()
    {
        var resolver = new CountingResolver();
        var analyzer = new ExpressionDependencyAnalyzer(new ExpressionReferenceParser(), resolver);
        var catalog = new ExpressionScopeCatalog(new[] { Scope("package", "") });
        var failed = analyzer.Analyze(new ExpressionDependencyInput("owner", "package", "Property", "@Secret +"), catalog);
        Assert.False(failed.IsComplete);
        Assert.Equal(0, resolver.Calls);
        Assert.Empty(failed.Resolutions);
        Assert.DoesNotContain("Secret", JsonConvert.SerializeObject(failed));
        Assert.DoesNotContain("Expression", JsonConvert.SerializeObject(new ExpressionDependencyInput("owner", "package", "Property", "Secret")));
        var constant = analyzer.Analyze(new ExpressionDependencyInput("owner", "package", "Property", "42"), catalog);
        Assert.True(constant.IsComplete);
        var missing = analyzer.Analyze(new ExpressionDependencyInput("owner", "package", "Property", "@Missing + @Missing"), catalog);
        Assert.False(missing.IsComplete);
        Assert.Equal(2, missing.Resolutions.Count);
        Assert.DoesNotContain("Diagnostics", JsonConvert.SerializeObject(missing));
    }

    private sealed class CountingResolver : IExpressionScopeResolver
    {
        public int Calls { get; private set; }
        public IReadOnlyList<ExpressionReferenceResolution> Resolve(string owner, IReadOnlyList<ExpressionReference> references, ExpressionScopeCatalog catalog)
        { Calls++; return new ExpressionScopeResolver().Resolve(owner, references, catalog); }
    }
}