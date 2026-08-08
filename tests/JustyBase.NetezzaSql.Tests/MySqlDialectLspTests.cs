using JustyBase.NetezzaSqlLsp.Services;
using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.NetezzaSqlParser.Caching;
using JustyBase.NetezzaSqlParser.Dialects;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed class MySqlDialectLspTests
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
        var diagnostics = LintService.Lint("SELECT * FROM employees", null, SqlDialect.MySql);

        Assert.All(diagnostics, d => Assert.Equal("MySQL SQL", d.Source));
        Assert.DoesNotContain(diagnostics, d => d.Code?.StartsWith("NZ", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(diagnostics, d => d.Code?.StartsWith("MSS", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task CompletionHoverAndSignatureUseMySqlCatalog()
    {
        var completions = await CompletionService.GetCompletions("SELECT", 0, 6, null, SqlDialect.MySql);
        var hover = HoverService.GetHover("SELECT JSON FROM t", 0, 10, null, SqlDialect.MySql);
        var signatures = SignatureHelpService.GetSignatureHelp("SELECT IF(", 0, 10, SqlDialect.MySql);

        Assert.Contains(completions.Items!, i => i.Label.Equals("IF", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(hover);
        Assert.Contains("JSON", hover!.Contents!.Value);
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
        const string sql = "CREATE TABLE `orders` (id INT); SELECT * FROM `orders`;";
        var offset = sql.IndexOf("orders", StringComparison.Ordinal);

        var edit = RenameService.Rename(sql, 0, offset + 1, "order archive", "file:///mysql.sql", SqlDialect.MySql);

        Assert.NotNull(edit);
        var edits = edit!.Changes!["file:///mysql.sql"];
        Assert.Equal(2, edits.Length);
        Assert.All(edits, item => Assert.Equal("`order archive`", item.NewText));
    }

    [Theory]
    [InlineData(new[] { "--dialect=mysql" }, SqlDialect.MySql)]
    [InlineData(new[] { "--dialect", "mysql" }, SqlDialect.MySql)]
    public void LspDialectArgsParsesMySql(string[] args, SqlDialect expected)
    {
        Assert.Equal(expected, JustyBase.NetezzaSqlLsp.LspDialectArgs.Parse(args));
    }
}
