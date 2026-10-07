using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.NetezzaSqlParser.Caching;
using JustyBase.NetezzaSqlParser.Completion;
using JustyBase.NetezzaSqlParser.Dialects;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed class MySqlDialectAuthoringTests
{
    [Fact]
    public void ParseSession_MySqlDialect_ParsesBackticksLimitAndDuplicateUpdate()
    {
        using var session = new DocumentParseSession(SqlDialect.MySql);
        var result = session.GetOrParse(
            "INSERT IGNORE INTO `TESTDB`.`departments` (id) VALUES (1) ON DUPLICATE KEY UPDATE id = 2;");

        Assert.True(result.Valid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ParseSession_MySqlDialectRejectsThreePartNames()
    {
        using var session = new DocumentParseSession(SqlDialect.MySql);
        var result = session.GetOrParse("SELECT * FROM a.b.c;");

        Assert.False(result.Valid);
        Assert.Contains(result.Errors, e => e.Code == "PAR001");
    }

    [Fact]
    public void Lint_MySqlDialectHasNoOtherDialectRules()
    {
        var issues = DialectRuntime.QualityRules(SqlDialect.MySql).AllRules
            .SelectMany(r => r.Check("SELECT * FROM employees")).ToList();

        Assert.Equal("MySQL SQL", DialectRuntime.DiagnosticSource(SqlDialect.MySql));
        Assert.DoesNotContain(issues, d => d.RuleId.StartsWith("NZ", StringComparison.Ordinal));
        Assert.DoesNotContain(issues, d => d.RuleId.StartsWith("MSS", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CompletionHoverAndSignatureUseMySqlCatalog()
    {
        var catalog = DialectRuntime.AuthoringCatalog(SqlDialect.MySql);
        var completions = await CompletionOrchestrator.GetCompletions("SELECT", 6, null, SqlDialect.MySql);
        const string hoverSql = "SELECT JSON FROM t";
        var hover = NzHoverService.GetHover(hoverSql, 10, null, catalog: catalog, dialect: SqlDialect.MySql);
        const string sigSql = "SELECT IF(";
        var signatures = NzSignatureHelpService.GetSignatureHelp(sigSql, sigSql.Length, catalog: catalog, dialect: SqlDialect.MySql);

        Assert.Contains(completions.EngineItems, i => i.Label.Equals("IF", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(hover);
        Assert.Contains("JSON", hover!.Content, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(signatures);
        Assert.Contains(signatures!.Signatures, s => s.Label.Contains("IF", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SemanticTokensRecognizeBackticksAndHashComments()
    {
        var classifier = new NzSemanticTokenClassifier(null, null, SqlDialect.MySql);
        var spans = classifier.Classify("SELECT `id` FROM `orders` # comment");

        Assert.NotEmpty(spans);
    }

    [Fact]
    public void Rename_MySqlBacktickIdentifiersKeepsBackticksInEdits()
    {
        const string sql = "SELECT `a`.id FROM `orders` AS `a` WHERE `a`.id > 0;";
        var first = sql.IndexOf("`a`", StringComparison.Ordinal);
        var second = sql.IndexOf("`a`", first + 1, StringComparison.Ordinal);
        var third = sql.IndexOf("`a`", second + 1, StringComparison.Ordinal);
        var occurrences = new[]
        {
            new JustyBase.NetezzaSqlParser.Authoring.SymbolOccurrence(1, "a", JustyBase.NetezzaSqlParser.Authoring.SqlSymbolKind.Alias, first, first + 3, true, 1),
            new JustyBase.NetezzaSqlParser.Authoring.SymbolOccurrence(2, "a", JustyBase.NetezzaSqlParser.Authoring.SqlSymbolKind.Alias, second, second + 3, false, 1),
            new JustyBase.NetezzaSqlParser.Authoring.SymbolOccurrence(3, "a", JustyBase.NetezzaSqlParser.Authoring.SqlSymbolKind.Alias, third, third + 3, false, 1),
        };

        var renamed = NzRenameService.ApplyRename(sql, new JustyBase.NetezzaSqlParser.Authoring.SqlRenameInfo("a", JustyBase.NetezzaSqlParser.Authoring.SqlSymbolKind.Alias, occurrences), "order archive");
        Assert.Contains("`order archive`", renamed, StringComparison.Ordinal);
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(renamed, "`order archive`", System.Text.RegularExpressions.RegexOptions.None).Count);
    }

    [Theory]
    [InlineData("mysql", SqlDialect.MySql)]
    [InlineData(null, SqlDialect.Netezza)]
    public void DialectRuntime_ParsesMySql(string? name, SqlDialect expected)
    {
        Assert.Equal(expected, DialectRuntime.ParseName(name));
    }
}
