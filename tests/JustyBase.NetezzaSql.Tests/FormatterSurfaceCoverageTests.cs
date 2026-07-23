using JustyBase.NetezzaSqlParser.Formatter;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Parser;

namespace JustyBase.NetezzaSql.Tests;

public sealed class FormatterSurfaceCoverageTests
{
    [Theory]
    [InlineData("SELECT DISTINCT a, SUM(b) AS total FROM t WHERE a BETWEEN 1 AND 9 GROUP BY a HAVING SUM(b) > 2 ORDER BY a ASC LIMIT 4 OFFSET 1")]
    [InlineData("SELECT a FROM t LEFT JOIN u ON t.id = u.id WHERE u.id IS NOT NULL")]
    [InlineData("SELECT a FROM t RIGHT JOIN u ON t.id = u.id")]
    [InlineData("SELECT a FROM t FULL OUTER JOIN u ON t.id = u.id")]
    [InlineData("SELECT a FROM t CROSS JOIN u")]
    [InlineData("SELECT CASE a WHEN 1 THEN 'x' WHEN 2 THEN 'y' ELSE 'z' END FROM t")]
    [InlineData("SELECT NOT a, -b, a IN (1, 2), a LIKE 'x%' FROM t")]
    [InlineData("SELECT a, ROW_NUMBER() OVER (PARTITION BY b ORDER BY a DESC) FROM t")]
    [InlineData("SELECT a FROM (SELECT a FROM t) q WHERE EXISTS (SELECT 1 FROM u)")]
    [InlineData("WITH a AS (SELECT 1), b AS (SELECT 2) SELECT * FROM a UNION SELECT * FROM b")]
    public void Formatter_HandlesAdvancedSelectShapes(string sql)
    {
        var parser = new NzSqlParser(NzLexer.Tokenize(sql).ToArray());
        var statement = parser.Parse();

        Assert.NotNull(statement);
        Assert.Empty(parser.Errors);
        Assert.NotEmpty(NzSqlFormatter.Format(statement));
    }

    [Theory]
    [InlineData("ALTER TABLE t ADD COLUMN a INT")]
    [InlineData("ALTER TABLE t DROP COLUMN a")]
    [InlineData("CREATE EXTERNAL TABLE ext (id INT) USING (DATAOBJECT('file') DELIMITER ',')")]
    [InlineData("CREATE SEQUENCE seq START WITH 1 INCREMENT BY 1")]
    [InlineData("DROP VIEW IF EXISTS v")]
    [InlineData("SET CATALOG DB")]
    public void Formatter_HandlesCommandTailStatements(string sql)
    {
        var parser = new NzSqlParser(NzLexer.Tokenize(sql).ToArray());
        var statement = parser.Parse();

        Assert.NotNull(statement);
        Assert.NotEmpty(NzSqlFormatter.Format(statement));
    }
}
