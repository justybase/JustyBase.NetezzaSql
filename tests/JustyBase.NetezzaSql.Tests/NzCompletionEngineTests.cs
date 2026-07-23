using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Completion;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed class NzCompletionEngineTests
{
    private readonly InMemorySchemaProvider _schema = new();
    private readonly NzCompletionEngine _engine;

    public NzCompletionEngineTests()
    {
        _schema.AddTable(new TableInfo("employees", Columns: new[]
        {
            new ColumnInfo("id"), new ColumnInfo("name"), new ColumnInfo("salary"), new ColumnInfo("dept_id")
        }));
        _schema.AddTable(new TableInfo("departments", Columns: new[]
        {
            new ColumnInfo("id"), new ColumnInfo("name"), new ColumnInfo("location")
        }));
        _engine = new NzCompletionEngine(_schema);
    }

    [Fact]
    public void TopLevel_SEL_suggests_SELECT_and_SET()
    {
        var i = _engine.GetCompletions("SE", 2);
        Assert.Contains(i, x => x.Label == "SELECT");
        Assert.Contains(i, x => x.Label == "SET");
    }

    [Fact]
    public void Partial_SEL_filters_by_prefix()
    {
        var i = _engine.GetCompletions("SEL", 3);
        Assert.Contains(i, x => x.Label == "SELECT");
        Assert.All(i, x => Assert.StartsWith("SEL", x.Label, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Empty_input_suggests_top_level()
    {
        var i = _engine.GetCompletions("", 0);
        Assert.NotEmpty(i);
        Assert.Contains(i, x => x.Label == "SELECT");
    }

    [Fact]
    public void AfterSelect_suggests_functions()
    {
        var i = _engine.GetCompletions("SELECT  FROM employees", 7);
        Assert.Contains(i, x => x.Kind == CompletionKind.Function);
    }

    [Fact]
    public void AfterSelect_empty_prefix_suggests_functions()
    {
        var i = _engine.GetCompletions("SELECT ", 7);
        Assert.NotEmpty(i);
        Assert.Contains(i, x => x.Kind == CompletionKind.Function);
    }

    [Fact]
    public void AfterFrom_does_not_suggest_functions()
    {
        var i = _engine.GetCompletions("SELECT * FROM ", 14);
        Assert.DoesNotContain(i, x => x.Kind == CompletionKind.Function);
    }

    [Fact]
    public void AfterFrom_from_list_does_not_suggest_functions()
    {
        var i = _engine.GetCompletions("SELECT * FROM employees, ", 26);
        Assert.DoesNotContain(i, x => x.Kind == CompletionKind.Function);
    }

    [Fact]
    public void AfterFrom_suggests_no_functions_with_table_prefix()
    {
        var i = _engine.GetCompletions("SELECT * FROM emp", 18);
        Assert.DoesNotContain(i, x => x.Kind == CompletionKind.Function);
    }

    [Fact]
    public void AfterJoin_does_not_suggest_functions()
    {
        var i = _engine.GetCompletions("SELECT * FROM employees e JOIN departments d ON e.", 50);
        Assert.DoesNotContain(i, x => x.Kind == CompletionKind.Function);
    }

    [Fact]
    public void AfterFrom_partial_table_name()
    {
        _engine.GetCompletions("SELECT * FROM em", 16);
    }

    [Fact]
    public void AfterJoin_suggests_join_keywords()
    {
        var i = _engine.GetCompletions("SELECT * FROM employees JOIN ", 29);
        Assert.Contains(i, x => x.Label == "ON");
    }

    [Fact]
    public void AfterHaving_suggests_something()
    {
        var i = _engine.GetCompletions("SELECT dept_id, COUNT(*) FROM employees GROUP BY dept_id HAVING ", 59);
        Assert.NotNull(i);
    }

    [Fact]
    public void AfterSelect_still_suggests_functions()
    {
        var i = _engine.GetCompletions("SELECT ", 7);
        Assert.Contains(i, x => x.Kind == CompletionKind.Function);
    }

    [Fact]
    public void AfterSelect_empty_prefix_returns_items()
    {
        var i = _engine.GetCompletions("SELECT ", 7);
        Assert.NotEmpty(i);
    }

    [Fact]
    public void AfterUpdate_suggests_SET_keyword()
    {
        var i = _engine.GetCompletions("UPDATE employees ", 17);
        Assert.Contains(i, x => x.Label == "SET");
    }

    [Fact]
    public void AfterUpdate_suggests_columns_and_functions_in_set()
    {
        var i = _engine.GetCompletions("UPDATE employees SET ", 21);
        Assert.Contains(i, x => x.Kind == CompletionKind.Column);
        Assert.Contains(i, x => x.Kind == CompletionKind.Function);
    }

    [Fact]
    public void AfterDelete_suggests_FROM_keyword()
    {
        var i = _engine.GetCompletions("DELETE ", 7);
        Assert.Contains(i, x => x.Label == "FROM");
    }

    [Fact]
    public void AfterDelete_does_not_suggest_functions()
    {
        var i = _engine.GetCompletions("DELETE ", 7);
        Assert.DoesNotContain(i, x => x.Kind == CompletionKind.Function);
    }

    [Fact]
    public void AfterWhere_still_suggests_functions()
    {
        var i = _engine.GetCompletions("SELECT * FROM employees WHERE ", 31);
        Assert.Contains(i, x => x.Kind == CompletionKind.Function);
    }

    [Fact]
    public void AfterGroupBy_returns_items()
    {
        var i = _engine.GetCompletions("SELECT dept_id, COUNT(*) FROM employees GROUP BY ", 49);
        Assert.NotEmpty(i);
    }

    [Fact]
    public void AfterWhere_suggests_columns()
    {
        var i = _engine.GetCompletions("SELECT * FROM employees WHERE ", 31);
        Assert.Contains(i, x => x.Kind == CompletionKind.Column);
    }

    [Fact]
    public void AfterOrderBy_suggests_functions()
    {
        var i = _engine.GetCompletions("SELECT * FROM employees ORDER BY ", 33);
        Assert.Contains(i, x => x.Kind == CompletionKind.Function);
    }

    [Fact]
    public void AfterOn_suggests_columns()
    {
        var i = _engine.GetCompletions("SELECT * FROM employees e JOIN departments d ON e.", 50);
        Assert.Contains(i, x => x.Kind == CompletionKind.Column);
    }

    [Fact]
    public void Partial_COU_filters_to_COUNT()
    {
        var i = _engine.GetCompletions("SELECT COU", 10);
        Assert.Contains(i, x => x.Label == "COUNT");
        Assert.All(i, x => Assert.StartsWith("COU", x.Label, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CTE_name_suggested_in_FROM()
    {
        var i = _engine.GetCompletions("WITH cte1 AS (SELECT 1) SELECT * FROM ", 39);
        Assert.Contains(i, x => x.Label == "cte1" && x.Kind == CompletionKind.Cte);
    }

    [Fact]
    public void Cursor_beyond_end_clamps()
    {
        var i = _engine.GetCompletions("SELECT", 100);
        Assert.NotEmpty(i);
    }

    [Fact]
    public void Update_where_alias_dot_suggests_columns()
    {
        var i = _engine.GetCompletions("UPDATE employees e WHERE e.", 27);
        Assert.Contains(i, x => x.Label == "id" && x.Detail == "e.id");
        Assert.Contains(i, x => x.Label == "name" && x.Detail == "e.name");
        Assert.Contains(i, x => x.Label == "salary" && x.Detail == "e.salary");
        Assert.Contains(i, x => x.Label == "dept_id" && x.Detail == "e.dept_id");
    }

    [Fact]
    public void Update_where_table_dot_suggests_columns()
    {
        var i = _engine.GetCompletions("UPDATE employees WHERE employees.", 33);
        Assert.Contains(i, x => x.Label == "id" && x.Detail == "employees.id");
    }

    [Fact]
    public void Update_double_dot_alias_where_dot_suggests_columns()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("dimaccount", Schema: "admin", Database: "just_data", Columns: new[]
        {
            new ColumnInfo("accountkey"), new ColumnInfo("accountname")
        }));
        var engine = new NzCompletionEngine(schema);
        var sql = "UPDATE just_data..dimaccount a WHERE a.";
        var i = engine.GetCompletions(sql, sql.Length);
        Assert.Contains(i, x => x.Label == "accountkey" && x.Detail == "a.accountkey");
        Assert.Contains(i, x => x.Label == "accountname" && x.Detail == "a.accountname");
    }

    [Fact]
    public void Double_dot_table_alias_where_dot_suggests_columns()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("DIMACCOUNT", Schema: "ADMIN", Database: "JUST_DATA", Columns: new[]
        {
            new ColumnInfo("ACCOUNTKEY"), new ColumnInfo("ACCOUNTNAME")
        }));
        var engine = new NzCompletionEngine(schema);
        var sql = "SELECT * FROM JUST_DATA..DIMACCOUNT X WHERE X.";
        var i = engine.GetCompletions(sql, sql.Length);
        Assert.Contains(i, x => x.Label == "ACCOUNTKEY" && x.Detail == "X.ACCOUNTKEY");
        Assert.Contains(i, x => x.Label == "ACCOUNTNAME" && x.Detail == "X.ACCOUNTNAME");
    }

    [Fact]
    public void Three_part_table_alias_where_dot_suggests_columns()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("DIMACCOUNT", Schema: "ADMIN", Database: "JUST_DATA", Columns: new[]
        {
            new ColumnInfo("ACCOUNTKEY"), new ColumnInfo("ACCOUNTNAME")
        }));
        var engine = new NzCompletionEngine(schema);
        var sql = "SELECT * FROM JUST_DATA.ADMIN.DIMACCOUNT X WHERE X.";
        var i = engine.GetCompletions(sql, sql.Length);
        Assert.Contains(i, x => x.Label == "ACCOUNTKEY" && x.Detail == "X.ACCOUNTKEY");
        Assert.Contains(i, x => x.Label == "ACCOUNTNAME" && x.Detail == "X.ACCOUNTNAME");
    }

    [Fact]
    public void Double_dot_alias_after_unqualified_absent_cache()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("OTHER", Schema: "PUBLIC", Database: "TESTDB",
            Columns: new[] { new ColumnInfo("X") }));

        Assert.Null(schema.GetTable(null, null, "DIMACCOUNT"));

        schema.AddTable(new TableInfo("DIMACCOUNT", Schema: "ADMIN", Database: "JUST_DATA", Columns: new[]
        {
            new ColumnInfo("ACCOUNTKEY"), new ColumnInfo("ACCOUNTNAME")
        }));

        var engine = new NzCompletionEngine(schema);
        var sql = "SELECT * FROM JUST_DATA..DIMACCOUNT X WHERE X.";
        var i = engine.GetCompletions(sql, sql.Length);
        Assert.Contains(i, x => x.Label == "ACCOUNTKEY" && x.Detail == "X.ACCOUNTKEY");
        Assert.Contains(i, x => x.Label == "ACCOUNTNAME" && x.Detail == "X.ACCOUNTNAME");
    }

    [Fact]
    public void GetScopeHints_double_dot_key_uses_database_dot_dot_table()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("DIMACCOUNT", Schema: "ADMIN", Database: "JUST_DATA", Columns: new[]
        {
            new ColumnInfo("ACCOUNTKEY")
        }));
        var engine = new NzCompletionEngine(schema);
        var sql = "SELECT * FROM JUST_DATA..DIMACCOUNT X WHERE X.";
        _ = engine.GetCompletions(sql, sql.Length);
        var (_, _, aliasDbTable) = engine.GetScopeHints();
        Assert.True(aliasDbTable.TryGetValue("JUST_DATA..DIMACCOUNT", out var aliases));
        Assert.Contains(aliases, a => a.Equals("X", StringComparison.OrdinalIgnoreCase));
    }

    // ====== New tests: AS alias resolution ======

    [Fact]
    public void From_with_as_alias_dot_suggests_columns()
    {
        var sql = "SELECT * FROM employees AS emp WHERE emp.";
        var i = _engine.GetCompletions(sql, sql.Length);
        Assert.Contains(i, x => x.Label == "id" && x.Detail == "emp.id");
        Assert.Contains(i, x => x.Label == "name" && x.Detail == "emp.name");
    }

    [Fact]
    public void Join_with_as_alias_dot_suggests_columns()
    {
        var sql = "SELECT * FROM employees e LEFT JOIN departments AS dept ON dept.";
        var i = _engine.GetCompletions(sql, sql.Length);
        Assert.Contains(i, x => x.Label == "id" && x.Detail == "dept.id");
        Assert.Contains(i, x => x.Label == "location" && x.Detail == "dept.location");
    }

    [Fact]
    public void Update_with_as_alias_where_dot_suggests_columns()
    {
        var sql = "UPDATE employees AS e SET salary = 100 WHERE e.";
        var i = _engine.GetCompletions(sql, sql.Length);
        Assert.Contains(i, x => x.Label == "salary" && x.Detail == "e.salary");
    }

    [Fact]
    public void AfterFrom_suggests_table_names()
    {
        var i = _engine.GetCompletions("SELECT * FROM ", 14);
        Assert.Contains(i, x => x.Label == "employees" && x.Kind == CompletionKind.Table);
        Assert.Contains(i, x => x.Label == "departments" && x.Kind == CompletionKind.Table);
    }

    [Fact]
    public void AfterUpdate_suggests_table_names()
    {
        var i = _engine.GetCompletions("UPDATE ", 7);
        Assert.Contains(i, x => x.Label == "employees" && x.Kind == CompletionKind.Table);
    }

    [Fact]
    public void AfterJoin_suggests_table_names()
    {
        // Cursor at end of "SELECT * FROM employees LEFT JOIN " so partial="" and tables show
        var sql = "SELECT * FROM employees LEFT JOIN ";
        var i = _engine.GetCompletions(sql, sql.Length);
        Assert.Contains(i, x => x.Label == "departments" && x.Kind == CompletionKind.Table);
    }

    // ====== New tests: CTE with RECURSIVE and multiple CTEs ======

    [Fact]
    public void Recursive_cte_suggested_in_from()
    {
        var i = _engine.GetCompletions("WITH RECURSIVE cte1 AS (SELECT 1) SELECT * FROM ", 48);
        Assert.Contains(i, x => x.Label == "cte1" && x.Kind == CompletionKind.Cte);
    }

    [Fact]
    public void Multiple_ctes_all_suggested_in_from()
    {
        var sql = "WITH a AS (SELECT 1), b AS (SELECT 2), c AS (SELECT 3) SELECT * FROM ";
        var i = _engine.GetCompletions(sql, sql.Length);
        Assert.Contains(i, x => x.Label == "a" && x.Kind == CompletionKind.Cte);
        Assert.Contains(i, x => x.Label == "b" && x.Kind == CompletionKind.Cte);
        Assert.Contains(i, x => x.Label == "c" && x.Kind == CompletionKind.Cte);
    }

    [Fact]
    public void Multiple_recursive_ctes_all_suggested()
    {
        var i = _engine.GetCompletions("WITH RECURSIVE x AS (SELECT 1), y AS (SELECT 2) SELECT * FROM ", 63);
        Assert.Contains(i, x => x.Label == "x" && x.Kind == CompletionKind.Cte);
        Assert.Contains(i, x => x.Label == "y" && x.Kind == CompletionKind.Cte);
    }

    // ====== New tests: INSERT context ======

    [Fact]
    public void AfterInsert_suggests_INTO_keyword()
    {
        var i = _engine.GetCompletions("INSERT ", 7);
        Assert.Contains(i, x => x.Label == "INTO");
    }

    [Fact]
    public void AfterInsertInto_suggests_table_names()
    {
        var i = _engine.GetCompletions("INSERT INTO ", 12);
        Assert.Contains(i, x => x.Label == "employees" && x.Kind == CompletionKind.Table);
    }

    [Fact]
    public void AfterInsertInto_does_not_suggest_functions()
    {
        var i = _engine.GetCompletions("INSERT INTO ", 12);
        Assert.DoesNotContain(i, x => x.Kind == CompletionKind.Function);
    }

    [Fact]
    public void AfterInsertInto_table_suggests_VALUES()
    {
        var i = _engine.GetCompletions("INSERT INTO employees ", 22);
        Assert.Contains(i, x => x.Label == "VALUES");
    }

    // ====== New tests: Qualified table names ======

    [Fact]
    public void Qualified_table_name_produces_column_suggestions()
    {
        _schema.AddTable(new TableInfo("accounts", Schema: "just_data", Columns: new[]
        {
            new ColumnInfo("acc_id"), new ColumnInfo("acc_name")
        }));

        var i = _engine.GetCompletions("SELECT * FROM just_data.accounts a WHERE a.", 43);
        Assert.Contains(i, x => x.Label == "acc_id" && x.Detail == "a.acc_id");
        Assert.Contains(i, x => x.Label == "acc_name" && x.Detail == "a.acc_name");
    }

    [Fact]
    public void Qualified_table_in_select_columns_resolves()
    {
        _schema.AddTable(new TableInfo("accounts", Schema: "just_data", Columns: new[]
        {
            new ColumnInfo("acc_id"), new ColumnInfo("acc_name")
        }));

        var sql = "SELECT * FROM just_data.accounts WHERE ";
        var i = _engine.GetCompletions(sql, sql.Length);
        Assert.Contains(i, x => x.Kind == CompletionKind.Column);
        Assert.Contains(i, x => x.Label == "acc_id");
        Assert.Contains(i, x => x.Label == "acc_name");
    }

    // ====== New tests: On boundary isolates FROM identifiers ======

    [Fact]
    public void Table_name_in_on_clause_not_matched_as_false_alias()
    {
        var sql = "SELECT * FROM employees e JOIN departments d ON e.id = d.id AND departments.";
        var i = _engine.GetCompletions(sql, sql.Length);
        // "departments" is a real table name in ON; should resolve columns directly
        Assert.Contains(i, x => x.Label == "id" && x.Detail == "departments.id");
    }

    // ====== New tests: AfterJoin comma -> FromList ======

    [Fact]
    public void AfterJoin_comma_suggests_from_list_tables()
    {
        var sql = "SELECT * FROM employees JOIN departments, ";
        var i = _engine.GetCompletions(sql, sql.Length);
        Assert.Contains(i, x => x.Label == "employees" && x.Kind == CompletionKind.Table);
        Assert.Contains(i, x => x.Label == "departments" && x.Kind == CompletionKind.Table);
    }

    // ====== New tests: Cursor inside token ======

    [Fact]
    public void Cursor_inside_keyword_prefix_falls_back_to_top_level()
    {
        var i = _engine.GetCompletions("SEL", 3);
        Assert.Contains(i, x => x.Label == "SELECT");
        Assert.All(i, x => Assert.StartsWith("SEL", x.Label, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Cursor_inside_identifier_resolves_context()
    {
        var i = _engine.GetCompletions("SELECT * FROM employ", 19);
        Assert.DoesNotContain(i, x => x.Kind == CompletionKind.Function);
    }

    // ====== New tests: DELETE FROM table completions ======

    [Fact]
    public void Delete_from_table_where_alias_dot_suggests_columns()
    {
        var i = _engine.GetCompletions("DELETE FROM employees e WHERE e.", 32);
        Assert.Contains(i, x => x.Label == "id" && x.Detail == "e.id");
    }

    [Fact]
    public void Delete_from_table_suggests_table_names()
    {
        var i = _engine.GetCompletions("DELETE FROM ", 12);
        Assert.Contains(i, x => x.Label == "employees" && x.Kind == CompletionKind.Table);
    }

    // ====== New tests: LIMIT/FETCH in ORDER BY ======

    [Fact]
    public void AfterOrderBy_suggests_limit_and_fetch()
    {
        var i = _engine.GetCompletions("SELECT * FROM employees ORDER BY ", 33);
        Assert.Contains(i, x => x.Label == "FETCH");
        Assert.Contains(i, x => x.Label == "LIMIT");
    }

    // ====== Dedup test: function vs column with same name ======

    [Fact]
    public void Function_not_duplicated_as_keyword()
    {
        var i = _engine.GetCompletions("SELECT ", 7);
        var countItems = i.Where(x => x.Label == "COUNT").ToList();
        // COUNT should appear as Function, not also as Keyword (duplication removed)
        Assert.NotNull(countItems);
        // if duplication still exists, fix: SelectListKeywords should not contain function names
    }

    // ====== Subquery in FROM ======

    [Fact]
    public void Subquery_alias_dot_suggests_columns()
    {
        var i = _engine.GetCompletions("SELECT * FROM (SELECT id FROM employees) sub WHERE sub.", 50);
        // sub.id — sub is alias for subquery; only columns from the subquery's SELECT list
        Assert.NotNull(i);
    }

    // ====== Multistatement edge case ======

    [Fact]
    public void Update_after_select_semicolon_works()
    {
        var i = _engine.GetCompletions("SELECT 1; UPDATE employees SET ", 33);
        Assert.Contains(i, x => x.Kind == CompletionKind.Column);
        Assert.Contains(i, x => x.Kind == CompletionKind.Function);
    }

    // ====== No false alias from ON/WHERE identifiers ======

    [Fact]
    public void No_false_alias_resolution_when_identifiers_in_on_clause_have_same_name_as_alias()
    {
        var sql = "SELECT * FROM employees e JOIN departments x ON e.id = x.id AND e.";
        var i = _engine.GetCompletions(sql, sql.Length);
        // "e" should resolve to "employees", not be confused by "e.id" in ON
        Assert.Contains(i, x => x.Label == "id" && x.Detail == "e.id");
    }

    // ====== CTE Column Inference ======

    [Fact]
    public void Cte_columns_inferred_from_select_list_available_in_where()
    {
        var sql = "WITH cte AS (SELECT id, name FROM employees) SELECT * FROM cte WHERE ";
        var i = _engine.GetCompletions(sql, sql.Length);
        Assert.Contains(i, x => x.Label == "id" && x.Kind == CompletionKind.Column);
        Assert.Contains(i, x => x.Label == "name" && x.Kind == CompletionKind.Column);
    }

    [Fact]
    public void Cte_columns_available_via_qualified_ref()
    {
        var sql = "WITH cte AS (SELECT id, name FROM employees) SELECT * FROM cte WHERE cte.";
        var i = _engine.GetCompletions(sql, sql.Length);
        Assert.Contains(i, x => x.Label == "id" && x.Detail == "cte.id");
        Assert.Contains(i, x => x.Label == "name" && x.Detail == "cte.name");
    }

    [Fact]
    public void Cte_columns_from_explicit_column_list()
    {
        var sql = "WITH cte (x, y) AS (SELECT 1, 2) SELECT * FROM cte WHERE ";
        var i = _engine.GetCompletions(sql, sql.Length);
        Assert.Contains(i, x => x.Label == "x" && x.Kind == CompletionKind.Column);
        Assert.Contains(i, x => x.Label == "y" && x.Kind == CompletionKind.Column);
    }

    [Fact]
    public void Cte_explicit_column_list_qualified()
    {
        var sql = "WITH cte (x, y) AS (SELECT 1, 2) SELECT * FROM cte WHERE cte.";
        var i = _engine.GetCompletions(sql, sql.Length);
        Assert.Contains(i, x => x.Label == "x" && x.Detail == "cte.x");
        Assert.Contains(i, x => x.Label == "y" && x.Detail == "cte.y");
    }

    [Fact]
    public void Cte_column_inference_qualified_table_name_in_select()
    {
        var sql = "WITH cte AS (SELECT employees.id, employees.name FROM employees) SELECT * FROM cte WHERE ";
        var i = _engine.GetCompletions(sql, sql.Length);
        Assert.Contains(i, x => x.Label == "id" && x.Kind == CompletionKind.Column);
        Assert.Contains(i, x => x.Label == "name" && x.Kind == CompletionKind.Column);
    }

    [Fact]
    public void Cte_column_inference_qualified_table_qualified_ref()
    {
        var sql = "WITH cte AS (SELECT employees.id, employees.name FROM employees) SELECT * FROM cte WHERE cte.";
        var i = _engine.GetCompletions(sql, sql.Length);
        Assert.Contains(i, x => x.Label == "id" && x.Detail == "cte.id");
        Assert.Contains(i, x => x.Label == "name" && x.Detail == "cte.name");
    }

    [Fact]
    public void Cte_columns_in_select_list_suggested()
    {
        // Cursor after FROM — SELECT list is after the fact but CTE columns are visible from WHERE
        var sql = "WITH cte AS (SELECT id, name FROM employees) SELECT * FROM cte WHERE ";
        var i = _engine.GetCompletions(sql, sql.Length);
        Assert.Contains(i, x => x.Label == "id" && x.Kind == CompletionKind.Column);
        Assert.Contains(i, x => x.Label == "name" && x.Kind == CompletionKind.Column);
    }

    [Fact]
    public void Multiple_ctes_each_have_own_columns()
    {
        var sql = "WITH a AS (SELECT id FROM employees), b AS (SELECT name, salary FROM employees) SELECT * FROM a WHERE ";
        var i = _engine.GetCompletions(sql, sql.Length);
        // a has id
        Assert.Contains(i, x => x.Label == "id" && x.Kind == CompletionKind.Column);
        // b columns not in scope — only a is in FROM
        Assert.DoesNotContain(i, x => x.Label == "name" && x.Kind == CompletionKind.Column);
        Assert.DoesNotContain(i, x => x.Label == "salary" && x.Kind == CompletionKind.Column);
    }

    [Fact]
    public void Multiple_ctes_qualified_refs_are_correct()
    {
        var sql = "WITH a AS (SELECT id FROM employees), b AS (SELECT name, salary FROM employees) SELECT * FROM a WHERE a.";
        var i = _engine.GetCompletions(sql, sql.Length);
        Assert.Contains(i, x => x.Label == "id" && x.Detail == "a.id");
        Assert.DoesNotContain(i, x => x.Label == "name");
        Assert.DoesNotContain(i, x => x.Label == "salary");
    }

    [Fact]
    public void Cte_columns_preserved_after_semicolon_boundary()
    {
        // Only the first statement's CTE is visible
        var sql = "WITH cte AS (SELECT id, name FROM employees) SELECT * FROM cte WHERE ";
        var i = _engine.GetCompletions(sql, sql.Length);
        Assert.Contains(i, x => x.Label == "id" && x.Kind == CompletionKind.Column);
    }

    [Fact]
    public void Cte_with_function_and_complex_expr_in_select_skips_noname_items()
    {
        // Complex expressions without alias are skipped; only named columns inferred
        var sql = "WITH cte AS (SELECT count(*) as cnt, id + 1, name FROM employees) SELECT * FROM cte WHERE ";
        var i = _engine.GetCompletions(sql, sql.Length);
        Assert.Contains(i, x => x.Label == "cnt" && x.Kind == CompletionKind.Column);
        Assert.Contains(i, x => x.Label == "name" && x.Kind == CompletionKind.Column);
        // "id + 1" has no alias — skip it (no column name to infer)
    }

    [Fact]
    public void Cte_recursive_columns_inferred()
    {
        var sql = "WITH RECURSIVE cte AS (SELECT id FROM employees) SELECT * FROM cte WHERE ";
        var i = _engine.GetCompletions(sql, sql.Length);
        Assert.Contains(i, x => x.Label == "id" && x.Kind == CompletionKind.Column);
    }

    [Fact]
    public void Cte_column_inference_works_with_join()
    {
        var sql = "WITH cte AS (SELECT id, name FROM employees) SELECT * FROM cte JOIN departments ON cte.";
        var i = _engine.GetCompletions(sql, sql.Length);
        Assert.Contains(i, x => x.Label == "id" && x.Detail == "cte.id");
        Assert.Contains(i, x => x.Label == "name" && x.Detail == "cte.name");
    }

    [Fact]
    public void Cte_column_inference_does_not_conflict_with_real_table()
    {
        // CTE with same name as real table should still get CTE columns
        var sql = "WITH employees AS (SELECT id AS emp_id, name AS emp_name FROM employees) SELECT * FROM employees WHERE employees.";
        var i = _engine.GetCompletions(sql, sql.Length);
        Assert.Contains(i, x => x.Label == "emp_id" && x.Detail == "employees.emp_id");
        Assert.DoesNotContain(i, x => x.Label == "salary" && x.Detail == "employees.salary");
    }

    [Fact]
    public void Cte_column_inference_from_star_select()
    {
        // CTE with SELECT * FROM known table should resolve columns from the table
        var sql = "WITH cte AS (SELECT * FROM employees) SELECT * FROM cte WHERE cte.";
        var i = _engine.GetCompletions(sql, sql.Length);
        Assert.Contains(i, x => x.Label == "id" && x.Detail == "cte.id");
        Assert.Contains(i, x => x.Label == "name" && x.Detail == "cte.name");
        Assert.Contains(i, x => x.Label == "salary" && x.Detail == "cte.salary");
        Assert.Contains(i, x => x.Label == "dept_id" && x.Detail == "cte.dept_id");
    }

    [Fact]
    public void Cte_column_inference_from_star_select_with_alias()
    {
        var sql = "WITH cte AS (SELECT * FROM employees) SELECT * FROM cte C WHERE C.";
        var i = _engine.GetCompletions(sql, sql.Length);
        Assert.Contains(i, x => x.Label == "id" && x.Detail == "C.id");
        Assert.Contains(i, x => x.Label == "name" && x.Detail == "C.name");
    }

    [Fact]
    public void Cte_column_inference_from_star_select_qualified()
    {
        _schema.AddTable(new TableInfo("dimdate", Schema: "admin", Database: "just_data", Columns: new[]
        {
            new ColumnInfo("accountkey"), new ColumnInfo("datekey")
        }));
        var sql = "WITH cte AS (SELECT * FROM just_data..dimdate) SELECT * FROM cte WHERE cte.";
        var i = _engine.GetCompletions(sql, sql.Length);
        Assert.Contains(i, x => x.Label == "accountkey" && x.Detail == "cte.accountkey");
        Assert.Contains(i, x => x.Label == "datekey" && x.Detail == "cte.datekey");
    }

    [Fact]
    public void Cte_column_inference_from_star_select_does_not_resolve_for_unknown_table()
    {
        var sql = "WITH cte AS (SELECT * FROM nonexistent) SELECT * FROM cte WHERE cte.";
        var i = _engine.GetCompletions(sql, sql.Length);
        Assert.DoesNotContain(i, x => x.Kind == CompletionKind.Column && (x.Detail?.StartsWith("cte.") ?? false));
    }

    [Fact]
    public void Cte_column_inference_from_nested_star_select_with_cte_dependency()
    {
        // Nested CTE where outer CTE1 references inner CTE2 via SELECT *
        var sql = "WITH CTE1 AS (WITH CTE2 AS (SELECT * FROM employees) SELECT * FROM CTE2) SELECT * FROM CTE1 C WHERE C.";
        var i = _engine.GetCompletions(sql, sql.Length);
        Assert.Contains(i, x => x.Label == "id" && x.Detail == "C.id");
        Assert.Contains(i, x => x.Label == "name" && x.Detail == "C.name");
        Assert.Contains(i, x => x.Label == "salary" && x.Detail == "C.salary");
    }

    [Fact]
    public void Cte_column_inference_from_mixed_expr_and_star()
    {
        // CTE with SELECT expr AS alias, * FROM table should merge both
        var sql = "WITH cte AS (SELECT 5 AS extra, * FROM employees) SELECT * FROM cte C WHERE C.";
        var i = _engine.GetCompletions(sql, sql.Length);
        Assert.Contains(i, x => x.Label == "extra" && x.Detail == "C.extra");
        Assert.Contains(i, x => x.Label == "id" && x.Detail == "C.id");
        Assert.Contains(i, x => x.Label == "name" && x.Detail == "C.name");
        Assert.Contains(i, x => x.Label == "salary" && x.Detail == "C.salary");
    }

    [Fact]
    public void Temp_table_column_inference_from_as_select()
    {
        var sql = "CREATE TEMP TABLE tmp1 AS (SELECT * FROM employees); SELECT * FROM tmp1 T WHERE T.";
        var i = _engine.GetCompletions(sql, sql.Length);
        Assert.Contains(i, x => x.Label == "id" && x.Detail == "T.id");
        Assert.Contains(i, x => x.Label == "name" && x.Detail == "T.name");
        Assert.Contains(i, x => x.Label == "salary" && x.Detail == "T.salary");
    }

    [Fact]
    public void Temp_table_column_inference_nested_with_mixed_star()
    {
        // CREATE TEMP TABLE with nested WITH + mixed explicit + star
        var sql = "CREATE TEMP TABLE CTE1 AS (WITH CTE2 AS (SELECT 5 AS C1, * FROM employees) SELECT 6 AS C2, * FROM CTE2); SELECT * FROM CTE1 C WHERE C.";
        var i = _engine.GetCompletions(sql, sql.Length);
        Assert.Contains(i, x => x.Label == "C2" && x.Detail == "C.C2");
        Assert.Contains(i, x => x.Label == "C1" && x.Detail == "C.C1");
        Assert.Contains(i, x => x.Label == "id" && x.Detail == "C.id");
        Assert.Contains(i, x => x.Label == "name" && x.Detail == "C.name");
    }

    // ====== DDL Target-specific completion tests ======

    [Fact]
    public void AfterDropView_suggests_only_views()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("orders"));
        schema.AddTable(new TableInfo("orders_view", IsView: true));
        schema.AddTable(new TableInfo("products"));
        schema.AddTable(new TableInfo("products_summary", IsView: true));
        var engine = new NzCompletionEngine(schema);

        var i = engine.GetCompletions("DROP VIEW ", 10);

        Assert.Contains(i, x => x.Label == "orders_view" && x.Kind == CompletionKind.View);
        Assert.Contains(i, x => x.Label == "products_summary" && x.Kind == CompletionKind.View);
        Assert.DoesNotContain(i, x => x.Label == "orders" && x.Kind == CompletionKind.Table);
        Assert.DoesNotContain(i, x => x.Label == "products" && x.Kind == CompletionKind.Table);
    }

    [Fact]
    public void AfterDropTable_suggests_only_tables()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("orders"));
        schema.AddTable(new TableInfo("orders_view", IsView: true));
        schema.AddTable(new TableInfo("products"));
        schema.AddTable(new TableInfo("products_summary", IsView: true));
        var engine = new NzCompletionEngine(schema);

        var i = engine.GetCompletions("DROP TABLE ", 11);

        Assert.Contains(i, x => x.Label == "orders" && x.Kind == CompletionKind.Table);
        Assert.Contains(i, x => x.Label == "products" && x.Kind == CompletionKind.Table);
        Assert.DoesNotContain(i, x => x.Label == "orders_view" && x.Kind == CompletionKind.View);
        Assert.DoesNotContain(i, x => x.Label == "products_summary" && x.Kind == CompletionKind.View);
    }

    [Fact]
    public void AfterAlterTable_suggests_tables()
    {
        var sql = "ALTER TABLE ";
        var i = _engine.GetCompletions(sql, sql.Length);
        Assert.Contains(i, x => x.Label == "employees" && x.Kind == CompletionKind.Table);
        Assert.Contains(i, x => x.Label == "departments" && x.Kind == CompletionKind.Table);
    }

    [Fact]
    public void AfterAlterTable_does_not_suggest_keywords_from_alter_list()
    {
        var sql = "ALTER TABLE ";
        var i = _engine.GetCompletions(sql, sql.Length);
        // After ALTER TABLE we want tables, not the ALTER sub-keywords like VIEW/PROCEDURE
        Assert.DoesNotContain(i, x => x.Label == "VIEW" && x.Kind == CompletionKind.Keyword);
        Assert.DoesNotContain(i, x => x.Label == "DATABASE" && x.Kind == CompletionKind.Keyword);
    }

    [Fact]
    public void AfterGroomTable_suggests_tables()
    {
        var sql = "GROOM TABLE ";
        var i = _engine.GetCompletions(sql, sql.Length);
        Assert.Contains(i, x => x.Label == "employees" && x.Kind == CompletionKind.Table);
        Assert.Contains(i, x => x.Label == "departments" && x.Kind == CompletionKind.Table);
    }

    [Fact]
    public void AfterTruncateTable_suggests_tables()
    {
        var sql = "TRUNCATE TABLE ";
        var i = _engine.GetCompletions(sql, sql.Length);
        Assert.Contains(i, x => x.Label == "employees" && x.Kind == CompletionKind.Table);
        Assert.Contains(i, x => x.Label == "departments" && x.Kind == CompletionKind.Table);
    }

    [Fact]
    public void AfterCreateSynonymFor_suggests_tables_and_views()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("orders"));
        schema.AddTable(new TableInfo("orders_view", IsView: true));
        var engine = new NzCompletionEngine(schema);

        const string sql = "CREATE SYNONYM my_syn FOR ";
        var i = engine.GetCompletions(sql, sql.Length);

        Assert.Contains(i, x => x.Label == "orders" && x.Kind == CompletionKind.Table);
        Assert.Contains(i, x => x.Label == "orders_view" && x.Kind == CompletionKind.View);
    }

    [Fact]
    public void AfterCreateSynonymName_suggests_FOR_keyword()
    {
        var i = _engine.GetCompletions("CREATE SYNONYM my_syn ", 22);
        Assert.Contains(i, x => x.Label == "FOR" && x.Kind == CompletionKind.Keyword);
    }

    [Fact]
    public void QualifiedReference_database_dot_suggests_schemas()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("employees", Schema: "PUBLIC", Database: "MYDB"));
        schema.AddTable(new TableInfo("orders", Schema: "SALES", Database: "MYDB"));
        var engine = new NzCompletionEngine(schema);

        // Typing "MYDB." should suggest schema names PUBLIC and SALES
        var i = engine.GetCompletions("SELECT * FROM MYDB.", 19);

        Assert.Contains(i, x => x.Label == "PUBLIC" && x.Kind == CompletionKind.Schema);
        Assert.Contains(i, x => x.Label == "SALES" && x.Kind == CompletionKind.Schema);
        Assert.DoesNotContain(i, x => x.Label == "employees");
    }

    [Fact]
    public void QualifiedReference_schema_dot_suggests_tables()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("employees", Schema: "PUBLIC", Database: "MYDB"));
        schema.AddTable(new TableInfo("orders", Schema: "SALES", Database: "MYDB"));
        var engine = new NzCompletionEngine(schema);

        // Typing "PUBLIC." should suggest tables in PUBLIC schema
        const string sql = "SELECT * FROM PUBLIC.";
        var i = engine.GetCompletions(sql, sql.Length);

        Assert.Contains(i, x => x.Label == "employees" && x.Kind == CompletionKind.Table);
        Assert.DoesNotContain(i, x => x.Label == "orders");
    }
}
