using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Parser;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed class NzSqlParserTests
{
    private static SelectStatement? Parse(string sql)
    {
        var tokens = NzLexer.Tokenize(sql).ToArray();
        return new NzSqlParser(tokens).ParseSelect();
    }

    [Fact]
    public void ParseSelect_SimpleLiteral()
    {
        var result = Parse("SELECT 1");
        Assert.NotNull(result);
        Assert.Single(result.SelectList);
        Assert.Null(result.From);
        Assert.Null(result.Where);
    }

    [Fact]
    public void ParseSelect_ColumnNames()
    {
        var result = Parse("SELECT col1, col2, col3");
        Assert.NotNull(result);
        Assert.Equal(3, result.SelectList.Count);
        Assert.Null(result.From);
    }

    [Fact]
    public void ParseSelect_FromTable()
    {
        var result = Parse("SELECT * FROM employees");
        Assert.NotNull(result);
        Assert.Single(result.SelectList);
        Assert.NotNull(result.From);
        Assert.Single(result.From);
    }

    [Fact]
    public void ParseSelect_WhereClause()
    {
        var result = Parse("SELECT * FROM t WHERE x > 10");
        Assert.NotNull(result);
        Assert.NotNull(result.Where);
    }

    [Fact]
    public void ParseSelect_WhereWithAnd()
    {
        var result = Parse("SELECT * FROM t WHERE x > 10 AND y < 20");
        Assert.NotNull(result);
        Assert.NotNull(result.Where);
    }

    [Fact]
    public void ParseSelect_GroupBy()
    {
        var result = Parse("SELECT dept, COUNT(*) FROM emp GROUP BY dept");
        Assert.NotNull(result);
        Assert.NotNull(result.GroupBy);
        Assert.Single(result.GroupBy);
    }

    [Fact]
    public void ParseSelect_OrderBy()
    {
        var result = Parse("SELECT * FROM t ORDER BY col1");
        Assert.NotNull(result);
        Assert.NotNull(result.OrderBy);
        Assert.Single(result.OrderBy);
    }

    [Fact]
    public void ParseSelect_OrderByDesc()
    {
        var result = Parse("SELECT * FROM t ORDER BY col1 DESC");
        Assert.NotNull(result);
        Assert.NotNull(result.OrderBy);
        Assert.True(result.OrderBy[0].Descending);
    }

    [Fact]
    public void ParseSelect_Limit()
    {
        var result = Parse("SELECT * FROM t LIMIT 5");
        Assert.NotNull(result);
        Assert.NotNull(result.Limit);
        Assert.Equal(5, result.Limit.Limit);
    }

    [Fact]
    public void ParseSelect_InnerJoin()
    {
        var result = Parse("SELECT a.x FROM a INNER JOIN b ON a.id = b.id");
        Assert.NotNull(result);
        Assert.NotNull(result.From);
        var tableRef = result.From[0];
        Assert.NotNull(tableRef.Joins);
        Assert.Single(tableRef.Joins);
    }

    [Fact]
    public void ParseSelect_CrossJoin()
    {
        var result = Parse("SELECT * FROM a CROSS JOIN b");
        Assert.NotNull(result);
        Assert.NotNull(result.From);
        var tableRef = result.From[0];
        Assert.NotNull(tableRef.Joins);
    }

    [Fact]
    public void ParseSelect_LeftJoin()
    {
        var result = Parse("SELECT * FROM a LEFT JOIN b ON a.id = b.id");
        Assert.NotNull(result);
        Assert.NotNull(result.From);
    }

    [Fact]
    public void ParseSelect_Alias()
    {
        var result = Parse("SELECT col1 AS c1, col2 c2 FROM t AS tbl");
        Assert.NotNull(result);
        Assert.Equal(2, result.SelectList.Count);
        Assert.Equal("c1", result.SelectList[0].Alias);
        Assert.Equal("c2", result.SelectList[1].Alias);
    }

    [Fact]
    public void ParseSelect_CaseExpression()
    {
        var result = Parse("SELECT CASE WHEN x > 10 THEN 'big' ELSE 'small' END FROM t");
        Assert.NotNull(result);
        Assert.NotNull(result.From);
    }

    [Fact]
    public void ParseSelect_CastExpression()
    {
        var result = Parse("SELECT CAST(col AS INT) FROM t");
        Assert.NotNull(result);
        Assert.NotNull(result.From);
    }

    [Fact]
    public void ParseSelect_MultipleTables()
    {
        var result = Parse("SELECT * FROM t1, t2");
        Assert.NotNull(result);
        Assert.NotNull(result.From);
        Assert.Equal(2, result.From.Count);
    }

    [Fact]
    public void ParseSelect_FunctionCall()
    {
        var result = Parse("SELECT COUNT(*), SUM(x) FROM t");
        Assert.NotNull(result);
        Assert.Equal(2, result.SelectList.Count);
    }

    [Fact]
    public void ParseSelect_NullCheck()
    {
        var result = Parse("SELECT * FROM t WHERE col IS NULL");
        Assert.NotNull(result);
        Assert.NotNull(result.Where);
    }

    [Fact]
    public void ParseSelect_LikeExpression()
    {
        var result = Parse("SELECT * FROM t WHERE col LIKE '%test%'");
        Assert.NotNull(result);
        Assert.NotNull(result.Where);
    }

    [Fact]
    public void ParseSelect_StringLiteral()
    {
        var result = Parse("SELECT * FROM t WHERE name = 'John'");
        Assert.NotNull(result);
        Assert.NotNull(result.Where);
    }

    [Fact]
    public void ParseSelect_TableDotColumn()
    {
        var result = Parse("SELECT t.col1, t.col2 FROM t");
        Assert.NotNull(result);
        Assert.Equal(2, result.SelectList.Count);
    }

    [Fact]
    public void ParseSelect_Concat()
    {
        var result = Parse("SELECT a || b FROM t");
        Assert.NotNull(result);
        Assert.Single(result.SelectList);
    }

    [Fact]
    public void ParseSelect_MultipleOrderBy()
    {
        var result = Parse("SELECT * FROM t ORDER BY col1, col2 DESC");
        Assert.NotNull(result);
        Assert.NotNull(result.OrderBy);
        Assert.Equal(2, result.OrderBy.Count);
    }

    [Fact]
    public void ParseSelect_NotNull()
    {
        var result = Parse("SELECT * FROM t WHERE col IS NOT NULL");
        Assert.NotNull(result);
        Assert.NotNull(result.Where);
    }

    [Fact]
    public void ParseSelect_Arithmetic()
    {
        var result = Parse("SELECT x + y * 2 FROM t");
        Assert.NotNull(result);
        Assert.Single(result.SelectList);
    }
}
