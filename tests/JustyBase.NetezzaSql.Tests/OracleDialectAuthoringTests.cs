using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.NetezzaSqlParser.Caching;
using JustyBase.NetezzaSqlParser.Completion;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Lexer;

namespace JustyBase.Tests.NetezzaSqlParser;

/// <summary>
/// Oracle dialect switching tests: the Oracle dialect composes the Oracle
/// lexer/parser into the parse session and shared authoring services
/// (lint rules, authoring catalogs), while the Netezza dialect keeps the shared pipeline unchanged.
/// </summary>
public sealed class OracleDialectAuthoringTests
{
    // ====== DocumentParseSession dialect ======

    [Fact]
    public void ParseSession_OracleDialect_ParsesDatabaseLinksWithoutErrors()
    {
        using var session = new DocumentParseSession(SqlDialect.Oracle);

        var result = session.GetOrParse("SELECT * FROM HR.EMPLOYEES@PROD;");

        Assert.True(result.Valid);
        Assert.Single(result.Statements);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ParseSession_NetezzaDialect_RejectsDatabaseLinks()
    {
        using var session = new DocumentParseSession(SqlDialect.Netezza);

        var result = session.GetOrParse("SELECT * FROM HR.EMPLOYEES@PROD;");

        Assert.False(result.Valid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void ParseSession_OracleDialect_ParsesAnonymousBlock()
    {
        using var session = new DocumentParseSession(SqlDialect.Oracle);

        var result = session.GetOrParse("""
            BEGIN
              IF :NEW.ID IS NULL THEN
                :NEW.ID := seq.NEXTVAL;
              END IF;
            END;
            """);

        Assert.True(result.Valid);
        Assert.Single(result.Statements);
    }

    [Fact]
    public void ParseSession_OracleDialect_RejectsNetezzaOnlyLimit()
    {
        using var session = new DocumentParseSession(SqlDialect.Oracle);

        var result = session.GetOrParse("SELECT * FROM t LIMIT 10;");

        Assert.False(result.Valid);
        Assert.Contains(result.Errors, e => e.Code == "PAR001");
    }

    // ====== ParsingRuntime + coordinator dialect ======

    [Fact]
    public void ParsingRuntime_OracleDialect_CompilesCatalog()
    {
        using var runtime = new ParsingRuntime(SqlDialect.Oracle);

        var result = runtime.Parse("SELECT COUNT(*) FROM t@PROD;");

        Assert.True(result.Valid);
        Assert.Equal(1, runtime.GetStats().misses);
    }

    [Fact]
    public void ParsingCoordinator_Clear_AfterDialectSwitchDropsStaleSessions()
    {
        using var coordinator = new DocumentParsingCoordinator();
        var oracle = coordinator.GetOrCreate("doc1", SqlDialect.Oracle);
        Assert.NotNull(oracle);

        coordinator.Clear();
        var again = coordinator.GetOrCreate("doc1", SqlDialect.Netezza);
        Assert.NotSame(oracle, again);
    }

    [Fact]
    public void ParsingCoordinator_SameUriDifferentDialects_AreIsolated()
    {
        using var coordinator = new DocumentParsingCoordinator();
        var oracle = coordinator.GetOrCreate("doc1", SqlDialect.Oracle);
        var netezza = coordinator.GetOrCreate("doc1", SqlDialect.Netezza);

        Assert.NotSame(oracle, netezza);
        Assert.Same(oracle, coordinator.GetOrCreate("doc1", SqlDialect.Oracle));
        Assert.Same(netezza, coordinator.GetOrCreate("doc1", SqlDialect.Netezza));
    }

    // ====== Quality rules dialect ======

    private static IReadOnlyList<JustyBase.NetezzaSqlParser.Linter.LintIssue> CheckOracle(string sql) =>
        DialectRuntime.QualityRules(SqlDialect.Oracle).AllRules.SelectMany(r => r.Check(sql)).ToList();

    private static IReadOnlyList<JustyBase.NetezzaSqlParser.Linter.LintIssue> CheckNetezza(string sql) =>
        DialectRuntime.QualityRules(SqlDialect.Netezza).AllRules.SelectMany(r => r.Check(sql)).ToList();

    [Fact]
    public void Lint_OracleDialect_ReportsOra001SelectStar()
    {
        var issues = CheckOracle("SELECT * FROM employees");

        Assert.Contains(issues, d => d.RuleId == "ORA001");
    }

    [Fact]
    public void Lint_OracleDialect_ReportsOra002DeleteWithoutWhere()
    {
        var issues = CheckOracle("DELETE FROM employees");

        Assert.Contains(issues, d => d.RuleId == "ORA002");
    }

    [Fact]
    public void Lint_NetezzaDialect_DoesNotReportOraRules()
    {
        var issues = CheckNetezza("SELECT * FROM employees");

        Assert.DoesNotContain(issues, d => d.RuleId.StartsWith("ORA", StringComparison.Ordinal));
    }

    [Fact]
    public void Lint_OracleDialect_DoesNotReportNzRules()
    {
        // SELECT * triggers ORA001; NZ001 must not fire in Oracle mode.
        var issues = CheckOracle("SELECT * FROM employees");

        Assert.Contains(issues, d => d.RuleId == "ORA001");
        Assert.DoesNotContain(issues, d => d.RuleId.StartsWith("NZ", StringComparison.Ordinal));
        Assert.Equal("Oracle SQL", DialectRuntime.DiagnosticSource(SqlDialect.Oracle));
    }

    // ====== Completion + hover + signature catalogs ======

    [Fact]
    public async Task Completion_OracleDialect_OffersOracleFunctions()
    {
        var result = await CompletionOrchestrator.GetCompletions("SELECT", 6, null, SqlDialect.Oracle);

        Assert.Contains(result.EngineItems, i => i.Label == "NVL");
        Assert.Contains(result.EngineItems, i => i.Label == "TO_DATE");
    }

    [Fact]
    public void Hover_OracleDialect_ExplainsOracleDataType()
    {
        const string sql = "SELECT VARCHAR2(10) FROM t";
        var hover = NzHoverService.GetHover(
            sql, 10, null,
            catalog: DialectRuntime.AuthoringCatalog(SqlDialect.Oracle),
            dialect: SqlDialect.Oracle);

        Assert.NotNull(hover);
        Assert.Contains("VARCHAR2", hover!.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SignatureHelp_OracleDialect_ReturnsOracleSignatures()
    {
        const string sql = "SELECT TO_CHAR(";
        var help = NzSignatureHelpService.GetSignatureHelp(
            sql, sql.Length,
            catalog: DialectRuntime.AuthoringCatalog(SqlDialect.Oracle),
            dialect: SqlDialect.Oracle);

        Assert.NotNull(help);
        Assert.Contains(help!.Signatures, s => s.Label.Contains("TO_CHAR", StringComparison.OrdinalIgnoreCase));
    }

    // ====== Semantic tokens ======

    [Fact]
    public void SemanticTokens_OracleDialect_ClassifiesBindVariableAsParameter()
    {
        var classifier = new NzSemanticTokenClassifier(null, null, SqlDialect.Oracle);

        var spans = classifier.Classify("SELECT * FROM t WHERE id = :NEW.ID");

        Assert.Contains(spans, s => s.Kind == SemanticTokenKind.Parameter);
    }

    [Fact]
    public void Hover_OracleDialect_ExplainsBindVariable()
    {
        const string sql = "SELECT * FROM t WHERE id = :NEW.ID";
        var offset = sql.IndexOf(':');
        var hover = NzHoverService.GetHover(
            sql, offset, null,
            catalog: DialectRuntime.AuthoringCatalog(SqlDialect.Oracle),
            dialect: SqlDialect.Oracle);

        Assert.NotNull(hover);
        Assert.Contains(":NEW.ID", hover!.Content, StringComparison.Ordinal);
        Assert.Contains("bind", hover.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SignatureHelp_OracleDialect_ResolvesQualifiedFunction()
    {
        // TO_CHAR is in the Oracle catalog; after a package-style call the short name is used.
        const string sql = "SELECT TO_CHAR(";
        var help = NzSignatureHelpService.GetSignatureHelp(
            sql, sql.Length,
            catalog: DialectRuntime.AuthoringCatalog(SqlDialect.Oracle),
            dialect: SqlDialect.Oracle);

        Assert.NotNull(help);
        Assert.Contains(help!.Signatures, s => s.Label.Contains("TO_CHAR", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OracleLexer_DialectTokenization_KeepsOracleQualifiedFunctions()
    {
        var tokens = OracleLexer.Tokenize("DBMS_OUTPUT.PUT_LINE('x')").ToArray();

        Assert.Contains(tokens, t => t.Kind == NzToken.OracleQualifiedFunction);
    }

    [Theory]
    [InlineData("oracle", SqlDialect.Oracle)]
    [InlineData("netezza", SqlDialect.Netezza)]
    [InlineData(null, SqlDialect.Netezza)]
    public void DialectRuntime_ParsesEqualsAndSpacedForms(string? name, SqlDialect expected)
    {
        Assert.Equal(expected, DialectRuntime.ParseName(name));
    }
}
