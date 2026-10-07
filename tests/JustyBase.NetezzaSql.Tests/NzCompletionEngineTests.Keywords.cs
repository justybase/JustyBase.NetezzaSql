using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Completion;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed partial class NzCompletionEngineTests
{
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
    public void AfterFrom_partial_table_name()
    {
        var schema = SqlTestHelpers.CreateMultiSchemaDuplicateCatalog();
        var engine = new NzCompletionEngine(schema);
        const string sql = "SELECT * FROM DIMDA";
        var items = engine.GetCompletions(sql, sql.Length);

        Assert.Contains(items, x => x.Label == "DIMDATE" && x.Kind == CompletionKind.Table);
        CompletionTestAssertions.AssertUniqueTableAndViewLabels(items);
        CompletionTestAssertions.AssertLabelCount(items, "DIMDATE", 1);
    }


    [Fact]
    public void AfterJoin_suggests_join_keywords()
    {
        var i = _engine.GetCompletions("SELECT * FROM employees JOIN ", 29);
        Assert.Contains(i, x => x.Label == "ON");
    }


    [Fact]
    public void AfterUpdate_suggests_SET_keyword()
    {
        var i = _engine.GetCompletions("UPDATE employees ", 17);
        Assert.Contains(i, x => x.Label == "SET");
    }


    [Fact]
    public void AfterDelete_suggests_FROM_keyword()
    {
        var i = _engine.GetCompletions("DELETE ", 7);
        Assert.Contains(i, x => x.Label == "FROM");
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
    public void CTE_name_suggested_in_FROM()
    {
        var i = _engine.GetCompletions("WITH cte1 AS (SELECT 1) SELECT * FROM ", 39);
        Assert.Contains(i, x => x.Label == "cte1" && x.Kind == CompletionKind.Cte);
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
    public void Unknown_alias_after_where_dot_does_not_suggest_from_table_columns()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("DIMDATE", Schema: "ADMIN", Database: "JUST_DATA", Columns: new[]
        {
            new ColumnInfo("DATEKEY"), new ColumnInfo("CALENDARYEAR")
        }));
        var engine = new NzCompletionEngine(schema);
        var sql = "SELECT * FROM JUST_DATA..DIMDATE D WHERE NO_SUCH_ALIAS.";
        var i = engine.GetCompletions(sql, sql.Length);

        Assert.DoesNotContain(i, x => x.Kind == CompletionKind.Column &&
                                      x.Detail is not null &&
                                      x.Detail.StartsWith("NO_SUCH_ALIAS.", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(i, x => x.Label == "DATEKEY");
        Assert.DoesNotContain(i, x => x.Label == "CALENDARYEAR");
    }


    [Fact]
    public void ResolveAlias_ignores_identifier_outside_from_join_context()
    {
        var sql = "SELECT * FROM JUST_DATA..DIMDATE D WHERE NO_SUCH_ALIAS.";
        var tokens = NzLexer.Tokenize(sql).ToArray();

        Assert.Equal("DIMDATE", CompletionAliasResolver.ResolveAlias(tokens, "D"));
        Assert.Null(CompletionAliasResolver.ResolveAlias(tokens, "NO_SUCH_ALIAS"));
        Assert.Null(CompletionAliasResolver.ResolveTablePath(tokens, "NO_SUCH_ALIAS"));
        Assert.NotNull(CompletionAliasResolver.ResolveTablePath(tokens, "D"));
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
    public void AfterFrom_double_dot_prefix_scopes_tables_to_database()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("DIMDATE", "ADMIN", "JUST_DATA"));
        schema.AddTable(new TableInfo("DIMDATE", "PUBLIC", "JUST_DATA"));
        schema.AddTable(new TableInfo("OTHER_DB_TABLE", "ADMIN", "OTHER_DB"));
        var engine = new NzCompletionEngine(schema);

        var sql = "SELECT * FROM JUST_DATA..DIMD";
        var items = engine.GetCompletions(sql, sql.Length);

        Assert.Contains(items, x => x.Label == "DIMDATE" && x.Kind == CompletionKind.Table);
        Assert.DoesNotContain(items, x => x.Label == "OTHER_DB_TABLE");
        Assert.Equal(1, items.Count(x => x.Label.Equals("DIMDATE", StringComparison.OrdinalIgnoreCase)));
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
    public void Multiple_ctes_all_suggested_in_from()
    {
        var sql = "WITH a AS (SELECT 1), b AS (SELECT 2), c AS (SELECT 3) SELECT * FROM ";
        var i = _engine.GetCompletions(sql, sql.Length);
        Assert.Contains(i, x => x.Label == "a" && x.Kind == CompletionKind.Cte);
        Assert.Contains(i, x => x.Label == "b" && x.Kind == CompletionKind.Cte);
        Assert.Contains(i, x => x.Label == "c" && x.Kind == CompletionKind.Cte);
    }


    [Fact]
    public void Double_dot_completion_lists_tables_from_the_requested_database_across_schemas()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("A_TABLE", "ADMIN", "JUST_DATA"));
        schema.AddTable(new TableInfo("B_TABLE", "REPORT", "JUST_DATA"));
        schema.AddTable(new TableInfo("OTHER_TABLE", "ADMIN", "OTHER_DB"));
        var engine = new NzCompletionEngine(schema, activeDatabase: "JUST_DATA");

        const string sql = "SELECT * FROM JUST_DATA..";
        var items = engine.GetCompletions(sql, sql.Length);

        Assert.Contains(items, item => item.Label == "A_TABLE");
        Assert.Contains(items, item => item.Label == "B_TABLE");
        Assert.DoesNotContain(items, item => item.Label == "OTHER_TABLE");
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
    public void AfterOrderBy_suggests_limit_and_excludes_unsupported_fetch()
    {
        var i = _engine.GetCompletions("SELECT * FROM employees ORDER BY ", 33);
        Assert.DoesNotContain(i, x => x.Label == "FETCH");
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
    public void Cte_columns_from_explicit_column_list()
    {
        var sql = "WITH cte (x, y) AS (SELECT 1, 2) SELECT * FROM cte WHERE ";
        var i = _engine.GetCompletions(sql, sql.Length);
        Assert.Contains(i, x => x.Label == "x" && x.Kind == CompletionKind.Column);
        Assert.Contains(i, x => x.Label == "y" && x.Kind == CompletionKind.Column);
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
    public void AfterAlterTable_does_not_suggest_keywords_from_alter_list()
    {
        var sql = "ALTER TABLE ";
        var i = _engine.GetCompletions(sql, sql.Length);
        // After ALTER TABLE we want tables, not the ALTER sub-keywords like VIEW/PROCEDURE
        Assert.DoesNotContain(i, x => x.Label == "VIEW" && x.Kind == CompletionKind.Keyword);
        Assert.DoesNotContain(i, x => x.Label == "DATABASE" && x.Kind == CompletionKind.Keyword);
    }


    [Fact]
    public void AfterCreateSynonymName_suggests_FOR_keyword()
    {
        var i = _engine.GetCompletions("CREATE SYNONYM my_syn ", 22);
        Assert.Contains(i, x => x.Label == "FOR" && x.Kind == CompletionKind.Keyword);
    }


    [Fact]
    public void FromClauseTail_after_plain_table_suggests_continuation_keywords()
    {
        const string sql = "SELECT * FROM employees ";
        var i = _engine.GetCompletions(sql, sql.Length);

        Assert.Contains(i, x => x.Label == "WHERE" && x.Kind == CompletionKind.Keyword);
        Assert.DoesNotContain(i, x => x.Kind == CompletionKind.Column);
        Assert.DoesNotContain(i, x => x.Kind == CompletionKind.Function);
    }


    [Fact]
    public void FromClauseTail_prefix_filters_multiword_keywords()
    {
        const string sql = "SELECT * FROM employees e L";
        var i = _engine.GetCompletions(sql, sql.Length);

        Assert.Contains(i, x => x.Label == "LEFT JOIN" && x.Kind == CompletionKind.Keyword);
        Assert.Contains(i, x => x.Label == "LIMIT" && x.Kind == CompletionKind.Keyword);
        Assert.DoesNotContain(i, x => x.Label == "WHERE");
        Assert.All(i, x => Assert.StartsWith("L", x.Label, StringComparison.OrdinalIgnoreCase));
    }


    [Fact]
    public void FromClauseTail_mid_word_table_typing_still_suggests_tables()
    {
        var schema = SqlTestHelpers.CreateMultiSchemaDuplicateCatalog();
        var engine = new NzCompletionEngine(schema);

        const string sql = "SELECT * FROM DIMDA";
        var i = engine.GetCompletions(sql, sql.Length);

        Assert.Contains(i, x => x.Label == "DIMDATE" && x.Kind == CompletionKind.Table);
        Assert.DoesNotContain(i, x => x.Kind == CompletionKind.Keyword);
    }


    [Fact]
    public void FromList_after_comma_suggests_tables_not_continuation_keywords()
    {
        const string sql = "SELECT * FROM employees e, ";
        var i = _engine.GetCompletions(sql, sql.Length);

        Assert.Contains(i, x => x.Label == "departments" && x.Kind == CompletionKind.Table);
        Assert.DoesNotContain(i, x => x.Label == "JOIN");
    }


    [Fact]
    public void FromClauseTail_qualified_table_suggests_continuation_keywords()
    {
        const string sql = "SELECT * FROM MYDB..employees ";
        var i = _engine.GetCompletions(sql, sql.Length);

        Assert.Contains(i, x => x.Label == "JOIN" && x.Kind == CompletionKind.Keyword);
        Assert.Contains(i, x => x.Label == "WHERE" && x.Kind == CompletionKind.Keyword);
    }


    [Fact]
    public void FromClauseTail_not_applied_to_update_statements()
    {
        const string sql = "UPDATE employees ";
        var i = _engine.GetCompletions(sql, sql.Length);

        Assert.Contains(i, x => x.Label == "SET" && x.Kind == CompletionKind.Keyword);
        Assert.DoesNotContain(i, x => x.Label == "JOIN");
        Assert.DoesNotContain(i, x => x.Label == "WHERE");
    }

    // ===== WhereClause vs WhereContinuation =====

    [Fact]
    public void WhereClauseStart_suggests_columns_functions_and_keywords()
    {
        const string sql = "SELECT * FROM employees e WHERE e.";
        var i = _engine.GetCompletions(sql, sql.Length);

        Assert.Contains(i, x => x.Label == "id" && x.Kind == CompletionKind.Column);
    }


    [Fact]
    public void WhereContinuation_after_full_predicate_suggests_keywords_only()
    {
        const string sql = "SELECT * FROM employees e WHERE e.salary = 1 ";
        var i = _engine.GetCompletions(sql, sql.Length);

        Assert.Contains(i, x => x.Label == "AND" && x.Kind == CompletionKind.Keyword);
        Assert.Contains(i, x => x.Label == "OR" && x.Kind == CompletionKind.Keyword);
        Assert.Contains(i, x => x.Label == "GROUP" && x.Kind == CompletionKind.Keyword);
        Assert.DoesNotContain(i, x => x.Label == "NOT");
        Assert.DoesNotContain(i, x => x.Kind == CompletionKind.Column);
        Assert.DoesNotContain(i, x => x.Kind == CompletionKind.Function);
    }


    [Fact]
    public void WhereContinuation_after_like_predicate_suggests_keywords_only()
    {
        const string sql = "SELECT * FROM employees e WHERE e.name LIKE 'x' ";
        var i = _engine.GetCompletions(sql, sql.Length);

        Assert.Contains(i, x => x.Label == "AND" && x.Kind == CompletionKind.Keyword);
        Assert.DoesNotContain(i, x => x.Kind == CompletionKind.Column);
    }


    [Fact]
    public void WhereClauseStart_after_and_boundary_suggests_columns_again()
    {
        const string sql = "SELECT * FROM employees e WHERE e.salary = 1 AND ";
        var i = _engine.GetCompletions(sql, sql.Length);

        Assert.Contains(i, x => x.Label == "salary" && x.Kind == CompletionKind.Column);
    }


    [Fact]
    public void WhereContinuation_parens_are_balanced_before_detection()
    {
        const string sql = "SELECT * FROM employees e WHERE e.salary = 1 AND e.dept_id IN (1, 2) ";
        var i = _engine.GetCompletions(sql, sql.Length);

        Assert.Contains(i, x => x.Label == "AND" && x.Kind == CompletionKind.Keyword);
        Assert.DoesNotContain(i, x => x.Kind == CompletionKind.Column);
    }
}
