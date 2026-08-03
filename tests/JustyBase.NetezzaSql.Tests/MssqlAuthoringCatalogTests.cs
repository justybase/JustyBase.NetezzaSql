using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.NetezzaSqlParser.Completion;

namespace JustyBase.NetezzaSql.Tests;

public sealed class MssqlAuthoringCatalogTests
{
    private static readonly MssqlSqlCatalog Catalog = MssqlSqlCatalog.Instance;

    [Fact]
    public void ExposesMssqlTypesAndBuiltins()
    {
        Assert.True(Catalog.TryGetDataType("NVARCHAR(50)", out var nvarchar));
        Assert.Equal("NVARCHAR", nvarchar.CanonicalName);
        Assert.True(nvarchar.WarnWhenLengthIsMissing);
        Assert.True(Catalog.TryGetDataType("DATETIME2", out var datetime2));
        Assert.Equal("DATETIME2", datetime2.CanonicalName);
        Assert.True(Catalog.TryGetDataType("UNIQUEIDENTIFIER", out var uid));
        Assert.Equal("UNIQUEIDENTIFIER", uid.CanonicalName);
        Assert.Contains(Catalog.ValidationBuiltinFunctions, f =>
            string.Equals(f.Name, "GETDATE", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExposesIsNullAndStringAggSignatures()
    {
        Assert.True(NzSignatureHelpService.TryGetSignature("ISNULL", Catalog, out var isnull));
        Assert.Contains("check_expression", isnull.Label, StringComparison.OrdinalIgnoreCase);
        Assert.True(NzSignatureHelpService.TryGetSignature("STRING_AGG", Catalog, out var stringAgg));
        Assert.Contains("separator", stringAgg.Label, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CompletionKeywords_IncludeMssqlPhrases()
    {
        Assert.Contains("TOP", Catalog.CompletionKeywords);
        Assert.Contains("OUTPUT", Catalog.CompletionKeywords);
        Assert.Contains("CROSS APPLY", Catalog.CompletionKeywords);
        Assert.Contains("OUTER APPLY", Catalog.CompletionKeywords);
        Assert.Contains("BEGIN TRY", Catalog.CompletionKeywords);
    }

    [Fact]
    public void CompletionEngine_SurfacesMssqlFunctions()
    {
        var engine = new NzCompletionEngine(catalog: Catalog);
        var items = engine.GetCompletions("SELECT ", 7);
        Assert.Contains(items, i => i.Label.Equals("ISNULL", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(items, i => i.Label.Equals("GETDATE", StringComparison.OrdinalIgnoreCase));
    }
}
