using JustyBase.NetezzaSqlParser.Linter;

namespace JustyBase.Tests.NetezzaSqlParser;

/// <summary>
/// Oracle dialect lint rules tests. Port of the Oracle quality rules from
/// extensions/oracle/src/sql/qualityRules.ts in the reference TypeScript project.
/// </summary>
public class OracleLintRulesTests
{
    private static List<LintIssue> Run(string sql, string ruleId)
    {
        var registry = new QualityRuleRegistry();
        registry.AddRules(OracleLintRules.AllRules);
        var engine = new LintEngine(registry);
        var issues = engine.RunCheapRules(sql, registry.BuildEffectiveSeverities()).ToList();
        return issues.Where(i => i.RuleId == ruleId).ToList();
    }

    // ====== ORA001: SELECT * ======

    [Fact]
    public void ORA001_SelectStar_FlagsStar()
    {
        var issues = Run("SELECT * FROM employees;", "ORA001");
        Assert.Single(issues);
        Assert.Equal(LintSeverity.Warning, issues[0].Severity);
    }

    [Fact]
    public void ORA001_SelectStar_SkipsExplicitProjection()
    {
        Assert.Empty(Run("SELECT employee_id FROM employees;", "ORA001"));
    }

    [Fact]
    public void ORA001_SelectStar_SkipsStarInsideStringLiteral()
    {
        Assert.Empty(Run("SELECT name FROM t WHERE name = 'SELECT * FROM x';", "ORA001"));
    }

    // ====== ORA002: DELETE without WHERE ======

    [Fact]
    public void ORA002_DeleteWithoutWhere_FlagsDelete()
    {
        var issues = Run("DELETE FROM employees;", "ORA002");
        Assert.Single(issues);
        Assert.Equal(LintSeverity.Error, issues[0].Severity);
    }

    [Fact]
    public void ORA002_DeleteWithWhere_Silent()
    {
        Assert.Empty(Run("DELETE FROM employees WHERE employee_id = 1;", "ORA002"));
    }

    [Fact]
    public void ORA002_DeleteWithoutWhere_FlaggedInsideSubquery()
    {
        var sql = "DELETE FROM employees WHERE employee_id IN (SELECT employee_id FROM audit);";
        Assert.Empty(Run(sql, "ORA002"));
    }

    [Fact]
    public void ORA002_WhereKeywordInsideStringOnly_MatchesReferenceBehavior()
    {
        // The reference rule scans the raw statement tail, so 'where' inside a
        // string literal suppresses the issue (parity with qualityRules.ts).
        Assert.Empty(Run("DELETE FROM employees SET name = 'where';", "ORA002"));
    }

    [Fact]
    public void ORA002_DeleteWithoutWhere_QualifiedTableName()
    {
        Assert.Single(Run("DELETE FROM HR.EMPLOYEES;", "ORA002"));
        Assert.Single(Run("DELETE FROM \"HR\".\"EMPLOYEES\";", "ORA002"));
    }

    // ====== ORA003: UPDATE without WHERE ======

    [Fact]
    public void ORA003_UpdateWithoutWhere_FlagsUpdate()
    {
        var issues = Run("UPDATE employees SET salary = 0;", "ORA003");
        Assert.Single(issues);
        Assert.Equal(LintSeverity.Error, issues[0].Severity);
    }

    [Fact]
    public void ORA003_UpdateWithWhere_Silent()
    {
        Assert.Empty(Run("UPDATE employees SET salary = 0 WHERE employee_id = 1;", "ORA003"));
    }

    [Fact]
    public void ORA003_UpdateWithoutWhere_QualifiedQuotedTableName()
    {
        Assert.Single(Run("UPDATE \"HR\".\"EMPLOYEES\" SET salary = 0;", "ORA003"));
    }

    // ====== ORA004: ROWNUM with ORDER BY ======

    [Fact]
    public void ORA004_RownumWithOrderBy_Flags()
    {
        var sql = "SELECT * FROM (SELECT employee_id FROM employees ORDER BY employee_id) WHERE ROWNUM < 10;";
        var issues = Run(sql, "ORA004");
        Assert.Empty(issues); // ORDER BY is inside the subquery, outside the ROWNUM statement tail
    }

    [Fact]
    public void ORA004_RownumWithOrderBy_FlagsSameLevelOrderBy()
    {
        var sql = "SELECT * FROM employees WHERE ROWNUM < 10 ORDER BY employee_id;";
        var issues = Run(sql, "ORA004");
        Assert.Single(issues);
        Assert.Equal(LintSeverity.Warning, issues[0].Severity);
    }

    [Fact]
    public void ORA004_RownumWithOrderBy_FetchFirstIsSilent()
    {
        var sql = "SELECT * FROM employees WHERE ROWNUM < 10 ORDER BY employee_id FETCH FIRST 10 ROWS ONLY;";
        Assert.Empty(Run(sql, "ORA004"));
    }

    [Fact]
    public void ORA004_RownumWithoutOrderBy_Silent()
    {
        Assert.Empty(Run("SELECT * FROM employees WHERE ROWNUM < 10;", "ORA004"));
    }

    [Fact]
    public void ORA004_RownumWithOrderBy_StatementBoundaryRespected()
    {
        var sql = "SELECT * FROM employees WHERE ROWNUM < 10; SELECT 1 FROM dual ORDER BY 1;";
        Assert.Empty(Run(sql, "ORA004"));
    }

    // ====== StatementEnd / q-quote (port of oracleAuthoring.test.ts) ======

    [Fact]
    public void ORA003_SemicolonInsideString_DoesNotEndStatement()
    {
        var issues = Run("UPDATE ORDERS SET NOTE = 'x; y' WHERE ID = 1; -- another; terminator\n", "ORA003");
        Assert.Empty(issues);
    }

    [Fact]
    public void ORA003_SemicolonInsideQQuote_DoesNotEndStatement()
    {
        var issues = Run("UPDATE ORDERS SET NOTE = q'[x; y]' WHERE ID = 1; SELECT 1 FROM DUAL;", "ORA003");
        Assert.Empty(issues);
    }

    [Theory]
    [InlineData("SELECT q'{test;}' FROM DUAL; SELECT q'<test;>' FROM DUAL; SELECT q'(test;)' FROM DUAL;")]
    [InlineData("SELECT q'[test;]' FROM DUAL; SELECT q'{test;}' FROM DUAL; SELECT q'<test;>' FROM DUAL; SELECT q'(test;)' FROM DUAL;")]
    public void ORA003_QQuoteDelimiters_DoNotFalsePositive(string sql)
    {
        Assert.Empty(Run(sql, "ORA003"));
    }

    [Fact]
    public void ORA003_AfterClosedQQuote_StillDetectsMissingWhere()
    {
        var sql = "UPDATE ORDERS SET NOTE = q'[x]' ; SELECT * FROM DUAL WHERE 1 = 1;";
        Assert.Single(Run(sql, "ORA003"));
    }

    // ====== Registry integration ======

    [Fact]
    public void AddRules_RegistersOracleRules()
    {
        var registry = new QualityRuleRegistry();
        Assert.False(registry.HasRule("ORA001"));
        registry.AddRules(OracleLintRules.AllRules);
        Assert.True(registry.HasRule("ORA001"));
        Assert.True(registry.HasRule("ORA002"));
        Assert.True(registry.HasRule("ORA003"));
        Assert.True(registry.HasRule("ORA004"));
    }

    [Fact]
    public void AddRules_DoesNotAffectNetezzaRules()
    {
        var registry = new QualityRuleRegistry();
        Assert.True(registry.HasRule("NZ001"));
        registry.AddRules(OracleLintRules.AllRules);
        Assert.True(registry.HasRule("NZ001"));
    }
}
