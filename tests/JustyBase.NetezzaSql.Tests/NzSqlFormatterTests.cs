using JustyBase.NetezzaSqlParser.Formatter;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Parser;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed class NzSqlFormatterTests
{
    private static string ParseAndFormat(string sql)
    {
        var tokens = NzLexer.Tokenize(sql).ToArray();
        var parser = new NzSqlParser(tokens);
        var stmt = parser.ParseSelect();
        if (stmt is null) return "<PARSE FAILED>";
        return NzSqlFormatter.Format(stmt);
    }

    private static string ParseAndFormatStatement(string sql)
    {
        var tokens = NzLexer.Tokenize(sql).ToArray();
        var parser = new NzSqlParser(tokens);
        var stmt = parser.Parse();
        if (stmt is null) return "<PARSE FAILED>";
        return NzSqlFormatter.Format(stmt);
    }

    [Fact]
    public void Format_SimpleSelect_RoundTrips()
    {
        var result = ParseAndFormat("SELECT 1");
        Assert.Equal("SELECT 1", result);
    }

    [Fact]
    public void Format_SelectFrom_RoundTrips()
    {
        var result = ParseAndFormat("SELECT col1 FROM employees");
        Assert.Equal("SELECT col1\r\nFROM employees", result);
    }

    [Fact]
    public void Format_SelectWhere_RoundTrips()
    {
        var result = ParseAndFormat("SELECT * FROM t WHERE x > 10");
        Assert.Contains("WHERE x > 10", result);
    }

    [Fact]
    public void Format_SelectOrderBy_RoundTrips()
    {
        var result = ParseAndFormat("SELECT * FROM t ORDER BY col1 DESC");
        Assert.Contains("ORDER BY col1 DESC", result);
    }

    [Fact]
    public void Format_SelectLimit_RoundTrips()
    {
        var result = ParseAndFormat("SELECT * FROM t LIMIT 10");
        Assert.Contains("LIMIT 10", result);
    }

    [Fact]
    public void Format_Join_RoundTrips()
    {
        var result = ParseAndFormat("SELECT a.x FROM a INNER JOIN b ON a.id = b.id");
        Assert.Contains("INNER JOIN", result);
        Assert.Contains("ON a.id = b.id", result);
    }

    [Fact]
    public void Format_CaseExpression()
    {
        var result = ParseAndFormat("SELECT CASE WHEN x > 10 THEN 'big' ELSE 'small' END FROM t");
        Assert.Contains("CASE WHEN", result);
        Assert.Contains("END", result);
    }

    [Fact]
    public void Format_CastExpression()
    {
        var result = ParseAndFormat("SELECT CAST(col AS INT) FROM t");
        Assert.Contains("CAST(col AS INT)", result);
    }

    [Fact]
    public void Format_GroupBy()
    {
        var result = ParseAndFormat("SELECT dept, COUNT(*) FROM t GROUP BY dept");
        Assert.Contains("GROUP BY dept", result);
    }

    [Fact]
    public void Format_StringLiteral()
    {
        var result = ParseAndFormat("SELECT * FROM t WHERE name = 'John'");
        Assert.Contains("'John'", result);
    }

    [Fact]
    public void Format_Aliases()
    {
        var result = ParseAndFormat("SELECT col1 AS c1, col2 AS c2 FROM t AS tbl");
        Assert.Contains("AS c1", result);
        Assert.Contains("AS tbl", result);
    }

    [Fact]
    public void Format_SelectWithCte()
    {
        var result = ParseAndFormat("WITH cte AS (SELECT 1) SELECT * FROM cte");

        Assert.Contains("WITH cte AS (", result);
        Assert.Contains("SELECT 1", result);
        Assert.Contains("FROM cte", result);
    }

    [Fact]
    public void Format_SelectUnionAll()
    {
        var result = ParseAndFormat("SELECT 1 UNION ALL SELECT 2");

        Assert.Contains("UNION ALL", result);
        Assert.Contains("SELECT 2", result);
    }

    [Fact]
    public void Format_InsertValues()
    {
        var result = ParseAndFormatStatement("INSERT INTO employees (id, name) VALUES (1, 'John')");

        Assert.Equal("INSERT INTO employees (id, name) VALUES (1, 'John')", result);
    }

    [Fact]
    public void Format_UpdateStatement()
    {
        var result = ParseAndFormatStatement("UPDATE employees e SET name = 'Jane' FROM departments d WHERE e.dept_id = d.id");

        Assert.Contains("UPDATE employees e", result);
        Assert.Contains("SET name = 'Jane'", result);
        Assert.Contains("FROM departments AS d", result);
        Assert.Contains("WHERE e.dept_id = d.id", result);
    }

    [Fact]
    public void Format_DeleteStatement()
    {
        var result = ParseAndFormatStatement("DELETE FROM employees e WHERE e.id = 1");

        Assert.Contains("DELETE FROM employees e", result);
        Assert.Contains("WHERE e.id = 1", result);
    }

    [Fact]
    public void Format_CreateTableStatement()
    {
        var result = ParseAndFormatStatement("CREATE TABLE employees (id INT NOT NULL, name VARCHAR(50))");

        Assert.Contains("CREATE TABLE employees", result);
        Assert.Contains("id INT NOT NULL", result);
        Assert.Contains("name VARCHAR(50)", result);
    }

    [Fact]
    public void Format_CreateViewStatement()
    {
        var result = ParseAndFormatStatement("CREATE OR REPLACE VIEW v_emp AS SELECT 1");

        Assert.Contains("CREATE OR REPLACE VIEW v_emp AS", result);
        Assert.Contains("SELECT 1", result);
    }

    [Fact]
    public void Format_DropStatement()
    {
        var result = ParseAndFormatStatement("DROP TABLE a, b");

        Assert.Equal("DROP TABLE a, b", result);
    }

    [Fact]
    public void Format_TruncateStatement()
    {
        var result = ParseAndFormatStatement("TRUNCATE TABLE employees");

        Assert.Equal("TRUNCATE TABLE employees", result);
    }

    [Fact]
    public void Format_GroomStatement()
    {
        var result = ParseAndFormatStatement("GROOM TABLE employees RECORDS");

        Assert.Contains("GROOM TABLE employees", result);
        Assert.Contains("RECORDS", result);
    }

    [Fact]
    public void Format_GenerateStatisticsStatement()
    {
        var result = ParseAndFormatStatement("GENERATE EXPRESS STATISTICS ON employees (dept_id)");

        Assert.Contains("GENERATE EXPRESS STATISTICS ON employees (dept_id)", result);
    }

    [Fact]
    public void Format_CommentStatement()
    {
        var result = ParseAndFormatStatement("COMMENT ON TABLE employees IS 'sample'");

        Assert.Equal("COMMENT ON TABLE employees IS 'sample'", result);
    }

    [Fact]
    public void Format_CallStatement()
    {
        var result = ParseAndFormatStatement("CALL my_proc");

        Assert.Equal("CALL my_proc", result);
    }

    [Fact]
    public void Format_ProcedureStatement()
    {
        var sql = """
                  CREATE PROCEDURE demo() RETURNS INT LANGUAGE NZPLSQL AS
                  BEGIN_PROC
                  RETURN 1;
                  END_PROC;
                  """;

        var result = ParseAndFormatStatement(sql);

        Assert.Contains("CREATE PROCEDURE demo () RETURNS INT LANGUAGE NZPLSQL AS", result);
        Assert.Contains("BEGIN_PROC", result);
        Assert.Contains("RETURN 1", result);
        Assert.Contains("END_PROC", result);
    }
}
