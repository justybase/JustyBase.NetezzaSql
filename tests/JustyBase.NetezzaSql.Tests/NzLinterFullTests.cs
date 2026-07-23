using JustyBase.NetezzaSqlParser.Linter;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed class NzLinterFullTests
{
    [Fact] public void NZ001_select_star_lowercase() => AssertIssues("select * from users", "NZ001", 1);
    [Fact] public void NZ001_multiple_select_star() => AssertIssues("SELECT * FROM t1; SELECT * FROM t2", "NZ001", 2);
    [Fact] public void NZ001_explicit_columns_no_issue() => AssertIssues("SELECT id, name FROM users", "NZ001", 0);
    [Fact] public void NZ001_in_comment_no_issue() => AssertIssues("-- SELECT * FROM users\nSELECT id FROM users", "NZ001", 0);
    [Fact] public void NZ001_in_string_no_issue() => AssertIssues("SELECT 'SELECT * FROM foo' as query FROM users", "NZ001", 0);

    [Fact] public void NZ002_delete_without_where() => AssertIssues("DELETE FROM users", "NZ002", 1);
    [Fact] public void NZ002_delete_with_where() => AssertIssues("DELETE FROM users WHERE id = 1", "NZ002", 0);
    [Fact] public void NZ002_delete_semicolon_no_where() => AssertIssues("DELETE FROM users;", "NZ002", 1);
    [Fact] public void NZ002_lowercase() => AssertIssues("delete from users", "NZ002", 1);

    [Fact] public void NZ003_update_without_where() => AssertIssues("UPDATE users SET active = 0", "NZ003", 1);
    [Fact] public void NZ003_update_with_where() => AssertIssues("UPDATE users SET active = 0 WHERE id = 1", "NZ003", 0);
    [Fact] public void NZ003_semicolon_no_where() => AssertIssues("UPDATE users SET name = 'test';", "NZ003", 1);
    [Fact] public void NZ003_lowercase() => AssertIssues("update users set active = 1", "NZ003", 1);

    [Fact] public void NZ004_cross_join() => AssertIssues("SELECT * FROM t1 CROSS JOIN t2", "NZ004", 1);
    [Fact] public void NZ004_lowercase_cross_join() => AssertIssues("SELECT * FROM t1 cross join t2", "NZ004", 1);
    [Fact] public void NZ004_inner_join_no_issue() => AssertIssues("SELECT * FROM t1 INNER JOIN t2 ON t1.id = t2.id", "NZ004", 0);

    [Fact] public void NZ005_leading_percent() => AssertIssues("SELECT * FROM users WHERE name LIKE '%test'", "NZ005", 1);
    [Fact] public void NZ005_leading_and_trailing() => AssertIssues("SELECT * FROM users WHERE name LIKE '%test%'", "NZ005", 1);
    [Fact] public void NZ005_trailing_only_no_issue() => AssertIssues("SELECT * FROM users WHERE name LIKE 'test%'", "NZ005", 0);

    [Fact] public void NZ006_order_by_no_limit() => AssertIssues("SELECT * FROM users ORDER BY name", "NZ006", 1);
    [Fact] public void NZ006_order_by_with_limit() => AssertIssues("SELECT * FROM users ORDER BY name LIMIT 10", "NZ006", 0);
    [Fact] public void NZ006_order_by_with_fetch() => AssertIssues("SELECT * FROM users ORDER BY name FETCH FIRST 10 ROWS ONLY", "NZ006", 0);
    [Fact] public void NZ006_lowercase() => AssertIssues("select * from users order by name", "NZ006", 1);

    [Fact] public void NZ007_mixed_case_detected() => AssertIssuesGt("Select * From users Where id = 1", "NZ007", 0);
    [Fact] public void NZ007_all_upper_no_issue() => AssertIssues("SELECT * FROM USERS WHERE ID = 1", "NZ007", 0);
    [Fact] public void NZ007_all_lower_no_issue() => AssertIssues("select * from users where id = 1", "NZ007", 0);
    [Fact] public void NZ007_inconsistent_upper_lower() => AssertIssuesGt("SELECT * from users WHERE id = 1", "NZ007", 0);
    [Fact] public void NZ007_in_comment_no_issue() => AssertIssues("-- Select from users\nSELECT * FROM USERS", "NZ007", 0);
    [Fact] public void NZ007_in_string_no_issue() => AssertIssues("SELECT 'select from where' FROM USERS", "NZ007", 0);
    [Fact] public void NZ007_upper_dominant() { var i = Lint("SELECT col1 from table1 WHERE id = 1", "NZ007"); Assert.Contains(i, x => x.Message.Contains("UPPERCASE")); }

    [Fact] public void NZ008_truncate_table() => AssertIssues("TRUNCATE TABLE users", "NZ008", 1);
    [Fact] public void NZ008_truncate_lowercase() => AssertIssues("truncate table users", "NZ008", 1);
    [Fact] public void NZ008_truncate_no_table_kw() => AssertIssues("TRUNCATE users", "NZ008", 1);

    [Fact] public void NZ009_multiple_or() => AssertIssues("SELECT * FROM users WHERE status = 1 OR status = 2 OR status = 3", "NZ009", 1);
    [Fact] public void NZ009_single_or_no_issue() => AssertIssues("SELECT * FROM users WHERE status = 1 OR status = 2", "NZ009", 0);
    [Fact] public void NZ009_no_where_no_issue() => AssertIssues("SELECT 1 OR 2", "NZ009", 0);

    [Fact] public void NZ010_join_no_alias() => AssertIssues("SELECT * FROM t1 JOIN t2 ON t1.id = t2.id", "NZ010", 1);
    [Fact] public void NZ010_join_with_alias() => AssertIssues("SELECT * FROM t1 JOIN t2 t ON t1.id = t.id", "NZ010", 0);
    [Fact] public void NZ010_join_with_as_alias() => AssertIssues("SELECT * FROM t1 JOIN t2 AS t ON t1.id = t.id", "NZ010", 0);
    [Fact] public void NZ010_left_join_no_alias() => AssertIssues("SELECT * FROM t1 LEFT JOIN t2 ON t1.id = t2.id", "NZ010", 1);
    [Fact] public void NZ010_inner_join_no_alias() => AssertIssues("SELECT * FROM t1 INNER JOIN t2 ON t1.id = t2.id", "NZ010", 1);
    [Fact] public void NZ010_multiple_joins_no_alias() => AssertIssues("SELECT * FROM t1 JOIN t2 ON t1.id = t2.id JOIN t3 ON t2.id = t3.id", "NZ010", 2);
    [Fact] public void NZ010_lowercase() => AssertIssues("select * from t1 join t2 on t1.id = t2.id", "NZ010", 1);

    [Fact] public void NZ011_ctas_no_distribute() => AssertIssues("CREATE TABLE new_table AS SELECT * FROM old_table", "NZ011", 1);
    [Fact] public void NZ011_ctas_distribute_random() => AssertIssues("CREATE TABLE new_table AS SELECT * FROM old_table DISTRIBUTE ON RANDOM", "NZ011", 0);
    [Fact] public void NZ011_ctas_distribute_column() => AssertIssues("CREATE TABLE new_table AS SELECT * FROM old_table DISTRIBUTE ON (id)", "NZ011", 0);
    [Fact] public void NZ011_ctas_if_not_exists() => AssertIssues("CREATE TABLE IF NOT EXISTS new_table AS SELECT * FROM old_table", "NZ011", 1);
    [Fact] public void NZ011_regular_create_no_issue() => AssertIssues("CREATE TABLE new_table (id INT, name VARCHAR(100))", "NZ011", 0);
    [Fact] public void NZ011_ctas_parentheses() => AssertIssues("CREATE TABLE new_table AS (SELECT * FROM old_table)", "NZ011", 1);
    [Fact] public void NZ011_lowercase() => AssertIssues("create table new_table as select * from old_table", "NZ011", 1);
    [Fact] public void NZ011_distribute_later() => AssertIssues("CREATE TABLE new_table AS SELECT * FROM old_table WHERE id > 0 DISTRIBUTE ON (id)", "NZ011", 0);
    [Fact] public void NZ011_multiline() => AssertIssues("CREATE TABLE t AS \nSELECT * FROM old", "NZ011", 1);

    [Fact] public void NZ012_update_as_alias() => AssertIssues("UPDATE users AS u SET active = 1", "NZ012", 1);
    [Fact] public void NZ012_update_no_as() => AssertIssues("UPDATE users u SET active = 1", "NZ012", 0);
    [Fact] public void NZ012_plain_update() => AssertIssues("UPDATE users SET active = 1", "NZ012", 0);
    [Fact] public void NZ012_lowercase() => AssertIssues("update table1 as t1 set col1 = 1", "NZ012", 1);

    [Fact] public void NZ013_union() => AssertIssues("SELECT * FROM t1 UNION SELECT * FROM t2", "NZ013", 1);
    [Fact] public void NZ013_union_distinct() => AssertIssues("SELECT * FROM t1 UNION DISTINCT SELECT * FROM t2", "NZ013", 1);
    [Fact] public void NZ013_union_all_no_issue() => AssertIssues("SELECT * FROM t1 UNION ALL SELECT * FROM t2", "NZ013", 0);
    [Fact] public void NZ013_multiple_unions() => AssertIssues("SELECT 1 UNION SELECT 2 UNION ALL SELECT 3", "NZ013", 1);
    [Fact] public void NZ013_lowercase() => AssertIssues("select * from t1 union select * from t2", "NZ013", 1);
    [Fact] public void NZ013_multiple_union_no_all() => AssertIssues("SELECT * FROM t1 UNION SELECT * FROM t2 UNION SELECT * FROM t3", "NZ013", 2);

    [Fact] public void NZ014_or_in_join() => AssertIssues("SELECT * FROM A JOIN B ON A.id = B.id OR 1 = 1", "NZ014", 1);
    [Fact] public void NZ014_or_in_join_with_alias() => AssertIssues("SELECT * FROM TABLE_1 A JOIN TABLE_2 B ON A.id = B.id OR 1=1", "NZ014", 1);
    [Fact] public void NZ014_and_only_no_issue() => AssertIssues("SELECT * FROM A JOIN B ON A.id = B.id AND A.status = 'active'", "NZ014", 0);
    [Fact] public void NZ014_no_or_no_issue() => AssertIssues("SELECT * FROM A JOIN B ON A.id = B.id", "NZ014", 0);
    [Fact] public void NZ014_lowercase() => AssertIssues("select * from a join b on a.id = b.id or 1=1", "NZ014", 1);
    [Fact] public void NZ014_or_in_where_no_issue() => AssertIssues("SELECT * FROM t1 A JOIN t2 ON 1=1 WHERE 1=1 AND 1=1 OR 1=2", "NZ014", 0);

    [Fact] public void NZ015_function_in_where() => AssertIssues("SELECT * FROM table1 WHERE UPPER(name) = 'TEST'", "NZ015", 1);
    [Fact] public void NZ015_no_function_no_issue() => AssertIssues("SELECT * FROM table1 WHERE name = 'TEST'", "NZ015", 0);
    [Fact] public void NZ015_lowercase() => AssertIssues("select * from table1 where upper(name) = 'test'", "NZ015", 1);

    [Fact] public void NZ016_implicit_cast_join() => AssertIssues("SELECT * FROM table1 t1 JOIN table2 t2 ON t1.id = '123'", "NZ016", 0);

    [Fact] public void NZ017_double_quoted_id() => AssertIssues("SELECT \"column_name\" FROM table1", "NZ017", 1);
    [Fact] public void NZ017_multiple_quoted() => AssertIssues("SELECT \"col1\", \"col2\" FROM \"table_name\"", "NZ017", 3);
    [Fact] public void NZ017_single_quoted_no_issue() => AssertIssues("SELECT 'string value' FROM table1", "NZ017", 0);
    [Fact] public void NZ017_in_comment_no_issue() => AssertIssues("SELECT col1 FROM table1 -- \"comment with quotes\"", "NZ017", 0);

    [Fact] public void NZ018_self_ref_join() => AssertIssues("SELECT * FROM T1 JOIN T2 ON T1.ID = T1.ID", "NZ018", 1);
    [Fact] public void NZ018_different_cols_no_issue() => AssertIssues("SELECT * FROM T1 JOIN T2 ON T1.ID = T2.ID", "NZ018", 0);
    [Fact] public void NZ018_lowercase() => AssertIssues("select * from t1 join t2 on t1.id = t1.id", "NZ018", 1);
    [Fact] public void NZ018_multiple_and_or() => AssertIssues("SELECT 1 FROM t1 A WHERE A.col = A.col AND col = col AND 1 = 1", "NZ018", 3);

    [Fact] public void NZ019_case_without_end() => AssertIssues("SELECT CASE WHEN X=Y THEN 1 FROM table1", "NZ019", 1);
    [Fact] public void NZ019_case_else_no_end() => AssertIssues("SELECT CASE WHEN X=Y THEN 1 ELSE 2 FROM table1", "NZ019", 1);
    [Fact] public void NZ019_case_with_end() => AssertIssues("SELECT CASE WHEN X=Y THEN 1 END FROM table1", "NZ019", 0);
    [Fact] public void NZ019_case_else_end() => AssertIssues("SELECT CASE WHEN X=Y THEN 1 ELSE 2 END FROM table1", "NZ019", 0);
    [Fact] public void NZ019_lowercase() => AssertIssues("select case when x=y then 1 from table1", "NZ019", 1);
    [Fact] public void NZ019_multiple_case_with_end() => AssertIssues("SELECT CASE WHEN X=Y THEN 1 END, CASE WHEN A=B THEN 2 END FROM table1", "NZ019", 0);

    [Fact] public void NZ020_in_select_subquery() => AssertIssues("SELECT * FROM table1 WHERE id IN (SELECT id FROM table2)", "NZ020", 1);
    [Fact] public void NZ020_multiple_in_select() => AssertIssues("SELECT * FROM t1 WHERE id IN (SELECT id FROM t2) AND status IN (SELECT status FROM t3)", "NZ020", 2);
    [Fact] public void NZ020_in_literals_no_issue() => AssertIssues("SELECT * FROM table1 WHERE id IN (1, 2, 3)", "NZ020", 0);
    [Fact] public void NZ020_lowercase() => AssertIssues("select * from t1 where id in (select id from t2)", "NZ020", 1);

    // Edge cases
    [Fact] public void Edge_subquery_ignores_inner_select_star()
    {
        var sql = "SELECT * FROM (SELECT id, name FROM users WHERE active = 1) AS subquery";
        AssertIssues(sql, "NZ001", 1); // only outer SELECT * flagged
    }

    [Fact] public void Edge_block_comment_ignores()
    {
        var sql = "/* \n * SELECT * FROM dangerous_table\n * DELETE FROM important_table\n */\nSELECT col1 FROM table1 WHERE id = 1";
        AssertIssues(sql, "NZ001", 0);
        AssertIssues(sql, "NZ002", 0);
    }

    [Fact] public void Edge_nested_quotes()
    {
        var sql = "SELECT 'it''s a test' FROM table1";
        AssertIssues(sql, "NZ001", 0);
    }

    // ====== Procedure rules ======

    [Fact] public void NZP001_missing_begin_proc()
    {
        var sql = "CREATE PROCEDURE test_proc() LANGUAGE NZPLSQL IS END_PROC; SELECT 1;";
        AssertIssues(NzLintRulesExtensions.ProcedureRules, sql, "NZP001", 1);
    }

    [Fact] public void NZP001_missing_end_proc()
    {
        var sql = "CREATE PROCEDURE test_proc() LANGUAGE NZPLSQL IS BEGIN_PROC; SELECT 1";
        AssertIssues(NzLintRulesExtensions.ProcedureRules, sql, "NZP001", 1);
    }

    [Fact] public void NZP001_valid()
    {
        var sql = "CREATE PROCEDURE test_proc() LANGUAGE NZPLSQL IS BEGIN_PROC SELECT 1; END_PROC;";
        AssertIssues(NzLintRulesExtensions.ProcedureRules, sql, "NZP001", 0);
    }

    [Fact] public void NZP002_missing_language()
    {
        var sql = "CREATE PROCEDURE test_proc() IS BEGIN_PROC SELECT 1; END_PROC;";
        AssertIssues(NzLintRulesExtensions.ProcedureRules, sql, "NZP002", 1);
    }

    [Fact] public void NZP002_with_language()
    {
        var sql = "CREATE PROCEDURE test_proc() LANGUAGE NZPLSQL IS BEGIN_PROC SELECT 1; END_PROC;";
        AssertIssues(NzLintRulesExtensions.ProcedureRules, sql, "NZP002", 0);
    }

    [Fact] public void NZP003_missing_returns()
    {
        var sql = "CREATE PROCEDURE test_proc() LANGUAGE NZPLSQL IS BEGIN_PROC SELECT 1; END_PROC;";
        AssertIssues(NzLintRulesExtensions.ProcedureRules, sql, "NZP003", 1);
    }

    [Fact] public void NZP003_with_returns()
    {
        var sql = "CREATE PROCEDURE test_proc() RETURNS VARCHAR LANGUAGE NZPLSQL IS BEGIN_PROC SELECT 1; END_PROC;";
        AssertIssues(NzLintRulesExtensions.ProcedureRules, sql, "NZP003", 0);
    }

    [Fact] public void NZP004_unmatched_begin_missing_end()
    {
        var sql = "CREATE PROCEDURE test_proc() LANGUAGE NZPLSQL IS BEGIN_PROC BEGIN SELECT 1; END_PROC;";
        AssertIssues(NzLintRulesExtensions.ProcedureRules, sql, "NZP004", 1);
    }

    [Fact] public void NZP004_matched_begin_end()
    {
        var sql = "CREATE PROCEDURE test_proc() LANGUAGE NZPLSQL IS BEGIN_PROC BEGIN SELECT 1; END END_PROC;";
        AssertIssues(NzLintRulesExtensions.ProcedureRules, sql, "NZP004", 0);
    }

    [Fact] public void NZP005_if_without_end_if()
    {
        var sql = "CREATE PROCEDURE test_proc() LANGUAGE NZPLSQL IS BEGIN_PROC IF x = 1 THEN SELECT 1; END_PROC;";
        AssertIssues(NzLintRulesExtensions.ProcedureRules, sql, "NZP005", 1);
    }

    [Fact] public void NZP005_if_with_end_if()
    {
        var sql = "CREATE PROCEDURE test_proc() LANGUAGE NZPLSQL IS BEGIN_PROC IF x = 1 THEN SELECT 1; END IF; END_PROC;";
        AssertIssues(NzLintRulesExtensions.ProcedureRules, sql, "NZP005", 0);
    }

    [Fact] public void NZP006_loop_without_end_loop()
    {
        var sql = "CREATE PROCEDURE test_proc() LANGUAGE NZPLSQL IS BEGIN_PROC LOOP SELECT 1; END_PROC;";
        AssertIssues(NzLintRulesExtensions.ProcedureRules, sql, "NZP006", 1);
    }

    [Fact] public void NZP006_matched_loop_end_loop()
    {
        var sql = "CREATE PROCEDURE test_proc() LANGUAGE NZPLSQL IS BEGIN_PROC LOOP SELECT 1; END LOOP; END_PROC;";
        AssertIssues(NzLintRulesExtensions.ProcedureRules, sql, "NZP006", 0);
    }

    [Fact] public void NZP012_elseif_detected()
    {
        var sql = "CREATE PROCEDURE test_proc() LANGUAGE NZPLSQL IS BEGIN_PROC IF x=1 THEN NULL; ELSEIF x=2 THEN NULL; END IF; END_PROC;";
        AssertIssues(NzLintRulesExtensions.ProcedureRules, sql, "NZP012", 1);
    }

    [Fact] public void NZP012_else_if_detected()
    {
        var sql = "CREATE PROCEDURE test_proc() LANGUAGE NZPLSQL IS BEGIN_PROC IF x=1 THEN NULL; ELSE IF x=2 THEN NULL; END IF; END_PROC;";
        AssertIssues(NzLintRulesExtensions.ProcedureRules, sql, "NZP012", 1);
    }

    [Fact] public void NZP012_elsif_no_issue()
    {
        var sql = "CREATE PROCEDURE test_proc() LANGUAGE NZPLSQL IS BEGIN_PROC IF x=1 THEN NULL; ELSIF x=2 THEN NULL; END IF; END_PROC;";
        AssertIssues(NzLintRulesExtensions.ProcedureRules, sql, "NZP012", 0);
    }

    // ====== Helpers ======

    private static void AssertIssues(string sql, string ruleId, int expectedCount)
    {
        AssertIssues(NzLintRules.AllRules, sql, ruleId, expectedCount);
    }

    private static void AssertIssuesGt(string sql, string ruleId, int expectedMin)
    {
        var issues = NzLintRules.AllRules.SelectMany(r => r.Check(sql)).Where(i => i.RuleId == ruleId).ToList();
        Assert.True(issues.Count > expectedMin, $"Expected > {expectedMin} {ruleId} issues, got {issues.Count}");
    }

    private static void AssertIssues(IReadOnlyList<LintRule> rules, string sql, string ruleId, int expectedCount)
    {
        var issues = rules.SelectMany(r => r.Check(sql)).Where(i => i.RuleId == ruleId).ToList();
        Assert.Equal(expectedCount, issues.Count);
    }

    private static List<LintIssue> Lint(string sql, string ruleId)
    {
        return NzLintRules.AllRules.SelectMany(r => r.Check(sql)).Where(i => i.RuleId == ruleId).ToList();
    }
}
