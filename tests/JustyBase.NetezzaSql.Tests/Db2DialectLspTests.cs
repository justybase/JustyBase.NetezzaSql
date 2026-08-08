using JustyBase.NetezzaSqlLsp.Services;
using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.NetezzaSqlParser.Caching;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Lexer;

namespace JustyBase.Tests.NetezzaSqlParser;

/// <summary>
/// LSP-level Db2 dialect switching tests (mirror of OracleDialectLspTests).
/// </summary>
public sealed class Db2DialectLspTests
{
    [Fact]
    public void ParseSession_Db2Dialect_ParsesFetchFirstWithUr()
    {
        using var session = new DocumentParseSession(SqlDialect.Db2);
        var result = session.GetOrParse(
            "SELECT ID FROM T ORDER BY ID FETCH FIRST 5 ROWS ONLY WITH UR;");
        Assert.True(result.Valid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ParseSession_Db2Dialect_RejectsNetezzaOnlyLimit()
    {
        using var session = new DocumentParseSession(SqlDialect.Db2);
        var result = session.GetOrParse("SELECT * FROM t LIMIT 10;");
        Assert.False(result.Valid);
        Assert.Contains(result.Errors, e => e.Code == "PAR001");
    }

    [Fact]
    public void ParseSession_Db2Dialect_ParsesDgttAndAlias()
    {
        using var session = new DocumentParseSession(SqlDialect.Db2);
        var dgtt = session.GetOrParse(
            "DECLARE GLOBAL TEMPORARY TABLE SESSION.TMP1 (ID INTEGER) ON COMMIT PRESERVE ROWS;");
        Assert.True(dgtt.Valid);
        var alias = session.GetOrParse("CREATE ALIAS APP.ORDERS_A FOR APP.ORDERS;");
        Assert.True(alias.Valid);
    }

    [Fact]
    public void ParsingCoordinator_SameUriDifferentDialects_AreIsolated()
    {
        using var coordinator = new DocumentParsingCoordinator();
        var db2 = coordinator.GetOrCreate("doc1", SqlDialect.Db2);
        var netezza = coordinator.GetOrCreate("doc1", SqlDialect.Netezza);
        Assert.NotSame(db2, netezza);
        Assert.Same(db2, coordinator.GetOrCreate("doc1", SqlDialect.Db2));
    }

    [Fact]
    public void Lint_Db2Dialect_ReportsDb2001SelectStar()
    {
        var diagnostics = LintService.Lint("SELECT * FROM employees", null, SqlDialect.Db2);
        Assert.Contains(diagnostics, d => d.Code == "DB2001");
        Assert.All(diagnostics, d => Assert.Equal("Db2 SQL", d.Source));
    }

    [Fact]
    public void Lint_Db2Dialect_DoesNotReportNzRules()
    {
        var diagnostics = LintService.Lint("SELECT * FROM employees", null, SqlDialect.Db2);
        Assert.Contains(diagnostics, d => d.Code == "DB2001");
        Assert.DoesNotContain(diagnostics, d => d.Code?.StartsWith("NZ", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(diagnostics, d => d.Code?.StartsWith("ORA", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task Completion_Db2Dialect_OffersDb2Functions()
    {
        var completions = await CompletionService.GetCompletions("SELECT", 0, 6, null, SqlDialect.Db2);
        Assert.NotNull(completions.Items);
        Assert.Contains(completions.Items!, i => i.Label.Equals("COALESCE", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(completions.Items!, i => i.Label.Equals("CONCAT", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Hover_Db2Dialect_ExplainsDb2DataType()
    {
        var hover = HoverService.GetHover("SELECT DECFLOAT FROM t", 0, 10, null, SqlDialect.Db2);
        Assert.NotNull(hover);
        Assert.Contains("DECFLOAT", hover!.Contents!.Value);
    }

    [Fact]
    public void SignatureHelp_Db2Dialect_ReturnsDb2Signatures()
    {
        var help = SignatureHelpService.GetSignatureHelp("SELECT COALESCE(", 0, 16, SqlDialect.Db2);
        Assert.NotNull(help);
        Assert.Contains(help!.Signatures, s => s.Label.Contains("COALESCE", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SemanticTokens_Db2Dialect_TokenizesIsolation()
    {
        var classifier = new NzSemanticTokenClassifier(null, null, SqlDialect.Db2);
        var spans = classifier.Classify("SELECT 1 FROM T WITH UR");
        Assert.NotEmpty(spans);
    }

    [Theory]
    [InlineData(new[] { "--dialect=db2" }, SqlDialect.Db2)]
    [InlineData(new[] { "--dialect", "db2" }, SqlDialect.Db2)]
    [InlineData(new[] { "--dialect=oracle" }, SqlDialect.Oracle)]
    [InlineData(new[] { "--dialect=netezza" }, SqlDialect.Netezza)]
    [InlineData(new string[0], SqlDialect.Netezza)]
    public void LspDialectArgs_ParsesDb2Forms(string[] args, SqlDialect expected)
    {
        Assert.Equal(expected, JustyBase.NetezzaSqlLsp.LspDialectArgs.Parse(args));
    }

    [Fact]
    public void DialectRuntime_QualityRules_Db2Only()
    {
        var rules = DialectRuntime.QualityRules(SqlDialect.Db2).AllRules;
        Assert.All(rules, r => Assert.StartsWith("DB2", r.Id));
    }
}
