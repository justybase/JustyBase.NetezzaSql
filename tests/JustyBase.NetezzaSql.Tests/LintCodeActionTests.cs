using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.NetezzaSqlParser.Linter;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.NetezzaSql.Tests;

public sealed class LintCodeActionTests
{
    [Theory]
    [InlineData("NZ007", "Use uppercase", "select 1", 0, 6, "SELECT 1")]
    [InlineData("NZ011", "missing distribution", "CREATE TABLE t AS SELECT 1;", 0, 1, "CREATE TABLE t AS SELECT 1\nDISTRIBUTE ON RANDOM;")]
    [InlineData("NZ012", "", "UPDATE t AS x SET a=1", 9, 2, "UPDATE t x SET a=1")]
    [InlineData("NZ013", "", "SELECT 1 UNION SELECT 2", 9, 5, "SELECT 1 UNION ALL SELECT 2")]
    [InlineData("NZ023", "", "SELEC 1", 0, 5, "SELECT 1")]
    [InlineData("NZ021", "", "SELECT a, FROM t", 8, 1, "SELECT a FROM t")]
    [InlineData("NZ024", "", "SELECT a,", 8, 1, "SELECT a")]
    [InlineData("PAR002", "", "SELECT a,, b", 8, 1, "SELECT a, b")]
    [InlineData("PAR101", "", "WITH x (SELECT 1)", 7, 1, "WITH x AS (SELECT 1)")]
    [InlineData("SQL007", "", "SELECT * FROM DB.TABLE", 14, 8, "SELECT * FROM DB..TABLE")]
    [InlineData("SQL043", "", "UPDATE t SET a=1;", 0, 1, "UPDATE t SET a=1 WHERE 1=0;")]
    public void QuickFixes_ApplySupportedTransforms(string ruleId, string message, string sql, int start, int length, string expected)
    {
        var issue = new LintIssue(ruleId, message, LintSeverity.Warning, start, start + length);
        var fix = NzLintCodeActions.GetQuickFix(issue, sql);

        Assert.NotNull(fix);
        Assert.Equal(expected, fix.Value.Apply(sql));
    }

    [Fact]
    public void QuickFixes_RejectUnsupportedAndStaleRanges()
    {
        Assert.Null(NzLintCodeActions.GetQuickFix(new("UNKNOWN", "", LintSeverity.Warning, 0, 1), "SELECT 1"));
        Assert.Null(NzLintCodeActions.GetQuickFix(new("NZ007", "", LintSeverity.Warning, 9, 10), "SELECT 1"));
    }

    [Fact]
    public void PublicValidator_UsesSchemaOverloadsAndDisposalContract()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new("T", Columns: [new("ID")]));
        using var validator = new SqlValidator(schema);

        Assert.Empty(validator.Validate("SELECT ID FROM T", "file:///a.sql", 1).Issues);
        Assert.Empty(validator.ValidateIncremental("SELECT ID FROM T", "file:///a.sql", 2).Issues);
        Assert.Empty(validator.Validate("SELECT ID FROM T", schema).Issues);
        validator.Dispose();
        Assert.Throws<ObjectDisposedException>(() => validator.Validate("SELECT 1"));
    }
}
