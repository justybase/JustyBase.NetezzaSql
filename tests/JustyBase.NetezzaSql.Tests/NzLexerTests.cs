using JustyBase.NetezzaSqlParser.Lexer;
using Superpower.Model;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed class NzLexerTests
{
    private static Token<NzToken>[] T(string sql) => NzLexer.Tokenize(sql).ToArray();

    [Fact]
    public void Tokenize_SimpleSelect()
    {
        var t = T("SELECT 1");
        Assert.Equal(2, t.Length);
        Assert.Equal(NzToken.Select, t[0].Kind);
        Assert.Equal(NzToken.NumberLiteral, t[1].Kind);
        Assert.Equal("1", t[1].ToStringValue());
    }

    [Fact]
    public void Tokenize_SelectStarFromTable()
    {
        var t = T("SELECT * FROM employees");
        Assert.Equal(4, t.Length);
        Assert.Equal(NzToken.Select, t[0].Kind);
        Assert.Equal(NzToken.Multiply, t[1].Kind);
        Assert.Equal(NzToken.From, t[2].Kind);
        Assert.Equal(NzToken.Identifier, t[3].Kind);
        Assert.Equal("employees", t[3].ToStringValue());
    }

    [Fact]
    public void Tokenize_SelectWithWhere()
    {
        var t = T("SELECT col1 FROM t1 WHERE col1 > 10");
        Assert.Equal(NzToken.Identifier, t[1].Kind);
        Assert.Equal("col1", t[1].ToStringValue());
        Assert.Equal(NzToken.From, t[2].Kind);
        Assert.Equal(NzToken.Where, t[4].Kind);
        Assert.Equal(NzToken.GreaterThan, t[6].Kind);
        Assert.Equal(NzToken.NumberLiteral, t[7].Kind);
    }

    [Fact]
    public void Tokenize_KeywordsCaseInsensitive()
    {
        var t = T("select * from t");
        Assert.Equal(NzToken.Select, t[0].Kind);
        Assert.Equal(NzToken.From, t[2].Kind);
    }

    [Fact]
    public void Tokenize_Joins()
    {
        var t = T("SELECT a.x FROM a INNER JOIN b ON a.id = b.id LEFT JOIN c ON a.id = c.id");
        Assert.Equal(NzToken.Dot, t[2].Kind);
        Assert.Equal(NzToken.Inner, t[6].Kind);
        Assert.Equal(NzToken.Join, t[7].Kind);
        Assert.Equal(NzToken.On, t[9].Kind);
        Assert.Equal(NzToken.Left, t[17].Kind);
        Assert.Equal(NzToken.Join, t[18].Kind);
    }

    [Fact]
    public void Tokenize_CrossJoin()
    {
        var t = T("SELECT * FROM a CROSS JOIN b");
        Assert.Equal(NzToken.Cross, t[4].Kind);
        Assert.Equal(NzToken.Join, t[5].Kind);
    }

    [Fact]
    public void Tokenize_OrderByWithNulls()
    {
        var t = T("SELECT * FROM t ORDER BY col1 ASC NULLS FIRST");
        Assert.Equal(NzToken.OrderBy, t[4].Kind);
        Assert.Equal(NzToken.Asc, t[6].Kind);
        Assert.Equal(NzToken.Nulls, t[7].Kind);
        Assert.Equal(NzToken.First, t[8].Kind);
    }

    [Fact]
    public void Tokenize_GroupBy()
    {
        var t = T("SELECT col1, COUNT(*) FROM t GROUP BY col1");
        Assert.Equal(NzToken.GroupBy, t[9].Kind);
    }

    [Fact]
    public void Tokenize_StringLiteral()
    {
        var t = T("SELECT 'hello world'");
        Assert.Equal(NzToken.StringLiteral, t[1].Kind);
        Assert.Equal("'hello world'", t[1].ToStringValue());
    }

    [Fact]
    public void Tokenize_StringLiteralWithEscapedQuote()
    {
        var t = T("SELECT 'it''s working'");
        Assert.Equal(NzToken.StringLiteral, t[1].Kind);
        Assert.Equal("'it''s working'", t[1].ToStringValue());
    }

    [Fact]
    public void Tokenize_QuotedIdentifier()
    {
        var t = T("SELECT \"My Column\" FROM t");
        Assert.Equal(NzToken.QuotedIdentifier, t[1].Kind);
        Assert.Equal("\"My Column\"", t[1].ToStringValue());
    }

    [Fact]
    public void Tokenize_UnderscoreIdentifier()
    {
        var t = T("SELECT inner_col FROM t");
        Assert.Equal(NzToken.Identifier, t[1].Kind);
        Assert.Equal("inner_col", t[1].ToStringValue());
    }

    [Fact]
    public void Tokenize_NumberLiteral()
    {
        var t = T("SELECT 42, 3.14, 1e10");
        Assert.Equal(NzToken.NumberLiteral, t[1].Kind);
        Assert.Equal("42", t[1].ToStringValue());
        Assert.Equal(NzToken.NumberLiteral, t[3].Kind);
        Assert.Equal("3.14", t[3].ToStringValue());
        Assert.Equal(NzToken.NumberLiteral, t[5].Kind);
        Assert.Equal("1e10", t[5].ToStringValue());
    }

    [Fact]
    public void Tokenize_Comments_AreSkipped()
    {
        var t = T("-- this is a comment\nSELECT 1");
        Assert.Equal(2, t.Length);
        Assert.Equal(NzToken.Select, t[0].Kind);
        Assert.Equal(NzToken.NumberLiteral, t[1].Kind);
    }

    [Fact]
    public void Tokenize_BlockComment_IsSkipped()
    {
        var t = T("SELECT /* inline */ 1");
        Assert.Equal(2, t.Length);
        Assert.Equal(NzToken.Select, t[0].Kind);
        Assert.Equal(NzToken.NumberLiteral, t[1].Kind);
    }

    [Fact]
    public void Tokenize_MultilineBlockComment_IsSkipped()
    {
        var t = T("SELECT /* multi\nline\ncomment */ 1");
        Assert.Equal(2, t.Length);
        Assert.Equal(NzToken.Select, t[0].Kind);
        Assert.Equal(NzToken.NumberLiteral, t[1].Kind);
    }

    [Fact]
    public void Tokenize_CreateTable()
    {
        var t = T("CREATE TABLE t1 (id INT, name VARCHAR(100))");
        Assert.Equal(NzToken.Create, t[0].Kind);
        Assert.Equal(NzToken.Table, t[1].Kind);
        Assert.Equal("t1", t[2].ToStringValue());
        Assert.Equal(NzToken.LParen, t[3].Kind);
        Assert.Equal(NzToken.Identifier, t[4].Kind); // id
        Assert.Equal(NzToken.Identifier, t[5].Kind); // INT
        Assert.Equal(NzToken.Comma, t[6].Kind);
        Assert.Equal(NzToken.LParen, t[9].Kind);
        Assert.Equal(NzToken.NumberLiteral, t[10].Kind); // 100
    }

    [Fact]
    public void Tokenize_LikeAndIlike()
    {
        var t = T("SELECT * FROM t WHERE col LIKE '%test%' AND col2 ILIKE 'TeSt%'");
        Assert.Equal(NzToken.Like, t[6].Kind);
        Assert.Equal(NzToken.StringLiteral, t[7].Kind);
        Assert.Equal("'%test%'", t[7].ToStringValue());
        Assert.Equal(NzToken.Ilike, t[10].Kind);
        Assert.Equal(NzToken.StringLiteral, t[11].Kind);
        Assert.Equal("'TeSt%'", t[11].ToStringValue());
    }

    [Fact]
    public void Tokenize_Between()
    {
        var t = T("SELECT * FROM t WHERE col BETWEEN 1 AND 10");
        Assert.Equal(NzToken.Between, t[6].Kind);
    }

    [Fact]
    public void Tokenize_In()
    {
        var t = T("SELECT * FROM t WHERE col IN (1, 2, 3)");
        Assert.Equal(NzToken.In, t[6].Kind);
    }

    [Fact]
    public void Tokenize_Cast()
    {
        var t = T("SELECT CAST(col AS INT) FROM t");
        Assert.Equal(NzToken.Cast, t[1].Kind);
    }

    [Fact]
    public void Tokenize_DoubleColon()
    {
        var t = T("SELECT col::INT FROM t");
        Assert.Equal(NzToken.DoubleColon, t[2].Kind);
    }

    [Fact]
    public void Tokenize_Concat()
    {
        var t = T("SELECT a || b FROM t");
        Assert.Equal(NzToken.Concat, t[2].Kind);
    }

    [Fact]
    public void Tokenize_NotNull()
    {
        var t = T("SELECT * FROM t WHERE col IS NOT NULL");
        Assert.Equal(NzToken.Is, t[6].Kind);
        Assert.Equal(NzToken.Not, t[7].Kind);
        Assert.Equal(NzToken.Null, t[8].Kind);
    }

    [Fact]
    public void Tokenize_Assign()
    {
        var t = T("SET x := 10");
        Assert.Equal(NzToken.Assign, t[2].Kind);
    }

    [Fact]
    public void Tokenize_BracedVariable()
    {
        var t = T("SELECT ${myvar}");
        Assert.Equal(NzToken.BracedVariable, t[1].Kind);
        Assert.Equal("${myvar}", t[1].ToStringValue());
    }

    [Fact]
    public void Tokenize_DollarNumber()
    {
        var t = T("SELECT $1, $2");
        Assert.Equal(NzToken.DollarNumber, t[1].Kind);
        Assert.Equal("$1", t[1].ToStringValue());
        Assert.Equal(NzToken.DollarNumber, t[3].Kind);
        Assert.Equal("$2", t[3].ToStringValue());
    }

    [Fact]
    public void Tokenize_MixedCaseKeywords()
    {
        var t = T("SeLeCt * FrOm t WhErE x Is NoT NuLl");
        Assert.Equal(NzToken.Select, t[0].Kind);
        Assert.Equal(NzToken.From, t[2].Kind);
        Assert.Equal(NzToken.Where, t[4].Kind);
        Assert.Equal(NzToken.Is, t[6].Kind);
        Assert.Equal(NzToken.Not, t[7].Kind);
        Assert.Equal(NzToken.Null, t[8].Kind);
    }

    [Fact]
    public void Tokenize_SelectWithAlias()
    {
        var t = T("SELECT col1 AS c1, col2 c2 FROM t AS tbl");
        Assert.Equal(NzToken.As, t[2].Kind);
        Assert.Equal("c1", t[3].ToStringValue());
        Assert.Equal("c2", t[6].ToStringValue());
        Assert.Equal(NzToken.As, t[9].Kind);
    }

    [Fact]
    public void Tokenize_CaseExpression()
    {
        var t = T("SELECT CASE WHEN x > 10 THEN 'big' ELSE 'small' END FROM t");
        Assert.Equal(NzToken.Case, t[1].Kind);
        Assert.Equal(NzToken.When, t[2].Kind);
        Assert.Equal(NzToken.Then, t[6].Kind);
        Assert.Equal(NzToken.Else, t[8].Kind);
        Assert.Equal(NzToken.End, t[10].Kind);
    }

    [Fact]
    public void Tokenize_Procedure()
    {
        var t = T("CREATE OR REPLACE PROCEDURE my_proc() RETURNS INT LANGUAGE NZPLSQL AS BEGIN_PROC BEGIN RETURN 1; END; END_PROC");
        Assert.Equal(NzToken.Procedure, t[3].Kind);
        Assert.Equal(NzToken.Returns, t[7].Kind);
        Assert.Equal(NzToken.Language, t[9].Kind);
        Assert.Equal(NzToken.Nzplsql, t[10].Kind);
        Assert.Equal(NzToken.BeginProc, t[12].Kind);
        Assert.Equal(NzToken.Begin, t[13].Kind);
        Assert.Equal(NzToken.EndProc, t[19].Kind);
    }

    [Fact]
    public void Tokenize_DistributeOn()
    {
        var t = T("CREATE TABLE t1 (id INT) DISTRIBUTE ON (id)");
        Assert.Equal(NzToken.Distribute, t[7].Kind);
    }

    [Fact]
    public void Tokenize_Groom()
    {
        var t = T("GROOM TABLE t1 RECORDS ALL RECLAIM BACKUPSET NONE");
        Assert.Equal(NzToken.Groom, t[0].Kind);
        Assert.Equal(NzToken.Records, t[3].Kind);
        Assert.Equal(NzToken.All, t[4].Kind);
        Assert.Equal(NzToken.Reclaim, t[5].Kind);
        Assert.Equal(NzToken.Backupset, t[6].Kind);
        Assert.Equal(NzToken.None, t[7].Kind);
    }

    [Fact]
    public void Tokenize_GenerateStatistics()
    {
        var t = T("GENERATE EXPRESS STATISTICS ON t1");
        Assert.Equal(NzToken.Generate, t[0].Kind);
        Assert.Equal(NzToken.Express, t[1].Kind);
        Assert.Equal(NzToken.Statistics, t[2].Kind);
    }

    [Fact]
    public void Tokenize_ExternalTable()
    {
        var t = T("CREATE EXTERNAL TABLE ext_tab SAMEAS t1 USING (DATAOBJECT '/tmp/data.txt' DELIMITER '|')");
        Assert.Equal(NzToken.External, t[1].Kind);
    }

    [Fact]
    public void Tokenize_UnionAll()
    {
        var t = T("SELECT 1 UNION ALL SELECT 2");
        Assert.Equal(NzToken.Union, t[2].Kind);
        Assert.Equal(NzToken.All, t[3].Kind);
    }

    [Fact]
    public void Tokenize_WithClause()
    {
        var t = T("WITH cte AS (SELECT 1) SELECT * FROM cte");
        Assert.Equal(NzToken.With, t[0].Kind);
        Assert.Equal(NzToken.As, t[2].Kind);
    }

    [Fact]
    public void Tokenize_PartitionBy()
    {
        var t = T("SELECT ROW_NUMBER() OVER (PARTITION BY dept ORDER BY salary) FROM emp");
        Assert.Equal(NzToken.PartitionBy, t[6].Kind);
    }

    [Fact]
    public void Tokenize_WindowFrame()
    {
        var t = T("SELECT SUM(x) OVER (ORDER BY y ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) FROM t");
        Assert.Equal(NzToken.Rows, t[9].Kind);
        Assert.Equal(NzToken.Between, t[10].Kind);
        Assert.Equal(NzToken.Unbounded, t[11].Kind);
        Assert.Equal(NzToken.Preceding, t[12].Kind);
        Assert.Equal(NzToken.Current, t[14].Kind);
        Assert.Equal(NzToken.Row, t[15].Kind);
    }

    [Fact]
    public void Tokenize_NotEquals_AngleBracket()
    {
        var t = T("SELECT * FROM t WHERE x <> 10");
        Assert.Equal(NzToken.NotEquals, t[6].Kind);
    }

    [Fact]
    public void Tokenize_NotEquals_BangEqual()
    {
        var t = T("SELECT * FROM t WHERE x != 10");
        Assert.Equal(NzToken.NotEquals, t[6].Kind);
    }

    [Fact]
    public void Tokenize_FetchFirst()
    {
        var t = T("SELECT * FROM t ORDER BY x FETCH FIRST 10 ROWS ONLY");
        Assert.Equal(NzToken.Fetch, t[6].Kind);
        Assert.Equal(NzToken.First, t[7].Kind);
        Assert.Equal(NzToken.Only, t[10].Kind);
    }

    [Fact]
    public void Tokenize_Grant()
    {
        var t = T("GRANT SELECT ON t1 TO PUBLIC");
        Assert.Equal(NzToken.Grant, t[0].Kind);
        Assert.Equal(NzToken.Select, t[1].Kind);
        Assert.Equal(NzToken.To, t[4].Kind);
        Assert.Equal(NzToken.Public, t[5].Kind);
    }

    [Fact]
    public void Tokenize_EmptyString()
    {
        var t = T("   \n\t  ");
        Assert.Empty(t);
    }

    [Fact]
    public void Tokenize_Semicolons()
    {
        var t = T("SELECT 1; SELECT 2;");
        Assert.Equal(NzToken.Semicolon, t[2].Kind);
        Assert.Equal(NzToken.Semicolon, t[5].Kind);
    }

    [Fact]
    public void Tokenize_Dot()
    {
        var t = T("SELECT s.t.col FROM s.t");
        Assert.Equal(NzToken.Dot, t[2].Kind);
        Assert.Equal(NzToken.Dot, t[4].Kind);
        Assert.Equal(NzToken.Dot, t[8].Kind);
    }
}
