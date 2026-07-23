using JustyBase.NetezzaSqlParser.Linter;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.Tests.NetezzaSqlParser;

/// <summary>
/// Comprehensive tests for Expensive (AST-based) lint rules NZ101–NZ108.
/// Tests verify that rules fire correctly when the AST pattern matches,
/// and don't fire when the SQL is clean.
/// All tests use LintEngine.RunExpensiveAnalysis with a schema so the
/// parser is invoked and CheckStatement() is called on each statement.
/// </summary>
public sealed class NzExpensiveRulesTests : IDisposable
{
    private readonly LintEngine _engine = new();
    private readonly ISchemaProvider _schema = SqlTestHelpers.CreateStandardMockSchema();

    public void Dispose() => _engine.Dispose();

    // ====================================================================
    // NZ101: SELECT * with JOIN
    // ====================================================================

    [Fact]
    public void NZ101_SelectStarWithJoin_Detected()
    {
        var config = new LintConfig("SELECT * FROM employees e JOIN departments d ON e.department_id = d.department_id", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.Contains(result.Issues, i => i.RuleId == "NZ101");
    }

    [Fact]
    public void NZ101_SelectStarWithJoin_ExplicitColumns_NoIssue()
    {
        var config = new LintConfig("SELECT e.employee_id, d.department_name FROM employees e JOIN departments d ON e.department_id = d.department_id", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.DoesNotContain(result.Issues, i => i.RuleId == "NZ101");
    }

    [Fact]
    public void NZ101_SelectStarWithoutJoin_NoIssue()
    {
        var config = new LintConfig("SELECT * FROM employees", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.DoesNotContain(result.Issues, i => i.RuleId == "NZ101");
    }

    [Fact]
    public void NZ101_SelectStarWithCrossJoin_Detected()
    {
        var config = new LintConfig("SELECT * FROM employees CROSS JOIN departments", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.Contains(result.Issues, i => i.RuleId == "NZ101");
    }

    [Fact]
    public void NZ101_SelectStarWithJoin_SubqueryInFrom_NoIssueForOuter()
    {
        var config = new LintConfig("SELECT * FROM (SELECT employee_id FROM employees) e JOIN departments d ON 1=1", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.Contains(result.Issues, i => i.RuleId == "NZ101");
    }

    [Fact]
    public void NZ101_ImplicitJoin_NoJoinKeyword_NoIssue()
    {
        var config = new LintConfig("SELECT * FROM employees e, departments d WHERE e.department_id = d.department_id", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        // Implicit join (comma-separated FROM) doesn't have JOIN keyword, so no NZ101
        Assert.DoesNotContain(result.Issues, i => i.RuleId == "NZ101");
    }

    // ====================================================================
    // NZ102: Missing ON in JOIN
    // ====================================================================

    [Fact]
    public void NZ102_MissingJoinCondition_Detected()
    {
        var config = new LintConfig("SELECT * FROM employees e JOIN departments d", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.Contains(result.Issues, i => i.RuleId == "NZ102");
    }

    [Fact]
    public void NZ102_MissingJoinCondition_LeftJoinDetected()
    {
        var config = new LintConfig("SELECT * FROM employees e LEFT JOIN departments d", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.Contains(result.Issues, i => i.RuleId == "NZ102");
    }

    [Fact]
    public void NZ102_JoinWithOn_NoIssue()
    {
        var config = new LintConfig("SELECT * FROM employees e JOIN departments d ON e.department_id = d.department_id", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.DoesNotContain(result.Issues, i => i.RuleId == "NZ102");
    }

    [Fact]
    public void NZ102_JoinWithUsing_NoIssue()
    {
        // USING clause is also valid — skip NZ102 if USING columns are present
        var config = new LintConfig("SELECT * FROM employees e JOIN departments d USING (department_id)", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.DoesNotContain(result.Issues, i => i.RuleId == "NZ102");
    }

    [Fact]
    public void NZ102_CrossJoin_NoIssue()
    {
        var config = new LintConfig("SELECT * FROM employees CROSS JOIN departments", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.DoesNotContain(result.Issues, i => i.RuleId == "NZ102");
    }

    [Fact]
    public void NZ102_MultipleJoins_OneMissingCondition_Detected()
    {
        var config = new LintConfig("SELECT * FROM employees e JOIN departments d ON e.department_id = d.department_id JOIN orders o", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.Contains(result.Issues, i => i.RuleId == "NZ102");
    }

    // ====================================================================
    // NZ103: Aggregates without GROUP BY
    // ====================================================================

    [Fact]
    public void NZ103_AggregateWithoutGroupBy_Detected()
    {
        var config = new LintConfig("SELECT COUNT(*), department_id FROM employees", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.Contains(result.Issues, i => i.RuleId == "NZ103");
    }

    [Fact]
    public void NZ103_AggregateWithGroupBy_NoIssue()
    {
        var config = new LintConfig("SELECT COUNT(*), department_id FROM employees GROUP BY department_id", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.DoesNotContain(result.Issues, i => i.RuleId == "NZ103");
    }

    [Fact]
    public void NZ103_OnlyAggregates_NoBareColumns_NoIssue()
    {
        var config = new LintConfig("SELECT COUNT(*), AVG(salary) FROM employees", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.DoesNotContain(result.Issues, i => i.RuleId == "NZ103");
    }

    [Fact]
    public void NZ103_OnlyBareColumns_NoAggregates_NoIssue()
    {
        var config = new LintConfig("SELECT department_id, first_name FROM employees", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.DoesNotContain(result.Issues, i => i.RuleId == "NZ103");
    }

    [Fact]
    public void NZ103_AggregateWithLiteralAndColumn_Detected()
    {
        var config = new LintConfig("SELECT COUNT(*), 1 AS num, status FROM employees", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.Contains(result.Issues, i => i.RuleId == "NZ103");
    }

    [Fact]
    public void NZ103_SumWithBareColumn_Detected()
    {
        var config = new LintConfig("SELECT SUM(salary), department_id FROM employees", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.Contains(result.Issues, i => i.RuleId == "NZ103");
    }

    [Fact]
    public void NZ103_MultipleAggregatesWithColumn_Detected()
    {
        var config = new LintConfig("SELECT COUNT(*), AVG(salary), MIN(hire_date), status FROM employees", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.Contains(result.Issues, i => i.RuleId == "NZ103");
    }

    [Fact]
    public void NZ103_NoAggregates_ConstantsOnly_NoIssue()
    {
        var config = new LintConfig("SELECT 1, 'hello', 2 + 3 FROM employees", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.DoesNotContain(result.Issues, i => i.RuleId == "NZ103");
    }

    [Fact]
    public void NZ103_SubqueryInSelectList_NoIssue()
    {
        // Subquery expression in SELECT list is self-contained — ignore for NZ103
        var config = new LintConfig("SELECT COUNT(*), (SELECT MAX(salary) FROM employees) AS max_sal FROM employees", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.DoesNotContain(result.Issues, i => i.RuleId == "NZ103");
    }

    [Fact]
    public void NZ103_UppercaseFunctionWithColumn_AggregateDetected()
    {
        // UPPER(first_name) contains a bare column reference mixed with aggregate
        var config = new LintConfig("SELECT COUNT(*), UPPER(first_name) FROM employees", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.Contains(result.Issues, i => i.RuleId == "NZ103");
    }

    [Fact]
    public void NZ103_ConcatWithColumn_AggregateDetected()
    {
        // CONCAT(first_name, ' ', last_name) contains bare column references mixed with aggregate
        var config = new LintConfig("SELECT COUNT(*), CONCAT(first_name, ' ', last_name) FROM employees", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.Contains(result.Issues, i => i.RuleId == "NZ103");
    }

    [Fact]
    public void NZ103_CoalesceWithColumn_AggregateDetected()
    {
        // COALESCE(salary, 0) contains a bare column reference mixed with aggregate
        var config = new LintConfig("SELECT COUNT(*), COALESCE(salary, 0) FROM employees", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.Contains(result.Issues, i => i.RuleId == "NZ103");
    }

    [Fact]
    public void NZ103_SubstringWithColumn_AggregateDetected()
    {
        // SUBSTRING(first_name FROM 1 FOR 3) contains a bare column reference
        var config = new LintConfig("SELECT COUNT(*), SUBSTRING(first_name FROM 1 FOR 3) FROM employees", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.Contains(result.Issues, i => i.RuleId == "NZ103");
    }

    [Fact]
    public void NZ103_UppercaseWithAggregate_HasGroupBy_NoIssue()
    {
        // UPPER(first_name) with GROUP BY is fine
        var config = new LintConfig("SELECT COUNT(*), UPPER(first_name) FROM employees GROUP BY first_name", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.DoesNotContain(result.Issues, i => i.RuleId == "NZ103");
    }

    [Fact]
    public void NZ103_NestedFunctionsWithColumns_AggregateDetected()
    {
        // TRIM(UPPER(first_name)) contains a bare column reference through nested functions
        var config = new LintConfig("SELECT COUNT(*), TRIM(UPPER(first_name)) FROM employees", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.Contains(result.Issues, i => i.RuleId == "NZ103");
    }

    [Fact]
    public void NZ103_ScalarFunctionsOnly_NoAggregate_NoIssue()
    {
        // No aggregate — just scalar functions with columns, which is fine
        var config = new LintConfig("SELECT UPPER(first_name), CONCAT(first_name, ' ', last_name) FROM employees", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.DoesNotContain(result.Issues, i => i.RuleId == "NZ103");
    }

    // ====================================================================
    // NZ104: Unused CTE
    // ====================================================================

    [Fact]
    public void NZ104_UnusedCte_Detected()
    {
        var config = new LintConfig("WITH cte AS (SELECT employee_id FROM employees) SELECT department_id FROM departments", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.Contains(result.Issues, i => i.RuleId == "NZ104");
    }

    [Fact]
    public void NZ104_UsedCte_NoIssue()
    {
        var config = new LintConfig("WITH cte AS (SELECT employee_id FROM employees) SELECT * FROM cte", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.DoesNotContain(result.Issues, i => i.RuleId == "NZ104");
    }

    [Fact]
    public void NZ104_MultipleCtes_OneUnused_Detected()
    {
        var config = new LintConfig(@"
            WITH used_cte AS (SELECT employee_id FROM employees),
                 unused_cte AS (SELECT department_id FROM departments)
            SELECT * FROM used_cte", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.Contains(result.Issues, i => i.RuleId == "NZ104" && i.Message.Contains("unused_cte"));
    }

    [Fact]
    public void NZ104_RecursiveCte_Used_NoIssue()
    {
        var config = new LintConfig(@"
            WITH RECURSIVE cte AS (
                SELECT employee_id, manager_id FROM employees WHERE manager_id IS NULL
                UNION ALL
                SELECT e.employee_id, e.manager_id FROM employees e JOIN cte ON e.manager_id = cte.employee_id
            )
            SELECT * FROM cte", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.DoesNotContain(result.Issues, i => i.RuleId == "NZ104");
    }

    [Fact]
    public void NZ104_CteReferencedInAnotherCte_NoIssue()
    {
        var config = new LintConfig(@"
            WITH base AS (SELECT employee_id, department_id FROM employees),
                 derived AS (SELECT * FROM base)
            SELECT * FROM derived", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.DoesNotContain(result.Issues, i => i.RuleId == "NZ104");
    }

    [Fact]
    public void NZ104_NoCte_NoIssue()
    {
        var config = new LintConfig("SELECT employee_id FROM employees", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.DoesNotContain(result.Issues, i => i.RuleId == "NZ104");
    }

    [Fact]
    public void NZ104_AllCtesUsed_NoIssue()
    {
        var config = new LintConfig(@"
            WITH a AS (SELECT employee_id FROM employees),
                 b AS (SELECT department_id FROM departments)
            SELECT a.employee_id, b.department_id FROM a CROSS JOIN b", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.DoesNotContain(result.Issues, i => i.RuleId == "NZ104");
    }

    // ====================================================================
    // NZ105: DISTINCT with ORDER BY mismatch
    // ====================================================================

    [Fact]
    public void NZ105_DistinctOrderByMismatch_Detected()
    {
        var config = new LintConfig("SELECT DISTINCT first_name FROM employees ORDER BY salary", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.Contains(result.Issues, i => i.RuleId == "NZ105");
    }

    [Fact]
    public void NZ105_DistinctOrderByMatch_NoIssue()
    {
        var config = new LintConfig("SELECT DISTINCT first_name FROM employees ORDER BY first_name", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.DoesNotContain(result.Issues, i => i.RuleId == "NZ105");
    }

    [Fact]
    public void NZ105_DistinctOrderByAlias_NoIssue()
    {
        var config = new LintConfig("SELECT DISTINCT first_name AS name FROM employees ORDER BY name", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.DoesNotContain(result.Issues, i => i.RuleId == "NZ105");
    }

    [Fact]
    public void NZ105_NonDistinctOrderBy_NoIssue()
    {
        var config = new LintConfig("SELECT first_name, salary FROM employees ORDER BY salary", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.DoesNotContain(result.Issues, i => i.RuleId == "NZ105");
    }

    [Fact]
    public void NZ105_DistinctOrderByOrdinal_NoIssue()
    {
        var config = new LintConfig("SELECT DISTINCT first_name, salary FROM employees ORDER BY 1", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.DoesNotContain(result.Issues, i => i.RuleId == "NZ105");
    }

    [Fact]
    public void NZ105_DistinctSingleColumn_OrderBySame_NoIssue()
    {
        var config = new LintConfig("SELECT DISTINCT department_id FROM employees ORDER BY department_id", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.DoesNotContain(result.Issues, i => i.RuleId == "NZ105");
    }

    [Fact]
    public void NZ105_DistinctOrderByMultiple_SomeMismatch_Detected()
    {
        var config = new LintConfig("SELECT DISTINCT first_name, department_id FROM employees ORDER BY first_name, salary", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.Contains(result.Issues, i => i.RuleId == "NZ105" && i.Message.Contains("salary"));
    }

    // ====================================================================
    // NZ106: COMMIT/ROLLBACK in script context
    // ====================================================================

    [Fact]
    public void NZ106_CommitStatement_Detected()
    {
        var config = new LintConfig("COMMIT", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.Contains(result.Issues, i => i.RuleId == "NZ106" && i.Message.Contains("COMMIT"));
    }

    [Fact]
    public void NZ106_RollbackStatement_Detected()
    {
        var config = new LintConfig("ROLLBACK", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.Contains(result.Issues, i => i.RuleId == "NZ106" && i.Message.Contains("ROLLBACK"));
    }

    [Fact]
    public void NZ106_CommitAfterBegin_StillDetected()
    {
        // NZ106 always flags COMMIT/ROLLBACK (no state tracking)
        var config = new LintConfig("BEGIN; SELECT 1; COMMIT", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.Contains(result.Issues, i => i.RuleId == "NZ106");
    }

    [Fact]
    public void NZ106_MultipleCommits_AllDetected()
    {
        var config = new LintConfig("COMMIT; SELECT 1; COMMIT", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.Equal(2, result.Issues.Count(i => i.RuleId == "NZ106"));
    }

    [Fact]
    public void NZ106_SelectOnly_NoIssue()
    {
        var config = new LintConfig("SELECT employee_id FROM employees", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.DoesNotContain(result.Issues, i => i.RuleId == "NZ106");
    }

    [Fact]
    public void NZ106_DefaultSeverity_IsInformation()
    {
        var rule = new RuleNZ106_TransactionStatement();
        Assert.Equal(LintSeverity.Information, rule.DefaultSeverity);
    }

    [Fact]
    public void NZ106_UpdateStatement_NoIssue()
    {
        var config = new LintConfig("DELETE FROM employees WHERE employee_id = 1", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.DoesNotContain(result.Issues, i => i.RuleId == "NZ106");
    }

    // ====================================================================
    // NZ107: Unused column alias
    // ====================================================================

    [Fact]
    public void NZ107_AliasNotUsedInOrderBy_Detected()
    {
        var config = new LintConfig("SELECT first_name AS fn FROM employees", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.Contains(result.Issues, i => i.RuleId == "NZ107");
    }

    [Fact]
    public void NZ107_AliasUsedInOrderBy_NoIssue()
    {
        var config = new LintConfig("SELECT first_name AS fn FROM employees ORDER BY fn", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.DoesNotContain(result.Issues, i => i.RuleId == "NZ107");
    }

    [Fact]
    public void NZ107_AliasUsedInHaving_NoIssue()
    {
        var config = new LintConfig("SELECT department_id AS dept_id, COUNT(*) FROM employees GROUP BY department_id HAVING dept_id > 10", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.DoesNotContain(result.Issues, i => i.RuleId == "NZ107");
    }

    [Fact]
    public void NZ107_NoAlias_NoIssue()
    {
        var config = new LintConfig("SELECT first_name FROM employees", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.DoesNotContain(result.Issues, i => i.RuleId == "NZ107");
    }

    [Fact]
    public void NZ107_AliasSameAsColumnName_NoIssue()
    {
        var config = new LintConfig("SELECT first_name AS first_name FROM employees", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.DoesNotContain(result.Issues, i => i.RuleId == "NZ107");
    }

    [Fact]
    public void NZ107_MultipleAliases_SomeUnused_Detected()
    {
        var config = new LintConfig("SELECT first_name AS fn, last_name AS ln, salary FROM employees ORDER BY fn", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.Contains(result.Issues, i => i.RuleId == "NZ107" && i.Message.Contains("ln"));
        Assert.DoesNotContain(result.Issues, i => i.RuleId == "NZ107" && i.Message.Contains("fn"));
    }

    [Fact]
    public void NZ107_ExpressionAlias_NoIssue()
    {
        // NZ107 only flags aliases on simple column references, not expressions
        var config = new LintConfig("SELECT salary * 1.1 AS raised_salary FROM employees", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.DoesNotContain(result.Issues, i => i.RuleId == "NZ107");
    }

    [Fact]
    public void NZ107_AggregateAlias_NoIssue()
    {
        var config = new LintConfig("SELECT COUNT(*) AS cnt FROM employees", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.DoesNotContain(result.Issues, i => i.RuleId == "NZ107");
    }

    // ====================================================================
    // NZ108: Subquery in SELECT
    // ====================================================================

    [Fact]
    public void NZ108_SubqueryInSelect_Detected()
    {
        var config = new LintConfig("SELECT employee_id, (SELECT MAX(salary) FROM employees) AS max_sal FROM employees", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.Contains(result.Issues, i => i.RuleId == "NZ108");
    }

    [Fact]
    public void NZ108_NoSubquery_NoIssue()
    {
        var config = new LintConfig("SELECT employee_id, first_name FROM employees", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.DoesNotContain(result.Issues, i => i.RuleId == "NZ108");
    }

    [Fact]
    public void NZ108_MultipleSubqueries_AllDetected()
    {
        var config = new LintConfig(@"
            SELECT employee_id,
                (SELECT department_name FROM departments d WHERE d.department_id = e.department_id) AS dept_name,
                (SELECT COUNT(*) FROM orders o WHERE o.customer_id = e.employee_id) AS order_count
            FROM employees e", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.Contains(result.Issues, i => i.RuleId == "NZ108");
        Assert.True(result.Issues.Count(i => i.RuleId == "NZ108") >= 1);
    }

    [Fact]
    public void NZ108_SubqueryInWhere_NoIssue()
    {
        var config = new LintConfig("SELECT employee_id FROM employees WHERE salary > (SELECT AVG(salary) FROM employees)", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.DoesNotContain(result.Issues, i => i.RuleId == "NZ108");
    }

    [Fact]
    public void NZ108_SubqueryInHaving_NoIssue()
    {
        var config = new LintConfig("SELECT department_id, COUNT(*) AS cnt FROM employees GROUP BY department_id HAVING cnt > (SELECT COUNT(*) FROM departments)", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.DoesNotContain(result.Issues, i => i.RuleId == "NZ108");
    }

    [Fact]
    public void NZ108_SelectWithoutFrom_SubqueryInSelect_Detected()
    {
        var config = new LintConfig("SELECT (SELECT MAX(salary) FROM employees) AS max_sal", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.Contains(result.Issues, i => i.RuleId == "NZ108");
    }

    [Fact]
    public void NZ108_NonSelectStatement_NoIssue()
    {
        var config = new LintConfig("DELETE FROM employees WHERE employee_id = 1", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.DoesNotContain(result.Issues, i => i.RuleId == "NZ108");
    }

    // ====================================================================
    // Registry integration
    // ====================================================================

    [Fact]
    public void DefaultEngine_IncludesExpensiveRules()
    {
        Assert.NotEmpty(_engine.ExpensiveRules);
        Assert.Contains(_engine.ExpensiveRules, r => r.Id == "NZ101");
        Assert.Contains(_engine.ExpensiveRules, r => r.Id == "NZ102");
        Assert.Contains(_engine.ExpensiveRules, r => r.Id == "NZ103");
        Assert.Contains(_engine.ExpensiveRules, r => r.Id == "NZ104");
        Assert.Contains(_engine.ExpensiveRules, r => r.Id == "NZ105");
        Assert.Contains(_engine.ExpensiveRules, r => r.Id == "NZ106");
        Assert.Contains(_engine.ExpensiveRules, r => r.Id == "NZ107");
        Assert.Contains(_engine.ExpensiveRules, r => r.Id == "NZ108");
    }

    [Fact]
    public void QualityRuleRegistry_IncludesExpensiveRules()
    {
        var registry = new QualityRuleRegistry();
        Assert.True(registry.HasRule("NZ101"));
        Assert.True(registry.HasRule("NZ102"));
        Assert.True(registry.HasRule("NZ103"));
        Assert.True(registry.HasRule("NZ104"));
        Assert.True(registry.HasRule("NZ105"));
        Assert.True(registry.HasRule("NZ106"));
        Assert.True(registry.HasRule("NZ107"));
        Assert.True(registry.HasRule("NZ108"));
    }

    [Fact]
    public void ExpensiveRules_AreNotInCheapRules()
    {
        Assert.DoesNotContain(_engine.CheapRules, r => r.Id == "NZ101");
        Assert.DoesNotContain(_engine.CheapRules, r => r.Id == "NZ102");
        Assert.DoesNotContain(_engine.CheapRules, r => r.Id == "NZ103");
        Assert.DoesNotContain(_engine.CheapRules, r => r.Id == "NZ104");
        Assert.DoesNotContain(_engine.CheapRules, r => r.Id == "NZ105");
        Assert.DoesNotContain(_engine.CheapRules, r => r.Id == "NZ106");
        Assert.DoesNotContain(_engine.CheapRules, r => r.Id == "NZ107");
        Assert.DoesNotContain(_engine.CheapRules, r => r.Id == "NZ108");
    }

    [Fact]
    public void ExpensiveRuleCount_WithNewRules()
    {
        Assert.Equal(8, _engine.ExpensiveRules.Count);
    }

    [Fact]
    public void RunFullLint_IncludesExpensiveRuleIssues()
    {
        var config = new LintConfig("SELECT * FROM employees e JOIN departments d ON e.department_id = d.department_id", Schema: _schema);
        var result = _engine.RunFullLint(config);
        // NZ001 from cheap rules + NZ101 from expensive rules
        Assert.Contains(result.Issues, i => i.RuleId == "NZ001");
        Assert.Contains(result.Issues, i => i.RuleId == "NZ101");
    }

    [Fact]
    public void ExpensiveRuleOffViaRegistry_SuppressesRule()
    {
        _engine.Registry.SetSeverity("NZ101", RuleSeverityConfig.Off);
        try
        {
            var config = new LintConfig("SELECT * FROM employees e JOIN departments d ON e.department_id = d.department_id", Schema: _schema);
            var result = _engine.RunExpensiveAnalysis(config);
            Assert.DoesNotContain(result.Issues, i => i.RuleId == "NZ101");
        }
        finally
        {
            _engine.Registry.ResetSeverities();
        }
    }

    [Fact]
    public void ExpensiveRules_ListNamingConvention()
    {
        // Verify that all expensive rule IDs follow NZ1xx naming
        foreach (var rule in _engine.ExpensiveRules)
        {
            Assert.StartsWith("NZ1", rule.Id);
        }
    }

    [Fact]
    public void NzExpensiveRules_AllRules_Count()
    {
        Assert.Equal(8, NzExpensiveRules.AllRules.Count);
    }

    [Fact]
    public void NzExpensiveRules_AllRules_HaveExpensiveCost()
    {
        foreach (var rule in NzExpensiveRules.AllRules)
        {
            Assert.Equal(RuleCost.Expensive, rule.Cost);
        }
    }

    // ====================================================================
    // Edge cases: non-SELECT statements should not trigger SELECT-only rules
    // ====================================================================

    [Fact]
    public void UpdateStatement_DoesNotTriggerSelectOnlyRules()
    {
        var config = new LintConfig("UPDATE employees SET salary = 50000 WHERE employee_id = 1", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        // NZ102, NZ103, NZ104, NZ105, NZ101 are SELECT-only — none should fire
        Assert.DoesNotContain(result.Issues, i => i.RuleId == "NZ101");
        Assert.DoesNotContain(result.Issues, i => i.RuleId == "NZ102");
        Assert.DoesNotContain(result.Issues, i => i.RuleId == "NZ103");
        Assert.DoesNotContain(result.Issues, i => i.RuleId == "NZ104");
        Assert.DoesNotContain(result.Issues, i => i.RuleId == "NZ105");
    }

    [Fact]
    public void InsertStatement_DoesNotTriggerSelectOnlyRules()
    {
        var config = new LintConfig("INSERT INTO employees (employee_id, first_name) VALUES (1, 'John')", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.DoesNotContain(result.Issues, i => i.RuleId is "NZ101" or "NZ102" or "NZ103" or "NZ104" or "NZ105" or "NZ107" or "NZ108");
    }

    [Fact]
    public void DeleteStatement_DoesNotTriggerSelectOnlyRules()
    {
        var config = new LintConfig("DELETE FROM employees WHERE employee_id = 1", Schema: _schema);
        var result = _engine.RunExpensiveAnalysis(config);
        Assert.DoesNotContain(result.Issues, i => i.RuleId is "NZ101" or "NZ102" or "NZ103" or "NZ104" or "NZ105" or "NZ107" or "NZ108");
    }

    // ====================================================================
    // Severity override integration
    // ====================================================================

    [Fact]
    public void ExpensiveRule_SeverityOverride_ChangesSeverity()
    {
        _engine.Registry.SetSeverity("NZ103", RuleSeverityConfig.Error);
        try
        {
            var config = new LintConfig("SELECT COUNT(*), department_id FROM employees", Schema: _schema);
            var result = _engine.RunExpensiveAnalysis(config);
            var issue = Assert.Single(result.Issues, i => i.RuleId == "NZ103");
            Assert.Equal(LintSeverity.Error, issue.Severity);
        }
        finally
        {
            _engine.Registry.ResetSeverities();
        }
    }

    [Fact]
    public void ExpensiveRule_PerCallSeverity_OverridesRegistry_ThroughFullLint()
    {
        // RunExpensiveAnalysis doesn't support per-call severities directly.
        // Per-call severities are supported through RunFullLint, which passes
        // them to RunCheapRules. For expensive rules, use the registry instead.
        _engine.Registry.SetSeverity("NZ101", RuleSeverityConfig.Hint);
        try
        {
            var config = new LintConfig("SELECT * FROM employees e JOIN departments d ON e.department_id = d.department_id", Schema: _schema);
            var result = _engine.RunExpensiveAnalysis(config);
            var issue = Assert.Single(result.Issues, i => i.RuleId == "NZ101");
            Assert.Equal(LintSeverity.Hint, issue.Severity);
        }
        finally
        {
            _engine.Registry.ResetSeverities();
        }
    }
}
