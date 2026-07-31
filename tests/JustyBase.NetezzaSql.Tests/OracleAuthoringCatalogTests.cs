using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.NetezzaSqlParser.Completion;

namespace JustyBase.Tests.NetezzaSqlParser;

/// <summary>
/// Oracle authoring catalog tests. Port of the authoring surface exposed by
/// extensions/oracle/src/sql/authoring.ts in the reference TypeScript project.
/// </summary>
public class OracleAuthoringCatalogTests
{
    private static readonly OracleSqlCatalog Catalog = OracleSqlCatalog.Instance;

    // ====== Type specs ======

    [Fact]
    public void OracleCatalog_DataTypes_ContainsOracleTypes()
    {
        Assert.Contains(Catalog.DataTypes, t => t.CanonicalName == "NUMBER" && t.MaxParameters == 2);
        Assert.Contains(Catalog.DataTypes, t => t.CanonicalName == "VARCHAR2" && t.WarnWhenLengthIsMissing);
        Assert.Contains(Catalog.DataTypes, t => t.CanonicalName == "TIMESTAMP WITH TIME ZONE");
        Assert.Contains(Catalog.DataTypes, t => t.CanonicalName == "INTERVAL DAY TO SECOND");
        Assert.Contains(Catalog.DataTypes, t => t.CanonicalName == "XMLTYPE");
    }

    [Fact]
    public void OracleCatalog_TryGetDataType_ResolvesAliases()
    {
        Assert.True(Catalog.TryGetDataType("VARCHAR2", out var type));
        Assert.Equal("VARCHAR2", type.CanonicalName);
        Assert.False(Catalog.TryGetDataType("VARCHAR", out _)); // Oracle has no VARCHAR
    }

    // ====== Functions ======

    [Fact]
    public void OracleCatalog_BuiltinFunctions_ContainsOracleFunctions()
    {
        Assert.Contains(Catalog.BuiltinFunctions, f => f.Name == "NVL");
        Assert.Contains(Catalog.BuiltinFunctions, f => f.Name == "TO_DATE");
        Assert.Contains(Catalog.BuiltinFunctions, f => f.Name == "SYS_CONTEXT");
        Assert.Contains(Catalog.BuiltinFunctions, f => f.Name == "ADD_MONTHS");
        Assert.Contains(Catalog.BuiltinFunctions, f => f.Name == "REGEXP_LIKE");
    }

    [Fact]
    public void OracleCatalog_TryGetFunction_ResolvesSignature()
    {
        Assert.True(Catalog.TryGetFunction("to_date", out var function));
        var signature = function.Signatures.First();
        Assert.Contains("format", signature.Parameters[^1].Label);
    }

    // ====== Completion keywords ======

    [Fact]
    public void OracleCatalog_CompletionKeywords_ContainsOracleConstructs()
    {
        foreach (var keyword in new[] { "PIVOT", "UNPIVOT", "DUAL", "RETURNING INTO", "CONNECT BY", "START WITH", "PACKAGE", "TRIGGER", "FETCH FIRST", "ROWNUM" })
            Assert.Contains(Catalog.CompletionKeywords, k => k.Equals(keyword, StringComparison.OrdinalIgnoreCase));
    }

    // ====== Service injection ======

    [Fact]
    public void CompletionEngine_WithOracleCatalog_OffersOracleKeywords()
    {
        var engine = new NzCompletionEngine(catalog: OracleSqlCatalog.Instance);
        var labels = engine.GetCompletions("", 0).Select(i => i.Label).ToArray();

        Assert.Contains(labels, l => l.Equals("DUAL", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(labels, l => l.Equals("PIVOT", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(labels, l => l.Equals("CONNECT BY", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CompletionEngine_WithOracleCatalog_OffersOracleFunctions()
    {
        var engine = new NzCompletionEngine(catalog: OracleSqlCatalog.Instance);
        var labels = engine.GetCompletions("SELECT ", 7).Select(i => i.Label).ToArray();

        Assert.Contains(labels, l => l.Equals("NVL", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(labels, l => l.Equals("TO_DATE", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(labels, l => l.Equals("SYS_CONTEXT", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SignatureHelp_WithOracleCatalog_FindsOracleFunction()
    {
        Assert.True(NzSignatureHelpService.TryGetSignature("to_date", OracleSqlCatalog.Instance, out var signature));
        Assert.Equal("TO_DATE", signature.Label.Split('(')[0], ignoreCase: true);
    }

    [Fact]
    public void SignatureHelp_WithNetezzaCatalog_DoesNotFindOracleOnlyFunction()
    {
        Assert.False(NzSignatureHelpService.TryGetSignature("SYS_CONTEXT", NetezzaSqlAuthoringCatalog.Instance, out _));
    }

    [Fact]
    public void AlterTableCompletion_WithOracleCatalog_OffersOracleTypes()
    {
        var keywords = AlterTableCompletion.GetKeywordsForPhase(AlterTablePhase.AddColumnType, OracleSqlCatalog.Instance);
        Assert.Contains(keywords, k => k.Equals("VARCHAR2", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(keywords, k => k.Equals("NUMBER", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(keywords, k => k.Equals("VARCHAR", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NetezzaAdapter_MatchesStaticCatalog()
    {
        var adapter = NetezzaSqlAuthoringCatalog.Instance;
        Assert.True(adapter.TryGetFunction("HASH", out _));
        Assert.True(adapter.TryGetDataType("DOUBLE PRECISION", out _));
        Assert.Contains("VARCHAR", adapter.DataTypeNames, StringComparer.OrdinalIgnoreCase);
    }
}
