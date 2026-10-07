using JustyBase.NetezzaSqlParser.Caching;
using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Completion;
using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.NetezzaSqlParser.Linter;
using JustyBase.NetezzaSqlParser.Visitor;
using Xunit;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed class ConformanceProductionRegressionTests
{
    [Theory]
    [InlineData("SELECT * FROM t t ", "LEFT")]
    [InlineData("SELECT id FROM t ORDER BY ", "BY")]
    [InlineData("SELECT id FROM t GROUP BY ", "HAVING")]
    [InlineData("SELECT id FROM t WHERE id = 1 ", "GROUP")]
    public void Completion_OffersContextContinuationKeywords(string sql, string label)
        => Assert.Contains(new NzCompletionEngine().GetCompletions(sql, sql.Length), item => item.Label == label);

    [Fact]
    public void Completion_InsideBlockComment_IsUnavailable()
    {
        const string sql = "SELECT /* COA */";
        Assert.Empty(new NzCompletionEngine().GetCompletions(sql, sql.IndexOf(" */", StringComparison.Ordinal)));
    }

    [Fact]
    public void JoinOn_OffersMatchingVisibleColumnReferences()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("USERS", "ADMIN", "BAZA", Columns: [new ColumnInfo("ID"), new ColumnInfo("USER_ID")]));
        schema.AddTable(new TableInfo("ORDERS", "ADMIN", "BAZA", Columns: [new ColumnInfo("ID"), new ColumnInfo("USER_ID")]));
        const string sql = "SELECT * FROM BAZA.ADMIN.USERS U JOIN BAZA.ADMIN.ORDERS O ON ";
        var items = new NzCompletionEngine(schema).GetCompletions(sql, sql.Length);
        Assert.Contains(items, item => item.Label == "U.ID = O.ID" && item.Kind == CompletionKind.Reference);
        Assert.Contains(items, item => item.Label == "U.USER_ID = O.USER_ID" && item.Kind == CompletionKind.Reference);
    }

    [Fact]
    public void QualifiedInsertTarget_CompletesItsOwnColumns()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("CUSTOMERS", "SALES", "JUST_DATA",
            Columns: [new ColumnInfo("CUSTOMER_NAME")]));
        var sql = "INSERT INTO JUST_DATA.SALES.CUSTOMERS (";
        Assert.Contains(new NzCompletionEngine(schema).GetCompletions(sql, sql.Length),
            item => item.Label == "CUSTOMER_NAME");
    }

    [Fact]
    public void IncompleteFromKeyword_OffersFrom()
    {
        const string sql = "SELECT 1 FRO";
        Assert.Contains(new NzCompletionEngine().GetCompletions(sql, sql.Length),
            item => item.Label == "FROM");
    }

    [Fact]
    public void MissingColumns_ReportsMetadataWarningWithoutInventingUnknownColumn()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("EMPTY_META", "SALES", "JUST_DATA", Columns: []));
        using var engine = new LintEngine();
        var issues = engine.RunFullLint(new LintConfig(
            "SELECT E.CUSTOMER_ID FROM JUST_DATA.SALES.EMPTY_META E", schema)).Issues;
        Assert.Contains(issues, issue => issue.RuleId == "SQL005" && issue.Severity == LintSeverity.Warning);
        Assert.DoesNotContain(issues, issue => issue.RuleId == "SQL004");
    }

    [Theory]
    [InlineData("CREATE TABLE t (name VARCHAR)")]
    [InlineData("SELECT '😀', 1::VARCHAR")]
    public void CharacterTypeDiagnostic_CoversTheFullUtf16Token(string sql)
    {
        using var engine = new LintEngine();
        var issue = engine.RunFullLint(new LintConfig(sql, new InMemorySchemaProvider())).Issues.First(issue => issue.RuleId == "SQL012");
        Assert.Equal(sql.IndexOf("VARCHAR", StringComparison.Ordinal), issue.StartOffset);
        Assert.Equal(issue.StartOffset + 7, issue.EndOffset);
    }

    [Theory]
    [InlineData("DELETE FROM t", "SQL043", SqlQuickFixSafety.ReviewRequired)]
    [InlineData("CREATE TABLE t AS SELECT 1", "SQL045", SqlQuickFixSafety.ReviewRequired)]
    [InlineData("UPDATE t AS a SET id = 1", "SQL046", SqlQuickFixSafety.Safe)]
    public void QuickFix_ExplicitlyExposesSafetyAndActualEdits(string sql, string code, SqlQuickFixSafety safety)
    {
        using var engine = new LintEngine();
        var issue = engine.RunFullLint(new LintConfig(sql, new InMemorySchemaProvider())).Issues.First(issue => issue.RuleId == code);
        var action = NzLintCodeActions.GetQuickFixInfo(issue, sql);
        Assert.NotNull(action);
        Assert.Equal(safety, action.Safety);
        var edit = Assert.Single(action.Edits);
        var edited = sql[..edit.StartOffset] + edit.NewText + sql[edit.EndOffset..];
        Assert.Equal(NzLintCodeActions.GetQuickFix(issue, sql)!.Value.Apply(sql), edited);
    }

    [Fact]
    public void GroupByReminder_IsInformational()
    {
        using var engine = new LintEngine();
        var issues = engine.RunFullLint(new LintConfig("SELECT id, COUNT(*) FROM t", new InMemorySchemaProvider())).Issues;
        Assert.Contains(issues, issue => issue.RuleId == "SQL028" && issue.Severity == LintSeverity.Information);
    }

    [Fact]
    public void ConsecutiveCommaDiagnostic_CoversOnlyTheExtraComma()
    {
        using var engine = new LintEngine();
        var issues = engine.RunFullLint(new LintConfig("SELECT 1,,2", new InMemorySchemaProvider())).Issues;
        Assert.Contains(issues, issue => issue.RuleId == "PAR002"
            && issue.StartOffset == 9 && issue.EndOffset == 10);
    }

    [Theory]
    [InlineData("SELECT 1 FROM t WHERR 1=1", "PAR004")]
    [InlineData("SELECT 1 WHERE 1 IN ()", "NZL008")]
    [InlineData("SELECT 1 FETCH FIRST 5 ROWS ONLY", "NZS002")]
    [InlineData("SELECT 1 WHERE 1 = NULL", "NZL006")]
    public void FullAnalysis_EmitsSpecificAuthoringDiagnostic(string sql, string code)
    {
        using var engine = new LintEngine();
        Assert.Contains(engine.RunFullLint(new LintConfig(sql, new InMemorySchemaProvider())).Issues,
            issue => issue.RuleId == code);
    }

    [Fact]
    public void WhereWithoutFrom_IsAnEditorWarning()
    {
        using var engine = new LintEngine();
        Assert.Contains(engine.RunFullLint(new LintConfig("SELECT 1 WHERE 1=1", new InMemorySchemaProvider())).Issues,
            issue => issue.RuleId == "SQL042" && issue.Severity == LintSeverity.Warning);
    }

    [Theory]
    [InlineData("WITH x AS (")]
    [InlineData("SEL")]
    [InlineData("INSERT INTO t (id, ")]
    public void IncompleteAuthoringInput_RetainsStatementAndErrors(string sql)
    {
        using var runtime = new ParsingRuntime();
        var result = runtime.Parse(sql);
        Assert.NotEmpty(result.Statements);
        Assert.False(result.Valid);
    }
}
