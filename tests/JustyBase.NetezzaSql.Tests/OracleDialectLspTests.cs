using JustyBase.NetezzaSqlLsp.Services;
using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.NetezzaSqlParser.Caching;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Lexer;

namespace JustyBase.Tests.NetezzaSqlParser;

/// <summary>
/// LSP-level dialect switching tests: the Oracle dialect composes the Oracle
/// lexer/parser into the parse session and LSP services (lint rules, authoring
/// catalogs), while the Netezza dialect keeps the shared pipeline unchanged.
/// </summary>
public sealed class OracleDialectLspTests
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

    // ====== LintService dialect rules ======

    [Fact]
    public void Lint_OracleDialect_ReportsOra001SelectStar()
    {
        var diagnostics = LintService.Lint("SELECT * FROM employees", null, SqlDialect.Oracle);

        Assert.Contains(diagnostics, d => d.Code == "ORA001");
    }

    [Fact]
    public void Lint_OracleDialect_ReportsOra002DeleteWithoutWhere()
    {
        var diagnostics = LintService.Lint("DELETE FROM employees", null, SqlDialect.Oracle);

        Assert.Contains(diagnostics, d => d.Code == "ORA002");
    }

    [Fact]
    public void Lint_NetezzaDialect_DoesNotReportOraRules()
    {
        var diagnostics = LintService.Lint("SELECT * FROM employees", null, SqlDialect.Netezza);

        Assert.DoesNotContain(diagnostics, d => d.Code?.StartsWith("ORA", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Lint_OracleDialect_DoesNotReportNzRules()
    {
        // SELECT * triggers ORA001; NZ001 must not fire in Oracle mode.
        var diagnostics = LintService.Lint("SELECT * FROM employees", null, SqlDialect.Oracle);

        Assert.Contains(diagnostics, d => d.Code == "ORA001");
        Assert.DoesNotContain(diagnostics, d => d.Code?.StartsWith("NZ", StringComparison.Ordinal) == true);
        Assert.All(diagnostics, d => Assert.Equal("Oracle SQL", d.Source));
    }

    // ====== Completion + hover + signature catalogs ======

    [Fact]
    public async Task Completion_OracleDialect_OffersOracleFunctions()
    {
        var completions = await CompletionService.GetCompletions("SELECT", 0, 6, null, SqlDialect.Oracle);

        Assert.NotNull(completions.Items);
        Assert.Contains(completions.Items!, i => i.Label == "NVL");
        Assert.Contains(completions.Items!, i => i.Label == "TO_DATE");
    }

    [Fact]
    public void Hover_OracleDialect_ExplainsOracleDataType()
    {
        var hover = HoverService.GetHover("SELECT VARCHAR2(10) FROM t", 0, 10, null, SqlDialect.Oracle);

        Assert.NotNull(hover);
        Assert.NotNull(hover!.Contents);
        Assert.Contains("VARCHAR2", hover.Contents!.Value);
    }

    [Fact]
    public void SignatureHelp_OracleDialect_ReturnsOracleSignatures()
    {
        var help = SignatureHelpService.GetSignatureHelp("SELECT TO_CHAR(", 0, 16, SqlDialect.Oracle);

        Assert.NotNull(help);
        Assert.NotNull(help!.Signatures);
        Assert.Contains(help.Signatures, s => s.Label.Contains("TO_CHAR", StringComparison.OrdinalIgnoreCase));
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
        var sql = "SELECT * FROM t WHERE id = :NEW.ID";
        var offset = sql.IndexOf(':');
        var hover = HoverService.GetHover(sql, 0, offset, null, SqlDialect.Oracle);

        Assert.NotNull(hover);
        Assert.Contains(":NEW.ID", hover!.Contents!.Value);
        Assert.Contains("bind", hover.Contents.Value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SignatureHelp_OracleDialect_ResolvesQualifiedFunction()
    {
        // TO_CHAR is in the Oracle catalog; after a package-style call the short name is used.
        var help = SignatureHelpService.GetSignatureHelp("SELECT TO_CHAR(", 0, 16, SqlDialect.Oracle);

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
    [InlineData(new[] { "--dialect=oracle" }, SqlDialect.Oracle)]
    [InlineData(new[] { "--dialect", "oracle" }, SqlDialect.Oracle)]
    [InlineData(new[] { "--dialect=netezza" }, SqlDialect.Netezza)]
    [InlineData(new[] { "--dialect", "netezza" }, SqlDialect.Netezza)]
    [InlineData(new string[0], SqlDialect.Netezza)]
    public void LspDialectArgs_ParsesEqualsAndSpacedForms(string[] args, SqlDialect expected)
    {
        Assert.Equal(expected, JustyBase.NetezzaSqlLsp.LspDialectArgs.Parse(args));
    }
}
