using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Scripts;

namespace JustyBase.NetezzaSql.Tests;

public sealed class SqlStatementScannerTests
{
    [Fact]
    public void Generic_SplitsAtTopLevelSemicolonsAndKeepsOffsets()
    {
        const string source = "SELECT 1; SELECT 2";
        var scan = SqlStatementScanner.Scan(source, SqlDialect.Netezza);

        Assert.False(scan.HasUnterminatedConstruct);
        Assert.Equal(2, scan.Statements.Count);
        Assert.Equal("SELECT 1", scan.Statements[0].Slice(source).ToString());
        Assert.Equal(" SELECT 2", scan.Statements[1].Slice(source).ToString());
    }

    [Fact]
    public void Generic_DoubledQuoteAndCommentsDoNotSplit()
    {
        const string source = "SELECT 'a;''b' FROM T; -- ; in comment\nSELECT 2 /* ; */ FROM T";
        var scan = SqlStatementScanner.Scan(source, SqlDialect.Netezza);

        Assert.Equal(2, scan.Statements.Count);
        Assert.Equal("SELECT 'a;''b' FROM T", scan.Statements[0].Slice(source).ToString());
    }

    [Fact]
    public void Generic_SemicolonInsideParenthesesDoesNotSplit()
    {
        const string source = "SELECT (SELECT 1; SELECT 2); SELECT 3";
        var scan = SqlStatementScanner.Scan(source, SqlDialect.Netezza);

        Assert.Equal(2, scan.Statements.Count);
    }

    [Fact]
    public void Generic_UnterminatedQuoteIsReported()
    {
        var scan = SqlStatementScanner.Scan("SELECT 'oops", SqlDialect.Netezza);

        Assert.True(scan.HasUnterminatedConstruct);
    }

    [Fact]
    public void MySql_BackticksAndHashCommentsAreIgnored()
    {
        const string source = "SELECT `a;b` FROM t; # ; comment\nSELECT 2";
        var scan = SqlStatementScanner.Scan(source, SqlDialect.MySql);

        Assert.Equal(2, scan.Statements.Count);
    }

    [Fact]
    public void Db2_SplitsStatementsAndKeepsFetchFirstIntact()
    {
        const string source = "SELECT * FROM t FETCH FIRST 10 ROWS ONLY; SELECT 1 FROM SYSIBM.SYSDUMMY1";
        var scan = SqlStatementScanner.Scan(source, SqlDialect.Db2);

        Assert.Equal(2, scan.Statements.Count);
        Assert.False(scan.HasUnterminatedConstruct);
    }

    [Fact]
    public void Db2_ProcedureBodyWithBeginEndStaysOneStatement()
    {
        const string source = "CREATE PROCEDURE p () LANGUAGE SQL BEGIN DECLARE x INT DEFAULT 1; SET x = 2; END; SELECT 1 FROM SYSIBM.SYSDUMMY1";
        var scan = SqlStatementScanner.Scan(source, SqlDialect.Db2);

        Assert.Equal(2, scan.Statements.Count);
        Assert.False(scan.HasUnterminatedConstruct);
        Assert.Contains("SET x = 2", scan.Statements[0].Slice(source).ToString());
    }

    [Fact]
    public void Db2_CompoundEndsDoNotCloseBeginBlock()
    {
        const string source = "CREATE PROCEDURE p () LANGUAGE SQL BEGIN IF x > 0 THEN SET y = 1; END IF; WHILE x < 10 DO SET x = x + 1; END WHILE; END;";
        var scan = SqlStatementScanner.Scan(source, SqlDialect.Db2);

        Assert.Single(scan.Statements);
        Assert.False(scan.HasUnterminatedConstruct);
    }

    [Fact]
    public void Db2_CaseInsideBeginDoesNotSplitTheProcedure()
    {
        const string source = "CREATE PROCEDURE p () LANGUAGE SQL BEGIN SELECT CASE WHEN a = 1 THEN 2 END FROM T; SET x = 1; END;";
        var scan = SqlStatementScanner.Scan(source, SqlDialect.Db2);

        Assert.Single(scan.Statements);
    }

    [Fact]
    public void Db2_CaseEndOutsideBeginDoesNotSwallowFollowingStatements()
    {
        const string source = "SELECT CASE WHEN a = 1 THEN 2 END FROM T; SELECT 2 FROM SYSIBM.SYSDUMMY1";
        var scan = SqlStatementScanner.Scan(source, SqlDialect.Db2);

        Assert.Equal(2, scan.Statements.Count);
        Assert.False(scan.HasUnterminatedConstruct);
    }

    [Fact]
    public void Db2_UnclosedBeginIsReported()
    {
        var scan = SqlStatementScanner.Scan("CREATE PROCEDURE p () LANGUAGE SQL BEGIN SELECT 1 FROM T;", SqlDialect.Db2);

        Assert.True(scan.HasUnterminatedConstruct);
    }

    [Fact]
    public void PostgreSql_DollarQuotedBodyDoesNotSplit()
    {
        const string source = "DO $$ BEGIN PERFORM 1; PERFORM 2; END $$; SELECT 3;";
        var scan = SqlStatementScanner.Scan(source, SqlDialect.PostgreSql);

        Assert.Equal(2, scan.Statements.Count);
        Assert.Contains("PERFORM 2", scan.Statements[0].Slice(source).ToString());
        Assert.False(scan.HasUnterminatedConstruct);
    }

    [Fact]
    public void PostgreSql_BeginAtomicBodyStaysOneStatement()
    {
        const string source = "CREATE FUNCTION f() RETURNS int LANGUAGE SQL BEGIN ATOMIC SELECT CASE WHEN 1 = 1 THEN 1 END; SELECT 2; END; SELECT 3;";
        var scan = SqlStatementScanner.Scan(source, SqlDialect.PostgreSql);

        Assert.Equal(2, scan.Statements.Count);
    }

    [Fact]
    public void Sqlite_TriggerBodyStaysOneStatement()
    {
        const string source = "CREATE TRIGGER trg AFTER INSERT ON t BEGIN UPDATE t SET x = 1; DELETE FROM log; END; SELECT 1;";
        var scan = SqlStatementScanner.Scan(source, SqlDialect.Sqlite);

        Assert.Equal(2, scan.Statements.Count);
        Assert.Contains("DELETE FROM log", scan.Statements[0].Slice(source).ToString());
    }

    [Fact]
    public void Oracle_QQuoteAndSlashTerminatorAreSupported()
    {
        const string source = "SELECT q'[a;b]' FROM dual;\n/\nBEGIN NULL; END;\n/";
        var scan = SqlStatementScanner.Scan(source, SqlDialect.Oracle);

        Assert.Equal(2, scan.Statements.Count);
    }

    [Fact]
    public void Split_ReturnsStatementTextInOrder()
    {
        var statements = SqlStatementScanner.Split("SELECT 1; SELECT 2;", SqlDialect.Db2);

        Assert.Equal(["SELECT 1", " SELECT 2"], statements);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(8, 0)]
    [InlineData(9, 1)]
    [InlineData(100, 1)]
    public void FindStatementIndex_MapsCaretOffsets(int position, int expected)
    {
        var statements = new[]
        {
            new SqlStatementSpan(0, 8),
            new SqlStatementSpan(9, 8),
        };

        Assert.Equal(expected, SqlStatementScanner.FindStatementIndex(statements, position));
    }
}
