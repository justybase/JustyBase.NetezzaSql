using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Completion;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed partial class NzCompletionEngineTests
{
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
    public void AfterJoin_does_not_suggest_functions()
    {
        var i = _engine.GetCompletions("SELECT * FROM employees e JOIN departments d ON e.", 50);
        Assert.DoesNotContain(i, x => x.Kind == CompletionKind.Function);
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
    public void AfterDelete_does_not_suggest_functions()
    {
        var i = _engine.GetCompletions("DELETE ", 7);
        Assert.DoesNotContain(i, x => x.Kind == CompletionKind.Function);
    }


    [Fact]
    public void Partial_COU_filters_to_COUNT()
    {
        var i = _engine.GetCompletions("SELECT COU", 10);
        Assert.Contains(i, x => x.Label == "COUNT");
        Assert.All(i, x => Assert.StartsWith("COU", x.Label, StringComparison.OrdinalIgnoreCase));
    }


    [Fact]
    public void Cursor_beyond_end_clamps()
    {
        var i = _engine.GetCompletions("SELECT", 100);
        Assert.NotEmpty(i);
    }


    [Fact]
    public void AfterInsertInto_does_not_suggest_functions()
    {
        var i = _engine.GetCompletions("INSERT INTO ", 12);
        Assert.DoesNotContain(i, x => x.Kind == CompletionKind.Function);
    }
}
