using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Completion;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed partial class NzCompletionEngineTests
{
    [Fact]
    public void Alias_dot_prefers_data_type_and_documentation_when_present()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("employees", Columns:
        [
            new ColumnInfo("id", DataType: "INTEGER", Description: "Primary key"),
            new ColumnInfo("name", DataType: "VARCHAR(100)", Description: "Display name")
        ]));
        var engine = new NzCompletionEngine(schema);
        var i = engine.GetCompletions("SELECT e. FROM employees e", 9);

        Assert.Contains(i, x => x.Label == "id" && x.Detail == "INTEGER" && x.Documentation == "Primary key");
        Assert.Contains(i, x => x.Label == "name" && x.Detail == "VARCHAR(100)" && x.Documentation == "Display name");
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
    public void Cte_with_function_and_complex_expr_in_select_skips_noname_items()
    {
        // Complex expressions without alias are skipped; only named columns inferred
        var sql = "WITH cte AS (SELECT count(*) as cnt, id + 1, name FROM employees) SELECT * FROM cte WHERE ";
        var i = _engine.GetCompletions(sql, sql.Length);
        Assert.Contains(i, x => x.Label == "cnt" && x.Kind == CompletionKind.Column);
        Assert.Contains(i, x => x.Label == "name" && x.Kind == CompletionKind.Column);
        // "id + 1" has no alias — skip it (no column name to infer)
    }
}
