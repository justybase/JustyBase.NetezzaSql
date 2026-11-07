using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.NetezzaSqlParser.Completion;
using JustyBase.NetezzaSqlParser.Dialects;

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

    [Fact]
    public void Denylists_CoverNetezzaOnlySurface()
    {
        foreach (var keyword in new[] { "LIMIT", "ILIKE", "GROOM", "DISTRIBUTE ON", "ORGANIZE ON", "EXPLAIN" })
        {
            if (keyword == "EXPLAIN")
            {
                // Db2 has EXPLAIN; it must stay suggestible.
                Assert.DoesNotContain(keyword, Catalog.DeniedCompletionKeywords);
                continue;
            }

            Assert.Contains(keyword, Catalog.DeniedCompletionKeywords);
        }

        foreach (var function in new[] { "SPLIT_PART", "REVERSE", "PI", "ASCII" })
        {
            Assert.Contains(function, Catalog.DeniedFunctionNames);
        }
    }

    [Fact]
    public void Catalog_ExposesMoreLiveVerifiedDb2Surface()
    {
        Assert.True(Catalog.TryGetDataType("BINARY(16)", out var binary));
        Assert.Equal("BINARY", binary.CanonicalName);
        Assert.True(Catalog.TryGetDataType("ROWID", out _));
        Assert.True(Catalog.TryGetDataType("BOOLEAN", out _));
        Assert.False(Catalog.TryGetDataType("TEXT", out _));

        foreach (var function in new[] { "LISTAGG", "DIGITS", "VARCHAR_FORMAT", "DAYNAME", "REGEXP_LIKE" })
        {
            Assert.True(Catalog.TryGetFunction(function, out _), $"{function} must be suggested for Db2");
        }
    }

    [Fact]
    public void CompletionEngine_Db2_DoesNotLeakNetezzaFunctionsOrKeywords()
    {
        var engine = new NzCompletionEngine(catalog: Catalog, dialect: SqlDialect.Db2);
        var items = engine.GetCompletions("SELECT ", 7);

        Assert.DoesNotContain(items, i => i.Label.Equals("ADD_MONTHS", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(items, i => i.Label.Equals("HASH4", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(items, i => i.Label.Equals("GROOM", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(items, i => i.Label.Equals("NZPLSQL", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(items, i => i.Label.Equals("LISTAGG", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(items, i => i.Label.Equals("NVL", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CompletionEngine_Db2_OrderByUsesFetchFirstNotLimit()
    {
        var engine = new NzCompletionEngine(catalog: Catalog, dialect: SqlDialect.Db2);
        const string sql = "SELECT * FROM T ORDER BY ";
        var items = engine.GetCompletions(sql, sql.Length);

        Assert.Contains(items, i => i.Label == "FETCH");
        Assert.DoesNotContain(items, i => i.Label == "LIMIT");
        Assert.DoesNotContain(items, i => i.Label == "NULLS");
    }

    [Fact]
    public void CompletionEngine_Db2_AlterTableOmitsNetezzaActions()
    {
        var engine = new NzCompletionEngine(catalog: Catalog, dialect: SqlDialect.Db2);
        const string sql = "ALTER TABLE T ";
        var items = engine.GetCompletions(sql, sql.Length);

        Assert.Contains(items, i => i.Label == "ADD COLUMN");
        Assert.Contains(items, i => i.Label == "RENAME COLUMN");
        Assert.DoesNotContain(items, i => i.Label == "DISTRIBUTE ON");
        Assert.DoesNotContain(items, i => i.Label == "ORGANIZE ON");
        Assert.DoesNotContain(items, i => i.Label == "MODIFY COLUMN");
        Assert.DoesNotContain(items, i => i.Label == "RENAME TO");
    }

    [Fact]
    public void CompletionEngine_Netezza_StillSuggestsNetezzaOnlySurface()
    {
        var engine = new NzCompletionEngine(catalog: NetezzaSqlAuthoringCatalog.Instance, dialect: SqlDialect.Netezza);
        var items = engine.GetCompletions("SELECT ", 7);

        Assert.Contains(items, i => i.Label.Equals("ADD_MONTHS", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(items, i => i.Label.Equals("HASH4", StringComparison.OrdinalIgnoreCase));
    }
}
