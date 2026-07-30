using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.NetezzaSqlParser.Completion;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed class SqlAutocompleteWindowTests
{
    [Fact]
    public void Slice_uses_trailing_statement_after_huge_prefix()
    {
        string prefix = new string('a', 200_000) + ";";
        const string tail = "SELECT * FROM JUST_DATA..DIMDATE D WHERE D.";
        string sql = prefix + tail;

        (string window, int windowCursor) = SqlAutocompleteWindow.SliceForEngine(
            sql, sql.Length, lineCount: 9_188, forcedAutocomplete: false);

        Assert.Equal(tail, window);
        Assert.Equal(tail.Length, windowCursor);
    }

    [Fact]
    public void Slice_uses_lookback_when_no_semicolon()
    {
        string sql = new string('a', 200_000) + "SELECT * FROM t";
        int cursor = sql.Length;

        (string window, int windowCursor) = SqlAutocompleteWindow.SliceForEngine(
            sql, cursor, lineCount: 9_188, forcedAutocomplete: true);

        Assert.True(window.Length < sql.Length);
        Assert.Equal(window.Length, windowCursor);
        Assert.EndsWith("SELECT * FROM t", window);
    }

    [Fact]
    public void Passive_skips_oversized_trailing_statement()
    {
        string prefix = new string('a', 200_000) + ";";
        string oversizedTail = new string('x', SqlPerformancePolicy.PassiveAutocompleteStatementCharLimit + 50);
        string sql = prefix + oversizedTail;

        Assert.False(SqlAutocompleteWindow.ShouldRunEngine(
            sql, sql.Length, lineCount: 9_188, forcedAutocomplete: false));
        Assert.True(SqlAutocompleteWindow.ShouldRunEngine(
            sql, sql.Length, lineCount: 9_188, forcedAutocomplete: true));
    }

    [Fact]
    public void Passive_allows_short_trailing_statement_on_large_doc()
    {
        string prefix = new string('a', 200_000) + ";";
        const string tail = "SELECT * FROM JUST_DATA..DIMDATE D WHERE D.";
        string sql = prefix + tail;

        Assert.True(SqlAutocompleteWindow.ShouldRunEngine(
            sql, sql.Length, lineCount: 9_188, forcedAutocomplete: false));
    }

    [Fact]
    public void Small_document_returns_full_sql()
    {
        const string sql = "SELECT 1 FROM t;";
        (string window, int cursor) = SqlAutocompleteWindow.SliceForEngine(
            sql, sql.Length, lineCount: 1, forcedAutocomplete: true);

        Assert.Equal(sql, window);
        Assert.Equal(sql.Length, cursor);
    }

    [Fact]
    public void Engine_on_sliced_tail_suggests_alias_columns()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("DIMDATE", Schema: "ADMIN", Database: "JUST_DATA", Columns:
        [
            new ColumnInfo("DATEKEY"),
            new ColumnInfo("CALENDARYEAR")
        ]));
        var engine = new NzCompletionEngine(schema);

        string prefix = new string('a', 200_000) + ";";
        const string tail = "SELECT * FROM JUST_DATA..DIMDATE D WHERE D.";
        string sql = prefix + tail;

        var (engineSql, engineCursor) = SqlAutocompleteWindow.SliceForEngine(
            sql, sql.Length, lineCount: 9_188, forcedAutocomplete: true);
        var items = engine.GetCompletions(engineSql, engineCursor).ToArray();

        Assert.Equal(tail, engineSql);
        Assert.Contains(items, x => x.Label == "DATEKEY" && x.Kind == CompletionKind.Column);
        Assert.Contains(items, x => x.Label == "CALENDARYEAR" && x.Kind == CompletionKind.Column);
    }

    [Fact]
    public void GetTopLevelStatementBounds_ignores_semicolon_inside_string()
    {
        const string sql = "SELECT 'a;b' FROM t WHERE x.";
        (int start, int end) = SqlStatementBounds.GetTopLevelStatementBounds(sql.Length - 1, sql);

        Assert.Equal(0, start);
        Assert.Equal(sql.Length, end);
    }
}
