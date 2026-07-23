using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Linter;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed class NzLintParityGateTests : IDisposable
{
    private readonly LintEngine _engine = new();
    private readonly ISchemaProvider _schema = SqlTestHelpers.CreateStandardMockSchema();

    public void Dispose() => _engine.Dispose();

    private static bool HasRule(IEnumerable<LintIssue> issues, string ruleId) =>
        issues.Any(i => i.RuleId == ruleId);

    private static bool HasAnyRule(IEnumerable<LintIssue> issues, params string[] ruleIds) =>
        ruleIds.Any(id => HasRule(issues, id));

    private List<LintIssue> RunCheap(string sql) => _engine.RunCheapRules(sql);

    private IReadOnlyList<LintIssue> RunFull(string sql) =>
        _engine.RunFullLint(new LintConfig(sql, Schema: _schema, DocumentUri: Guid.NewGuid().ToString())).Issues;

    private IReadOnlyList<LintIssue> RunExpensive(string sql) =>
        _engine.RunExpensiveAnalysis(new LintConfig(sql, Schema: _schema, DocumentUri: Guid.NewGuid().ToString())).Issues;

    [Fact]
    public void LintParityGate_SelectStar_ReturnsNZ001() =>
        Assert.True(HasRule(RunCheap("SELECT * FROM employees"), "NZ001"));

    [Fact]
    public void LintParityGate_ExplicitColumns_NoNZ001() =>
        Assert.False(HasRule(RunCheap("SELECT employee_id FROM employees"), "NZ001"));

    [Fact]
    public void LintParityGate_CrossJoin_ReturnsNZ004() =>
        Assert.True(HasRule(RunCheap("SELECT * FROM a CROSS JOIN b"), "NZ004"));

    [Fact]
    public void LintParityGate_LeadingWildcardLike_ReturnsNZ005() =>
        Assert.True(HasRule(RunCheap("SELECT * FROM t WHERE col LIKE '%test'"), "NZ005"));

    [Fact]
    public void LintParityGate_OrderByWithoutLimit_ReturnsNZ006() =>
        Assert.True(HasRule(RunCheap("SELECT * FROM t ORDER BY col1"), "NZ006"));

    [Fact]
    public void LintParityGate_Truncate_ReturnsNZ008() =>
        Assert.True(HasRule(RunCheap("TRUNCATE TABLE t"), "NZ008"));

    [Fact]
    public void LintParityGate_UnionWithoutAll_ReturnsNZ013() =>
        Assert.True(HasRule(RunCheap("SELECT 1 UNION SELECT 2"), "NZ013"));

    [Fact]
    public void LintParityGate_OrInJoin_ReturnsNZ014() =>
        Assert.True(HasRule(RunCheap("SELECT * FROM t1 JOIN t2 ON t1.id = t2.id OR t1.id = t2.ref_id"), "NZ014"));

    [Fact]
    public void LintParityGate_DoubleQuotedIdentifier_ReturnsNZ017() =>
        Assert.True(HasRule(RunCheap("SELECT * FROM \"MyTable\""), "NZ017"));

    [Fact]
    public void LintParityGate_InSelectSubquery_ReturnsNZ020() =>
        Assert.True(HasRule(RunCheap("SELECT * FROM t WHERE col IN (SELECT id FROM u)"), "NZ020"));

    [Fact]
    public void LintParityGate_UpdateBadColumn_ReturnsSQL004() =>
        Assert.True(HasRule(RunExpensive("UPDATE EMPLOYEES SET BAD_COL = 1"), "SQL004"));

    [Fact]
    public void LintParityGate_DeleteBadColumn_ReturnsSQL004() =>
        Assert.True(HasRule(RunExpensive("DELETE FROM EMPLOYEES WHERE BAD_COL = 1"), "SQL004"));

    [Fact]
    public void LintParityGate_DoubleCommaInSelect_ReturnsParserError() =>
        Assert.True(HasAnyRule(RunExpensive("SELECT 1,,2"), "PAR001", "PAR002"));

    [Fact]
    public void LintParityGate_SelectBadColumn_ReturnsSQL004() =>
        Assert.True(HasRule(RunExpensive("SELECT BAD_COL FROM EMPLOYEES"), "SQL004"));

    [Fact]
    public void LintParityGate_UpdateValidColumn_NoSQL004() =>
        Assert.False(HasRule(RunExpensive("UPDATE EMPLOYEES SET SALARY = 1 WHERE EMPLOYEE_ID = 1"), "SQL004"));

    [Fact]
    public void LintParityGate_ValidSelectFromEmployees_NoErrors() =>
        Assert.False(HasAnyRule(RunExpensive("SELECT EMPLOYEE_ID FROM TESTDB..EMPLOYEES"), "SQL004", "PAR001", "LEX001"));

    [Fact]
    public void LintParityGate_DeleteWithoutWhere_ReturnsSQL043() =>
        Assert.True(HasRule(RunExpensive("DELETE FROM employees"), "SQL043"));

    [Fact]
    public void LintParityGate_DeleteWithWhere_NoSQL043() =>
        Assert.False(HasRule(RunExpensive("DELETE FROM employees WHERE employee_id = 1"), "SQL043"));

    [Fact]
    public void LintParityGate_UpdateWithoutWhere_ReturnsSQL044() =>
        Assert.True(HasRule(RunExpensive("UPDATE employees SET salary = 1"), "SQL044"));

    [Fact]
    public void LintParityGate_UpdateWithWhere_NoSQL044() =>
        Assert.False(HasRule(RunExpensive("UPDATE employees SET salary = 1 WHERE employee_id = 1"), "SQL044"));

    [Fact]
    public void LintParityGate_UpdateWithAs_ReturnsSQL046() =>
        Assert.True(HasRule(RunExpensive("UPDATE employees AS e SET salary = 1"), "SQL046"));

    [Fact]
    public void LintParityGate_CaseWithoutEnd_ReturnsSQL041() =>
        Assert.True(HasAnyRule(RunExpensive("SELECT CASE WHEN 1 = 1 THEN 'yes'"), "PAR108", "PAR001"));

    [Fact]
    public void LintParityGate_CaseWithEnd_NoSQL041() =>
        Assert.False(HasRule(RunExpensive("SELECT CASE WHEN 1 = 1 THEN 'yes' ELSE 'no' END FROM employees"), "SQL041"));

    [Fact]
    public void LintParityGate_UnclosedString_ReturnsLex001()
    {
        var result = SqlTestHelpers.Validate("SELECT 'unclosed", _schema);
        Assert.Contains(result.Errors, e => e.Code is "LEX001" or "PAR110");
    }

    [Fact]
    public void LintParityGate_InvalidSelectFrom_ReturnsParserError() =>
        Assert.True(HasAnyRule(RunExpensive("SELECT FROM employees"), "PAR001", "PAR002"));

    [Fact]
    public void LintParityGate_MultiStatementSecondInvalid_ReturnsParserError()
    {
        var issues = RunExpensive("SELECT 1; SELECT FROM employees");
        Assert.Contains(issues, i => i.RuleId.StartsWith("PAR", StringComparison.Ordinal));
    }

    [Fact]
    public void LintParityGate_CleanUpdate_NoVisitorErrors() =>
        Assert.False(HasAnyRule(RunExpensive("UPDATE EMPLOYEES SET SALARY = 100 WHERE EMPLOYEE_ID = 1"), "SQL004", "SQL044"));

    [Fact]
    public void LintParityGate_CleanDelete_NoVisitorErrors() =>
        Assert.False(HasAnyRule(RunExpensive("DELETE FROM EMPLOYEES WHERE EMPLOYEE_ID = 1"), "SQL004", "SQL043"));

    [Fact]
    public void LintParityGate_CommentIgnoresSelectStarRule() =>
        Assert.False(HasRule(RunCheap("-- SELECT * FROM t\nSELECT col1 FROM t"), "NZ001"));

    [Fact]
    public void LintParityGate_StringIgnoresDeleteRule() =>
        Assert.False(HasRule(RunExpensive("SELECT 'DELETE FROM' AS msg FROM employees"), "SQL043"));

    [Fact]
    public void LintParityGate_LintParityHelper_ReturnsIssues()
    {
        var issues = SqlTestHelpers.LintParity("SELECT * FROM employees", _schema);
        Assert.Contains(issues, i => i.RuleId == "NZ001");
    }

    [Fact]
    public void LintParityGate_MergeIntoValidTable_NoSQL004() =>
        Assert.False(HasRule(RunExpensive("MERGE INTO EMPLOYEES USING DEPARTMENTS ON 1=1 WHEN MATCHED THEN UPDATE SET SALARY = 1"), "SQL004"));

    [Fact]
    public void LintParityGate_InsertIntoValidTable_NoSQL004() =>
        Assert.False(HasRule(RunExpensive("INSERT INTO EMPLOYEES (EMPLOYEE_ID) VALUES (1)"), "SQL004"));

    [Fact]
    public void LintParityGate_AlterTableDropValidColumn_NoSQL004() =>
        Assert.False(HasRule(RunExpensive("ALTER TABLE EMPLOYEES DROP STATUS"), "SQL004"));

    [Fact]
    public void LintParityGate_SelectStarWithSchema_StillNZ001() =>
        Assert.True(HasRule(RunFull("SELECT * FROM TESTDB..EMPLOYEES"), "NZ001"));

    [Fact]
    public void LintParityGate_JoinMissingOn_ReturnsStructuralOrParserIssue()
    {
        var issues = RunExpensive("SELECT * FROM EMPLOYEES JOIN DEPARTMENTS");
        Assert.NotEmpty(issues);
    }

    [Fact]
    public void LintParityGate_EmptySql_ReturnsNoIssues()
    {
        var issues = RunFull("");
        Assert.Empty(issues);
    }

    [Fact]
    public void LintParityGate_SemicolonOnly_ReturnsNoIssues()
    {
        var issues = RunFull(";");
        Assert.Empty(issues);
    }

    [Fact]
    public void LintParityGate_UnionAll_NoNZ013() =>
        Assert.False(HasRule(RunCheap("SELECT 1 UNION ALL SELECT 2"), "NZ013"));

    [Fact]
    public void LintParityGate_OrderByWithLimit_NoNZ006() =>
        Assert.False(HasRule(RunCheap("SELECT * FROM t ORDER BY col1 LIMIT 100"), "NZ006"));

    [Fact]
    public void LintParityGate_TextColumnArithmetic_ReturnsSQL025()
    {
        var provider = new InMemorySchemaProvider();
        provider.AddTable(new TableInfo("T_TEXT", Columns:
        [
            new ColumnInfo("NAME", DataType: "VARCHAR"),
            new ColumnInfo("AMOUNT", DataType: "INTEGER"),
        ]));

        var issues = _engine.RunFullLint(new LintConfig(
            "SELECT NAME + 1 FROM T_TEXT;",
            Schema: provider,
            DocumentUri: Guid.NewGuid().ToString())).Issues;

        Assert.True(HasRule(issues, "SQL025"));
    }

    [Fact]
    public void LintParityGate_NumericColumnWithStringLiteral_ReturnsSQL025()
    {
        var provider = new InMemorySchemaProvider();
        provider.AddTable(new TableInfo("T_TEXT", Columns:
        [
            new ColumnInfo("NAME", DataType: "VARCHAR"),
            new ColumnInfo("AMOUNT", DataType: "INTEGER"),
        ]));

        var issues = _engine.RunFullLint(new LintConfig(
            "SELECT AMOUNT + '1' FROM T_TEXT;",
            Schema: provider,
            DocumentUri: Guid.NewGuid().ToString())).Issues;

        Assert.True(HasRule(issues, "SQL025"));
    }

    [Fact]
    public void LintParityGate_CompatibleArithmetic_NoSQL025()
    {
        var provider = new InMemorySchemaProvider();
        provider.AddTable(new TableInfo("T_TEXT", Columns:
        [
            new ColumnInfo("AMOUNT", DataType: "INTEGER"),
        ]));

        var issues = _engine.RunFullLint(new LintConfig(
            "SELECT AMOUNT + 1 FROM T_TEXT;",
            Schema: provider,
            DocumentUri: Guid.NewGuid().ToString())).Issues;

        Assert.False(HasRule(issues, "SQL025"));
    }
}
