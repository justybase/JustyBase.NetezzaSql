using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.NetezzaSqlParser.Caching;
using JustyBase.NetezzaSqlParser.Completion;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Lexer;

namespace JustyBase.Tests.NetezzaSqlParser;

/// <summary>
/// Db2 dialect switching tests against the shared parser authoring stack
/// (mirror of OracleDialectAuthoringTests).
/// </summary>
public sealed class Db2DialectAuthoringTests
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

    private static IReadOnlyList<JustyBase.NetezzaSqlParser.Linter.LintIssue> CheckDb2(string sql) =>
        DialectRuntime.QualityRules(SqlDialect.Db2).AllRules.SelectMany(r => r.Check(sql)).ToList();

    [Fact]
    public void Lint_Db2Dialect_ReportsDb2001SelectStar()
    {
        var issues = CheckDb2("SELECT * FROM employees");
        Assert.Contains(issues, d => d.RuleId == "DB2001");
        Assert.Equal("Db2 SQL", DialectRuntime.DiagnosticSource(SqlDialect.Db2));
    }

    [Fact]
    public void Lint_Db2Dialect_DoesNotReportNzRules()
    {
        var issues = CheckDb2("SELECT * FROM employees");
        Assert.Contains(issues, d => d.RuleId == "DB2001");
        Assert.DoesNotContain(issues, d => d.RuleId.StartsWith("NZ", StringComparison.Ordinal));
        Assert.DoesNotContain(issues, d => d.RuleId.StartsWith("ORA", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Completion_Db2Dialect_OffersDb2Functions()
    {
        var result = await CompletionOrchestrator.GetCompletions("SELECT", 6, null, SqlDialect.Db2);
        Assert.Contains(result.EngineItems, i => i.Label.Equals("COALESCE", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.EngineItems, i => i.Label.Equals("CONCAT", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Hover_Db2Dialect_ExplainsDb2DataType()
    {
        const string sql = "SELECT DECFLOAT FROM t";
        var hover = NzHoverService.GetHover(
            sql, 10, null,
            catalog: DialectRuntime.AuthoringCatalog(SqlDialect.Db2),
            dialect: SqlDialect.Db2);
        Assert.NotNull(hover);
        Assert.Contains("DECFLOAT", hover!.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SignatureHelp_Db2Dialect_ReturnsDb2Signatures()
    {
        const string sql = "SELECT COALESCE(";
        var help = NzSignatureHelpService.GetSignatureHelp(
            sql, sql.Length,
            catalog: DialectRuntime.AuthoringCatalog(SqlDialect.Db2),
            dialect: SqlDialect.Db2);
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
    [InlineData("db2", SqlDialect.Db2)]
    [InlineData("oracle", SqlDialect.Oracle)]
    [InlineData("netezza", SqlDialect.Netezza)]
    [InlineData(null, SqlDialect.Netezza)]
    public void DialectRuntime_ParsesDb2Forms(string? name, SqlDialect expected)
    {
        Assert.Equal(expected, DialectRuntime.ParseName(name));
    }

    [Fact]
    public void DialectRuntime_QualityRules_Db2Only()
    {
        var rules = DialectRuntime.QualityRules(SqlDialect.Db2).AllRules;
        Assert.All(rules, r => Assert.StartsWith("DB2", r.Id));
    }
}
