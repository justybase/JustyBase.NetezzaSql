using JustyBase.NetezzaSqlParser.Linter;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed partial class NzExpensiveRulesTests
{
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
    public void ExpensiveRuleCount_WithNewRules()
    {
        Assert.Equal(8, _engine.ExpensiveRules.Count);
    }


    [Fact]
    public void NzExpensiveRules_AllRules_Count()
    {
        Assert.Equal(8, NzExpensiveRules.AllRules.Count);
    }
}
