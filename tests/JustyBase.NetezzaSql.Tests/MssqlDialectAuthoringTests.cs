using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.NetezzaSqlParser.Caching;
using JustyBase.NetezzaSqlParser.Completion;
using JustyBase.NetezzaSqlParser.Dialects;

namespace JustyBase.Tests.NetezzaSqlParser;

/// <summary>
/// MSSQL dialect switching tests against the shared parser authoring stack (mirror of Db2DialectAuthoringTests).
/// </summary>
public sealed class MssqlDialectAuthoringTests
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

    private static IReadOnlyList<JustyBase.NetezzaSqlParser.Linter.LintIssue> CheckMssql(string sql) =>
        DialectRuntime.QualityRules(SqlDialect.Mssql).AllRules.SelectMany(r => r.Check(sql)).ToList();

    [Fact]
    public void Lint_MssqlDialect_ReportsMss001SelectStar()
    {
        var issues = CheckMssql("SELECT * FROM employees");
        Assert.Contains(issues, d => d.RuleId == "MSS001");
        Assert.Equal("MSSQL SQL", DialectRuntime.DiagnosticSource(SqlDialect.Mssql));
    }

    [Fact]
    public void Lint_MssqlDialect_DoesNotReportNzOrDb2Rules()
    {
        var issues = CheckMssql("SELECT * FROM employees");
        Assert.Contains(issues, d => d.RuleId == "MSS001");
        Assert.DoesNotContain(issues, d => d.RuleId.StartsWith("NZ", StringComparison.Ordinal));
        Assert.DoesNotContain(issues, d => d.RuleId.StartsWith("DB", StringComparison.Ordinal));
        Assert.DoesNotContain(issues, d => d.RuleId.StartsWith("ORA", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Completion_MssqlDialect_OffersMssqlFunctions()
    {
        var result = await CompletionOrchestrator.GetCompletions("SELECT", 6, null, SqlDialect.Mssql);
        Assert.Contains(result.EngineItems, i => i.Label.Equals("ISNULL", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.EngineItems, i => i.Label.Equals("GETDATE", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Hover_MssqlDialect_ExplainsMssqlDataType()
    {
        const string sql = "SELECT NVARCHAR FROM t";
        var hover = NzHoverService.GetHover(
            sql, 10, null,
            catalog: DialectRuntime.AuthoringCatalog(SqlDialect.Mssql),
            dialect: SqlDialect.Mssql);
        Assert.NotNull(hover);
        Assert.Contains("NVARCHAR", hover!.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SignatureHelp_MssqlDialect_ReturnsMssqlSignatures()
    {
        const string sql = "SELECT ISNULL(";
        var help = NzSignatureHelpService.GetSignatureHelp(
            sql, sql.Length,
            catalog: DialectRuntime.AuthoringCatalog(SqlDialect.Mssql),
            dialect: SqlDialect.Mssql);
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
    [InlineData("mssql", SqlDialect.Mssql)]
    [InlineData("sqlserver", SqlDialect.Mssql)]
    [InlineData("db2", SqlDialect.Db2)]
    [InlineData("oracle", SqlDialect.Oracle)]
    [InlineData("netezza", SqlDialect.Netezza)]
    [InlineData(null, SqlDialect.Netezza)]
    public void DialectRuntime_ParsesMssqlForms(string? name, SqlDialect expected)
    {
        Assert.Equal(expected, DialectRuntime.ParseName(name));
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
