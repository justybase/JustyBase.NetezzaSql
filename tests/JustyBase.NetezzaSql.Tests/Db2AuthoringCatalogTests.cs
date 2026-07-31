using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.NetezzaSqlParser.Completion;

namespace JustyBase.NetezzaSql.Tests;

/// <summary>
/// Port of src/__tests__/db2Authoring.test.ts (catalog surface).
/// </summary>
public sealed class Db2AuthoringCatalogTests
{
    private static readonly Db2SqlCatalog Catalog = Db2SqlCatalog.Instance;

    [Fact]
    public void ExposesDb2TypesAndBuiltins()
    {
        Assert.True(Catalog.TryGetDataType("DECFLOAT", out var decfloat));
        Assert.Equal("DECFLOAT", decfloat.CanonicalName);
        Assert.True(Catalog.TryGetDataType("VARCHAR", out var varchar));
        Assert.True(varchar.WarnWhenLengthIsMissing);
        Assert.True(Catalog.TryGetDataType("CHARACTER VARYING(40)", out var varying));
        Assert.Equal("VARCHAR", varying.CanonicalName);
        Assert.Contains(Catalog.ValidationBuiltinFunctions, f =>
            string.Equals(f.Name, "COALESCE", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExposesCountSignature()
    {
        Assert.True(NzSignatureHelpService.TryGetSignature("COUNT", Catalog, out var signature));
        Assert.Contains("expression", signature.Label, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CompletionKeywords_IncludeDb2Phrases()
    {
        Assert.Contains("FETCH FIRST", Catalog.CompletionKeywords);
        Assert.Contains("OPTIMIZE FOR", Catalog.CompletionKeywords);
        Assert.Contains("WITH UR", Catalog.CompletionKeywords);
        Assert.Contains("FINAL TABLE", Catalog.CompletionKeywords);
        Assert.Contains("NICKNAME", Catalog.CompletionKeywords);
    }

    [Fact]
    public void CompletionEngine_SurfacesDb2Functions()
    {
        var engine = new NzCompletionEngine(catalog: Catalog);
        var items = engine.GetCompletions("SELECT ", 7);
        Assert.Contains(items, i => i.Label.Equals("COALESCE", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(items, i => i.Label.Equals("COUNT", StringComparison.OrdinalIgnoreCase));
    }
}
