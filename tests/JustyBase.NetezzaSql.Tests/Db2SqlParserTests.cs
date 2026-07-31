using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Parser;

namespace JustyBase.NetezzaSql.Tests;

/// <summary>
/// Port of src/__tests__/sqlParser/db2Parser.test.ts.
/// </summary>
public sealed class Db2SqlParserTests
{
    private static (IReadOnlyList<Statement> Statements, IReadOnlyList<ValidationError> Errors) Parse(string sql)
    {
        var tokens = Db2Lexer.Tokenize(sql).ToArray();
        var parser = new Db2SqlParser(tokens);
        var statements = new List<Statement>();
        var errors = new List<ValidationError>();
        while (parser.Position < tokens.Length)
        {
            var before = parser.Errors.Count;
            var stmt = parser.Parse();
            for (var i = before; i < parser.Errors.Count; i++)
                errors.Add(parser.Errors[i]);
            if (stmt is null)
                break;
            statements.Add(stmt);
        }
        return (statements, errors);
    }

    [Theory]
    [InlineData("SELECT ID FROM T ORDER BY ID FETCH FIRST 5 ROWS ONLY OPTIMIZE FOR 5 ROWS WITH UR")]
    [InlineData("SELECT ID FROM T FETCH FIRST 1 ROW ONLY FOR READ ONLY WITH CS")]
    [InlineData("MERGE INTO target AS T USING source AS S ON (T.id = S.id) WHEN MATCHED THEN UPDATE SET T.v = S.v WHEN NOT MATCHED THEN INSERT (id, v) VALUES (S.id, S.v)")]
    [InlineData("WITH sales AS (SELECT id FROM orders) SELECT id FROM sales")]
    [InlineData("INSERT INTO T (A, B) VALUES (1, 'x'), (2, 'y')")]
    [InlineData("DECLARE GLOBAL TEMPORARY TABLE SESSION.TMP1 (ID INTEGER) ON COMMIT PRESERVE ROWS")]
    [InlineData("CREATE ALIAS APP.ORDERS_A FOR APP.ORDERS")]
    [InlineData("CREATE NICKNAME APP.REMOTE_ORDERS FOR FEDSERVER.REMOTE_SCHEMA.ORDERS")]
    [InlineData("CREATE TABLE T (ID INTEGER NOT NULL, NAME VARCHAR(40))")]
    [InlineData("SELECT * FROM FINAL TABLE (INSERT INTO T (ID) VALUES (1))")]
    public void ParseDb2_ValidStatements_NoErrors(string sql)
    {
        var (statements, errors) = Parse(sql);
        Assert.True(errors.Count == 0,
            string.Join(Environment.NewLine, errors.Select(e => e.Message)));
        Assert.NotEmpty(statements);
    }

    [Theory]
    [InlineData("SELECT * FROM T LIMIT 1")]
    [InlineData("SELECT * FROM DB..TABLE")]
    [InlineData("GROOM TABLE sales VERSIONS")]
    [InlineData("GENERATE STATISTICS ON sales")]
    [InlineData("CREATE TABLE t (a INT) DISTRIBUTE ON (a)")]
    [InlineData("CREATE EXTERNAL TABLE ext_sales (id INT)")]
    [InlineData("CREATE SYNONYM APP.ORDERS_S FOR APP.ORDERS")]
    public void ParseDb2_RejectsNetezzaOrNonDb2(string sql)
    {
        var (_, errors) = Parse(sql);
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void ParseDb2_ProcedureUnit_Opaque()
    {
        var sql = """
            CREATE OR REPLACE PROCEDURE demo_proc (IN p_in INTEGER, OUT p_out INTEGER)
            LANGUAGE SQL
            BEGIN
              SET p_out = p_in;
            END
            """;
        var (statements, errors) = Parse(sql);
        Assert.Empty(errors);
        var unit = Assert.IsType<Db2ProcedureUnitStatement>(Assert.Single(statements));
        Assert.Equal("demo_proc", unit.Name.Name, ignoreCase: true);
        Assert.NotEmpty(unit.Tokens);
    }

    [Fact]
    public void ParseDb2_DeclareGlobalTemp_ReturnsStatement()
    {
        var (statements, errors) = Parse(
            "DECLARE GLOBAL TEMPORARY TABLE SESSION.TMP1 (ID INTEGER) ON COMMIT PRESERVE ROWS");
        Assert.Empty(errors);
        var dgtt = Assert.IsType<Db2DeclareGlobalTempTableStatement>(Assert.Single(statements));
        Assert.Equal("TMP1", dgtt.Name.Name, ignoreCase: true);
    }

    [Fact]
    public void ParseDb2_CreateAlias_ReturnsStatement()
    {
        var (statements, errors) = Parse("CREATE ALIAS APP.ORDERS_A FOR APP.ORDERS");
        Assert.Empty(errors);
        var alias = Assert.IsType<Db2CreateAliasStatement>(Assert.Single(statements));
        Assert.Equal("ORDERS_A", alias.Alias.Name, ignoreCase: true);
        Assert.Equal("ORDERS", alias.Target.Name, ignoreCase: true);
    }

    [Fact]
    public void ParseDb2_CreateNickname_ReturnsStatement()
    {
        var (statements, errors) = Parse(
            "CREATE NICKNAME APP.REMOTE_ORDERS FOR FEDSERVER.REMOTE_SCHEMA.ORDERS");
        Assert.Empty(errors);
        Assert.IsType<Db2CreateNicknameStatement>(Assert.Single(statements));
    }

    [Fact]
    public void ParseDb2_FinalTable_IsFunctionSource()
    {
        var (statements, errors) = Parse("SELECT * FROM FINAL TABLE (INSERT INTO T (ID) VALUES (1))");
        Assert.Empty(errors);
        var select = Assert.IsType<SelectStatement>(Assert.Single(statements));
        var tableRef = Assert.Single(select.From!);
        Assert.True(tableRef.Source.FunctionSource);
    }

    [Theory]
    [InlineData("SELECT CURRENT DATE FROM SYSIBM.SYSDUMMY1")]
    [InlineData("SELECT CURRENT TIME FROM SYSIBM.SYSDUMMY1")]
    [InlineData("SELECT CURRENT TIMESTAMP FROM SYSIBM.SYSDUMMY1")]
    [InlineData("SELECT CURRENT USER FROM SYSIBM.SYSDUMMY1")]
    [InlineData("SELECT CURRENT SCHEMA FROM SYSIBM.SYSDUMMY1")]
    [InlineData("SELECT CURRENT SERVER FROM SYSIBM.SYSDUMMY1")]
    [InlineData("INSERT INTO T (D) VALUES (CURRENT DATE)")]
    public void ParseDb2_CurrentSpecialValues_NoErrors(string sql)
    {
        var (statements, errors) = Parse(sql);
        Assert.True(errors.Count == 0,
            string.Join(Environment.NewLine, errors.Select(e => e.Message)));
        Assert.NotEmpty(statements);
    }

    [Fact]
    public void ParseDb2_CurrentDate_IsColumnReference()
    {
        var (statements, errors) = Parse("SELECT CURRENT DATE FROM SYSIBM.SYSDUMMY1");
        Assert.Empty(errors);
        var select = Assert.IsType<SelectStatement>(Assert.Single(statements));
        var item = Assert.Single(select.SelectList);
        var col = Assert.IsType<ColumnReference>(item.Expression);
        Assert.Contains("DATE", col.Name, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("CREATE TABLE T (ID INTEGER GENERATED ALWAYS AS IDENTITY, NAME VARCHAR(40))")]
    [InlineData("CREATE TABLE T (ID INTEGER GENERATED BY DEFAULT AS IDENTITY)")]
    [InlineData("CREATE TABLE T (ID INTEGER GENERATED ALWAYS AS IDENTITY (START WITH 1, INCREMENT BY 1))")]
    [InlineData("CREATE TABLE T (ID INTEGER NOT NULL GENERATED BY DEFAULT AS IDENTITY)")]
    public void ParseDb2_GeneratedIdentityColumn_NoErrors(string sql)
    {
        var (statements, errors) = Parse(sql);
        Assert.True(errors.Count == 0,
            string.Join(Environment.NewLine, errors.Select(e => e.Message)));
        Assert.IsType<CreateTableStatement>(Assert.Single(statements));
    }

    [Theory]
    [InlineData("CREATE TABLE T (ID INTEGER) ORGANIZE BY ROW")]
    [InlineData("CREATE TABLE T (ID INTEGER) ORGANIZE BY COLUMN")]
    [InlineData("CREATE TABLE T (ID INTEGER) DATA CAPTURE NONE")]
    [InlineData("CREATE TABLE T (ID INTEGER) DATA CAPTURE CHANGES")]
    [InlineData("CREATE TABLE T (ID INTEGER GENERATED ALWAYS AS IDENTITY) ORGANIZE BY ROW")]
    public void ParseDb2_CreateTableOptions_ConsumedWithoutError(string sql)
    {
        var (statements, errors) = Parse(sql);
        Assert.True(errors.Count == 0,
            string.Join(Environment.NewLine, errors.Select(e => e.Message)));
        Assert.IsType<CreateTableStatement>(Assert.Single(statements));
    }

    [Fact]
    public void ParseDb2_CreateTableOptions_ThenNextStatementWithoutSemicolon()
    {
        var (statements, errors) = Parse(
            "CREATE TABLE T (ID INTEGER) ORGANIZE BY ROW SELECT 1 FROM SYSIBM.SYSDUMMY1");
        Assert.Empty(errors);
        Assert.Equal(2, statements.Count);
        Assert.IsType<CreateTableStatement>(statements[0]);
        Assert.IsType<SelectStatement>(statements[1]);
    }
}
