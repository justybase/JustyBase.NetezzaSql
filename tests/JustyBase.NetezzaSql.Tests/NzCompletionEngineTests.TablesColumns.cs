using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Completion;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed partial class NzCompletionEngineTests
{
    [Fact]
    public void AfterUpdate_suggests_columns_and_functions_in_set()
    {
        var i = _engine.GetCompletions("UPDATE employees SET ", 21);
        Assert.Contains(i, x => x.Kind == CompletionKind.Column);
        Assert.Contains(i, x => x.Kind == CompletionKind.Function);
    }


    [Fact]
    public void AfterOn_suggests_columns()
    {
        var i = _engine.GetCompletions("SELECT * FROM employees e JOIN departments d ON e.", 50);
        Assert.Contains(i, x => x.Kind == CompletionKind.Column);
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


    [Fact]
    public void Quoted_relation_identifiers_resolve_schema_table_and_alias_columns()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo(
            "Order Details",
            Schema: "Sales Data",
            Database: "DB1",
            Columns: [new ColumnInfo("Order Id"), new ColumnInfo("MixedCase")]));
        var engine = new NzCompletionEngine(schema);
        var sql = "SELECT * FROM \"Sales Data\".\"Order Details\" od WHERE od.";

        var items = engine.GetCompletions(sql, sql.Length);

        Assert.Contains(items, item => item.Label == "Order Id" && item.Detail == "od.Order Id");
        Assert.Contains(items, item => item.Label == "MixedCase" && item.Detail == "od.MixedCase");
    }


    [Fact]
    public void Quoted_table_in_database_double_dot_reference_resolves_alias_columns()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo(
            "Quoted Table",
            Schema: "ADMIN",
            Database: "DB1",
            Columns: [new ColumnInfo("MixedCase")]));
        var engine = new NzCompletionEngine(schema);
        var sql = "SELECT * FROM DB1..\"Quoted Table\" qt WHERE qt.";

        var items = engine.GetCompletions(sql, sql.Length);

        Assert.Contains(items, item => item.Label == "MixedCase" && item.Detail == "qt.MixedCase");
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
    public void AfterInsertInto_suggests_table_names()
    {
        var i = _engine.GetCompletions("INSERT INTO ", 12);
        Assert.Contains(i, x => x.Label == "employees" && x.Kind == CompletionKind.Table);
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
    public void Qualified_database_completion_is_scoped_and_supports_partial_schema_paths()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("AD_TABLE", "ADMIN", "JUST_DATA"));
        schema.AddTable(new TableInfo("OTHER_TABLE", "OTHER", "OTHER_DB"));
        schema.AddTable(new TableInfo("ADMIN_TABLE", "ADMIN", "JUST_DATA"));
        var engine = new NzCompletionEngine(schema, activeDatabase: "JUST_DATA");

        var schemas = engine.GetCompletions("SELECT * FROM JUST_DATA.", "SELECT * FROM JUST_DATA.".Length);
        Assert.Contains(schemas, item => item.Label == "ADMIN" && item.Kind == CompletionKind.Schema);
        Assert.DoesNotContain(schemas, item => item.Label == "OTHER" && item.Kind == CompletionKind.Schema);

        var partialSchemas = engine.GetCompletions("SELECT * FROM JUST_DATA.AD", "SELECT * FROM JUST_DATA.AD".Length);
        Assert.Contains(partialSchemas, item => item.Label == "ADMIN" && item.Kind == CompletionKind.Schema);
        Assert.DoesNotContain(partialSchemas, item => item.Label == "OTHER" && item.Kind == CompletionKind.Schema);

        var tables = engine.GetCompletions("SELECT * FROM JUST_DATA.ADMIN.", "SELECT * FROM JUST_DATA.ADMIN.".Length);
        Assert.Contains(tables, item => item.Label == "AD_TABLE" && item.Kind == CompletionKind.Table);
        Assert.DoesNotContain(tables, item => item.Label == "OTHER_TABLE");
    }


    [Fact]
    public void Unqualified_completion_uses_active_database()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("LOCAL_TABLE", "ADMIN", "JUST_DATA"));
        schema.AddTable(new TableInfo("FOREIGN_TABLE", "ADMIN", "OTHER_DB"));
        var engine = new NzCompletionEngine(schema, activeDatabase: "JUST_DATA");

        var items = engine.GetCompletions("SELECT * FROM ", "SELECT * FROM ".Length);
        Assert.Contains(items, item => item.Label == "LOCAL_TABLE");
        Assert.DoesNotContain(items, item => item.Label == "FOREIGN_TABLE");
    }


    [Fact]
    public void Sqlite_unqualified_table_prefers_main_schema_for_alias_columns()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("orders", Schema: "aux", Columns:
        [
            new ColumnInfo("aux_id")
        ]));
        schema.AddTable(new TableInfo("orders", Schema: "main", Columns:
        [
            new ColumnInfo("id"), new ColumnInfo("user_id")
        ]));
        var engine = new NzCompletionEngine(schema, dialect: SqlDialect.Sqlite);

        var qualifiedSql = "SELECT * FROM main.orders o WHERE o.";
        var unqualifiedSql = "SELECT * FROM orders o WHERE o.";
        var qualified = engine.GetCompletions(qualifiedSql, qualifiedSql.Length);
        var unqualified = engine.GetCompletions(unqualifiedSql, unqualifiedSql.Length);

        Assert.Contains(qualified, x => x.Label == "id" && x.Detail == "o.id");
        Assert.Contains(unqualified, x => x.Label == "id" && x.Detail == "o.id");
        Assert.DoesNotContain(unqualified, x => x.Label == "aux_id");
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
    public void Cte_columns_available_via_qualified_ref()
    {
        var sql = "WITH cte AS (SELECT id, name FROM employees) SELECT * FROM cte WHERE cte.";
        var i = _engine.GetCompletions(sql, sql.Length);
        Assert.Contains(i, x => x.Label == "id" && x.Detail == "cte.id");
        Assert.Contains(i, x => x.Label == "name" && x.Detail == "cte.name");
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

    // ===== FromClauseTail: continuation keywords after a completed FROM reference/alias =====

    [Fact]
    public void FromClauseTail_after_alias_suggests_continuation_keywords()
    {
        const string sql = "SELECT * FROM employees e ";
        var i = _engine.GetCompletions(sql, sql.Length);

        Assert.Contains(i, x => x.Label == "JOIN" && x.Kind == CompletionKind.Keyword);
        Assert.Contains(i, x => x.Label == "LEFT OUTER JOIN" && x.Kind == CompletionKind.Keyword);
        Assert.Contains(i, x => x.Label == "FULL OUTER JOIN" && x.Kind == CompletionKind.Keyword);
        Assert.Contains(i, x => x.Label == "WHERE" && x.Kind == CompletionKind.Keyword);
        Assert.Contains(i, x => x.Label == "GROUP BY" && x.Kind == CompletionKind.Keyword);
        Assert.Contains(i, x => x.Label == "ORDER BY" && x.Kind == CompletionKind.Keyword);
        Assert.Contains(i, x => x.Label == "LIMIT" && x.Kind == CompletionKind.Keyword);
        Assert.Contains(i, x => x.Label == "OFFSET" && x.Kind == CompletionKind.Keyword);
        Assert.Contains(i, x => x.Label == "FETCH" && x.Kind == CompletionKind.Keyword);
        Assert.DoesNotContain(i, x => x.Label == "employees");
        Assert.DoesNotContain(i, x => x.Label == "ON");
    }
}
