using JustyBase.NetezzaSqlLsp.Services;
using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.NetezzaSqlParser.Caching;
using JustyBase.NetezzaSqlParser.Dialects;

namespace JustyBase.Tests.NetezzaSqlParser;

/// <summary>
/// LSP-level MSSQL dialect switching tests (mirror of Db2DialectLspTests).
/// </summary>
public sealed class MssqlDialectLspTests
{
    [Fact]
    public void ParseSession_MssqlDialect_ParsesTopAndApply()
    {
        using var session = new DocumentParseSession(SqlDialect.Mssql);
        var top = session.GetOrParse("SELECT TOP 10 Id FROM dbo.Orders ORDER BY Id;");
        Assert.True(top.Valid);
        Assert.Empty(top.Errors);
        var apply = session.GetOrParse("SELECT a.id FROM dbo.A a CROSS APPLY dbo.fn(a.id) f;");
        Assert.True(apply.Valid);
        Assert.Empty(apply.Errors);
    }

    [Fact]
    public void ParseSession_MssqlDialect_ParsesOutputOnInsert()
    {
        using var session = new DocumentParseSession(SqlDialect.Mssql);
        var result = session.GetOrParse(
            "INSERT INTO dbo.Orders (id) OUTPUT inserted.id VALUES (1);");
        Assert.True(result.Valid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ParseSession_MssqlDialect_RejectsNetezzaOnlyLimit()
    {
        using var session = new DocumentParseSession(SqlDialect.Mssql);
        var result = session.GetOrParse("SELECT * FROM t LIMIT 10;");
        Assert.False(result.Valid);
        Assert.Contains(result.Errors, e => e.Code == "PAR001");
    }

    [Fact]
    public void ParsingCoordinator_SameUriDifferentDialects_AreIsolated()
    {
        using var coordinator = new DocumentParsingCoordinator();
        var mssql = coordinator.GetOrCreate("doc1", SqlDialect.Mssql);
        var netezza = coordinator.GetOrCreate("doc1", SqlDialect.Netezza);
        Assert.NotSame(mssql, netezza);
        Assert.Same(mssql, coordinator.GetOrCreate("doc1", SqlDialect.Mssql));
    }

    [Fact]
    public void Lint_MssqlDialect_ReportsMss001SelectStar()
    {
        var diagnostics = LintService.Lint("SELECT * FROM employees", null, SqlDialect.Mssql);
        Assert.Contains(diagnostics, d => d.Code == "MSS001");
        Assert.All(diagnostics, d => Assert.Equal("MSSQL SQL", d.Source));
    }

    [Fact]
    public void Lint_MssqlDialect_DoesNotReportNzOrDb2Rules()
    {
        var diagnostics = LintService.Lint("SELECT * FROM employees", null, SqlDialect.Mssql);
        Assert.Contains(diagnostics, d => d.Code == "MSS001");
        Assert.DoesNotContain(diagnostics, d => d.Code?.StartsWith("NZ", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(diagnostics, d => d.Code?.StartsWith("DB", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(diagnostics, d => d.Code?.StartsWith("ORA", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task Completion_MssqlDialect_OffersMssqlFunctions()
    {
        var completions = await CompletionService.GetCompletions("SELECT ", 0, 7, null, SqlDialect.Mssql);
        Assert.NotNull(completions.Items);
        Assert.Contains(completions.Items!, i => i.Label.Equals("ISNULL", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(completions.Items!, i => i.Label.Equals("GETDATE", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Hover_MssqlDialect_ExplainsMssqlDataType()
    {
        var hover = HoverService.GetHover("SELECT NVARCHAR FROM t", 0, 10, null, SqlDialect.Mssql);
        Assert.NotNull(hover);
        Assert.Contains("NVARCHAR", hover!.Contents!.Value);
    }

    [Fact]
    public void SignatureHelp_MssqlDialect_ReturnsMssqlSignatures()
    {
        var help = SignatureHelpService.GetSignatureHelp("SELECT ISNULL(", 0, 15, SqlDialect.Mssql);
        Assert.NotNull(help);
        Assert.Contains(help!.Signatures, s => s.Label.Contains("ISNULL", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SemanticTokens_MssqlDialect_TokenizesIsolation()
    {
        var classifier = new NzSemanticTokenClassifier(null, null, SqlDialect.Mssql);
        var spans = classifier.Classify("SELECT TOP 10 * FROM [Sales].[Orders]");
        Assert.NotEmpty(spans);
    }

    [Theory]
    [InlineData(new[] { "--dialect=mssql" }, SqlDialect.Mssql)]
    [InlineData(new[] { "--dialect", "mssql" }, SqlDialect.Mssql)]
    [InlineData(new[] { "--dialect=db2" }, SqlDialect.Db2)]
    [InlineData(new[] { "--dialect=oracle" }, SqlDialect.Oracle)]
    [InlineData(new[] { "--dialect=netezza" }, SqlDialect.Netezza)]
    [InlineData(new string[0], SqlDialect.Netezza)]
    public void LspDialectArgs_ParsesMssqlForms(string[] args, SqlDialect expected)
    {
        Assert.Equal(expected, JustyBase.NetezzaSqlLsp.LspDialectArgs.Parse(args));
    }

    [Fact]
    public void DialectRuntime_QualityRules_MssqlOnly()
    {
        var rules = DialectRuntime.QualityRules(SqlDialect.Mssql).AllRules;
        Assert.All(rules, r => Assert.StartsWith("MSS", r.Id));
    }

    [Fact]
    public void DialectRuntime_ParseName_AcceptsMssqlAndSqlServer()
    {
        Assert.Equal(SqlDialect.Mssql, DialectRuntime.ParseName("mssql"));
        Assert.Equal(SqlDialect.Mssql, DialectRuntime.ParseName("SQLSERVER"));
    }
}
