using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.NetezzaSqlParser.Linter;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.NetezzaSql.Tests;

public sealed class NzLintCodeActionsParityTests
{
    [Fact]
    public void Sql007_AddsSecondDot()
    {
        var sql = "SELECT * FROM DB1.EMP;";
        var issue = new LintIssue("SQL007", "bad", LintSeverity.Error, 14, 21);
        var fix = NzLintCodeActions.GetQuickFix(issue, sql);
        Assert.NotNull(fix);
        Assert.Equal("SELECT * FROM DB1..EMP;", fix!.Value.Apply(sql));
    }

    [Fact]
    public void Nz013_AddsUnionAll()
    {
        var sql = "SELECT 1 UNION SELECT 2;";
        var issue = new LintIssue("NZ013", "prefer all", LintSeverity.Information, 9, 14);
        var fix = NzLintCodeActions.GetQuickFix(issue, sql);
        Assert.NotNull(fix);
        Assert.Contains("UNION ALL", fix!.Value.Apply(sql), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Nzp012_ReplacesElseif()
    {
        var sql = "IF x THEN ELSE ELSEIF y THEN END IF;";
        var idx = sql.IndexOf("ELSEIF", StringComparison.OrdinalIgnoreCase);
        var issue = new LintIssue("NZP012", "use elsif", LintSeverity.Warning, idx, idx + 6);
        var fix = NzLintCodeActions.GetQuickFix(issue, sql);
        Assert.NotNull(fix);
        Assert.DoesNotContain("ELSEIF", fix!.Value.Apply(sql), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ELSIF", fix.Value.Apply(sql), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Nz001_ExpandsStar_WhenSchemaPresent()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo(
            "EMP",
            "PUBLIC",
            "DB1",
            Columns: [new ColumnInfo("ID"), new ColumnInfo("NAME")]));

        var sql = "SELECT * FROM DB1.PUBLIC.EMP;";
        var star = sql.IndexOf('*');
        var issue = new LintIssue("NZ001", "select star", LintSeverity.Warning, star, star + 1);
        var fix = NzLintCodeActions.GetQuickFix(issue, sql, schema);
        Assert.NotNull(fix);
        var applied = fix!.Value.Apply(sql);
        Assert.Contains("ID, NAME", applied, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SELECT *", applied, StringComparison.Ordinal);
    }

    [Fact]
    public void IsSafeForFixAll_IncludesSql007()
    {
        Assert.True(NzLintCodeActions.IsSafeForFixAll("SQL007"));
        Assert.False(NzLintCodeActions.IsSafeForFixAll("NZ001"));
    }

    [Fact]
    public void ApplyAllSafeFixes_AppliesNz007()
    {
        var sql = "select 1 from dual;";
        var issues = new[]
        {
            new LintIssue("NZ007", "UPPERCASE", LintSeverity.Information, 0, 6),
            new LintIssue("NZ007", "UPPERCASE", LintSeverity.Information, 9, 13),
        };
        var fixedSql = NzLintCodeActions.ApplyAllSafeFixes(sql, issues);
        Assert.StartsWith("SELECT", fixedSql, StringComparison.Ordinal);
    }

    [Fact]
    public void SuggestedFix_IsHonored()
    {
        var sql = "SELEC 1;";
        var issue = new LintIssue("PAR004", "typo", LintSeverity.Error, 0, 5, SuggestedFix: "SELECT");
        var fix = NzLintCodeActions.GetQuickFix(issue, sql);
        Assert.NotNull(fix);
        Assert.Equal("SELECT 1;", fix!.Value.Apply(sql));
    }
}
