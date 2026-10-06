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

    [Fact]
    public void Nz004_CrossJoinFix_ProducesValidInnerJoinWithPredicate()
    {
        var sql = "SELECT * FROM a CROSS JOIN b;";
        var start = sql.IndexOf("CROSS JOIN", StringComparison.Ordinal);
        var issue = new LintIssue("NZ004", "cross join", LintSeverity.Warning, start, start + "CROSS JOIN".Length);

        var fix = NzLintCodeActions.GetQuickFix(issue, sql);

        Assert.NotNull(fix);
        var applied = fix!.Value.Apply(sql);
        Assert.Contains("INNER JOIN", applied, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ON 1=1", applied, StringComparison.OrdinalIgnoreCase);
        Assert.True(applied.IndexOf("b", StringComparison.OrdinalIgnoreCase) < applied.IndexOf("ON 1=1", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Nz010_AddsAliasAndRewritesQualifiedReferences()
    {
        var sql = "SELECT * FROM t1 JOIN t2 ON t1.id = t2.id;";
        var start = sql.IndexOf("JOIN", StringComparison.OrdinalIgnoreCase);
        var end = start + "JOIN t2 ON".Length;
        var issue = new LintIssue("NZ010", "NZ010: Table 't2' in JOIN has no alias", LintSeverity.Information, start, end);

        var fix = NzLintCodeActions.GetQuickFix(issue, sql);

        Assert.NotNull(fix);
        var applied = fix!.Value.Apply(sql);
        Assert.Contains("JOIN t2 t3 ON", applied, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("t3.id", applied, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("t2.id", applied, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("SELECT * FROM a CROSS JOIN b WHERE a.id = 1;", "b ON 1=1 WHERE")]
    [InlineData("SELECT * FROM a CROSS JOIN b JOIN c ON c.id = a.id;", "b ON 1=1 JOIN c")]
    public void Nz004_CrossJoinFix_InsertsPredicateAtJoinSite(string sql, string expectedFragment)
    {
        var start = sql.IndexOf("CROSS JOIN", StringComparison.OrdinalIgnoreCase);
        var issue = new LintIssue("NZ004", "cross join", LintSeverity.Warning, start, start + 10);

        var fix = NzLintCodeActions.GetQuickFix(issue, sql);

        Assert.NotNull(fix);
        Assert.Contains(expectedFragment, fix!.Value.Apply(sql), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Nz010_DoesNotRewriteQualifiedReferencesInOtherStatements()
    {
        var sql = "SELECT t2.id FROM t1 JOIN t2 ON t1.id = t2.id; SELECT t2.id FROM t2;";
        var start = sql.IndexOf("JOIN t2 ON", StringComparison.OrdinalIgnoreCase);
        var issue = new LintIssue("NZ010", "NZ010: Table 't2' in JOIN has no alias", LintSeverity.Information, start, start + "JOIN t2 ON".Length);

        var fix = NzLintCodeActions.GetQuickFix(issue, sql);

        Assert.NotNull(fix);
        var applied = fix!.Value.Apply(sql);
        Assert.Contains("SELECT t3.id FROM t1 JOIN t2 t3 ON t1.id = t3.id;", applied, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("SELECT t2.id FROM t2;", applied, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Nz010_QualifiedTableAliasFix_RewritesBasenameReferences()
    {
        var sql = "SELECT customer.id FROM orders o JOIN sales.customer ON o.customer_id = sales.customer.id;";
        var start = sql.IndexOf("JOIN sales.customer ON", StringComparison.OrdinalIgnoreCase);
        var issue = new LintIssue("NZ010", "NZ010: Table 'sales.customer' in JOIN has no alias", LintSeverity.Information, start, start + "JOIN sales.customer ON".Length);

        var fix = NzLintCodeActions.GetQuickFix(issue, sql);

        Assert.NotNull(fix);
        var applied = fix!.Value.Apply(sql);
        Assert.Contains("SELECT t1.id", applied, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("JOIN sales.customer t1 ON", applied, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("o.customer_id = t1.id", applied, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("customer.id", applied, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyAllSafeFixes_UsesOriginalOffsetsOnlyOnce()
    {
        const string sql = "SELECT 1;";
        var issue = new LintIssue("PAR101", "missing AS", LintSeverity.Warning, 0, 0);

        var fixedSql = NzLintCodeActions.ApplyAllSafeFixes(sql, new[] { issue }, maxPasses: 3);

        Assert.Equal("AS SELECT 1;", fixedSql);
    }
}
