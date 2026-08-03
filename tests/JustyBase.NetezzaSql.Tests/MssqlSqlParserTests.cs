using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Parser;

namespace JustyBase.NetezzaSql.Tests;

/// <summary>
/// Port of src/__tests__/sqlParser/mssqlParser.test.ts.
/// </summary>
public sealed class MssqlSqlParserTests
{
    private static (IReadOnlyList<Statement> Statements, IReadOnlyList<ValidationError> Errors) Parse(string sql)
    {
        var tokens = MssqlLexer.Tokenize(sql).ToArray();
        var parser = new MssqlSqlParser(tokens);
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
    [InlineData("SELECT TOP 10 * FROM dbo.Orders")]
    [InlineData("SELECT TOP (10) * FROM dbo.Orders")]
    [InlineData("SELECT TOP 10 PERCENT id FROM dbo.Orders")]
    [InlineData("SELECT TOP (10) PERCENT id FROM dbo.Orders ORDER BY id")]
    [InlineData("SELECT TOP 5 WITH TIES id FROM dbo.Orders ORDER BY id")]
    [InlineData("SELECT id FROM dbo.Orders ORDER BY id OFFSET 2 ROWS FETCH NEXT 5 ROWS ONLY")]
    [InlineData("SELECT id FROM dbo.Orders ORDER BY id OFFSET 2 ROWS")]
    [InlineData("SELECT id FROM dbo.Orders ORDER BY id FETCH FIRST 5 ROWS ONLY")]
    [InlineData("SELECT [Order Id] FROM [Sales].[Order Items]")]
    [InlineData("SELECT id FROM dbo.Orders AS o WHERE [Status] = 'active'")]
    [InlineData("SELECT @p")]
    [InlineData("SELECT id INTO newTable FROM dbo.Orders")]
    [InlineData("SELECT * FROM a CROSS APPLY b")]
    [InlineData("SELECT * FROM a CROSS APPLY b x")]
    [InlineData("SELECT * FROM a OUTER APPLY b")]
    [InlineData("SELECT * FROM a CROSS APPLY dbo.fn(id) x")]
    [InlineData("SELECT * FROM a CROSS APPLY (SELECT id FROM b) x")]
    [InlineData("SELECT * FROM a CROSS APPLY b x OUTER APPLY c y")]
    [InlineData("SELECT * FROM (SELECT TOP 5 id FROM dbo.Orders) t")]
    [InlineData("WITH top5 AS (SELECT TOP 5 id FROM dbo.Orders) SELECT * FROM top5")]
    [InlineData("INSERT INTO dbo.Orders (id) VALUES (1)")]
    [InlineData("INSERT INTO dbo.Orders (id) OUTPUT inserted.id VALUES (1)")]
    [InlineData("INSERT INTO dbo.Orders (id) OUTPUT inserted.id INTO @ids VALUES (1)")]
    [InlineData("INSERT INTO dbo.Orders (id) SELECT id FROM dbo.Other")]
    [InlineData("UPDATE dbo.Orders SET status = 'x' WHERE id = 1")]
    [InlineData("UPDATE dbo.Orders SET status = 'x' OUTPUT inserted.id WHERE id = 1")]
    [InlineData("DELETE FROM dbo.Orders WHERE id = 1")]
    [InlineData("DELETE FROM dbo.Orders OUTPUT deleted.id WHERE id = 1")]
    [InlineData("MERGE INTO dbo.Target AS T USING dbo.Source AS S ON (T.id = S.id) WHEN MATCHED THEN UPDATE SET T.v = S.v WHEN NOT MATCHED THEN INSERT (id, v) VALUES (S.id, S.v)")]
    [InlineData("CREATE TABLE dbo.T (id INT IDENTITY(1,1) PRIMARY KEY, name NVARCHAR(50) NOT NULL)")]
    [InlineData("CREATE TABLE dbo.T (id INT, name VARCHAR(40))")]
    [InlineData("CREATE VIEW dbo.V AS SELECT id FROM dbo.Orders")]
    [InlineData("SELECT 1 GO SELECT 2")]
    [InlineData("DECLARE @x INT")]
    [InlineData("SET @x = 1")]
    public void ParseMssql_ValidStatements_NoErrors(string sql)
    {
        var (statements, errors) = Parse(sql);
        Assert.True(errors.Count == 0,
            string.Join(Environment.NewLine, errors.Select(e => e.Message)));
        Assert.NotEmpty(statements);
    }

    [Fact]
    public void ParseMssql_LoneGo_ProducesNoStatement()
    {
        var (statements, errors) = Parse("GO");
        Assert.Empty(statements);
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("SELECT * FROM T LIMIT 1")]
    [InlineData("SELECT * FROM DB..TABLE")]
    [InlineData("GROOM TABLE sales VERSIONS")]
    [InlineData("GENERATE STATISTICS ON sales")]
    [InlineData("CREATE TABLE t (a INT) DISTRIBUTE ON (a)")]
    [InlineData("CREATE EXTERNAL TABLE ext_sales (id INT)")]
    public void ParseMssql_RejectsNetezzaOnly(string sql)
    {
        var (_, errors) = Parse(sql);
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void ParseMssql_TopIsOpaqueTokenRange()
    {
        var (statements, errors) = Parse("SELECT TOP 10 PERCENT WITH TIES id FROM dbo.Orders ORDER BY id");
        Assert.Empty(errors);
        var select = Assert.IsType<SelectStatement>(Assert.Single(statements));
        Assert.NotNull(select.TopTokens);
        var text = string.Join(" ", select.TopTokens!.Select(t => t.ToStringValue()));
        Assert.Equal("TOP 10 PERCENT WITH TIES", text, ignoreCase: true);
    }

    [Fact]
    public void ParseMssql_TopInsideSubquery_NoErrors()
    {
        var (statements, errors) = Parse(
            "SELECT o.id FROM dbo.Orders o WHERE o.id IN (SELECT TOP 1 id FROM dbo.Payments)");
        Assert.Empty(errors);
        Assert.IsType<SelectStatement>(Assert.Single(statements));
    }

    [Fact]
    public void ParseMssql_OutputOnInsert_IsOpaqueTokenRange()
    {
        var (statements, errors) = Parse("INSERT INTO dbo.Orders (id) OUTPUT inserted.id VALUES (1)");
        Assert.Empty(errors);
        var insert = Assert.IsType<InsertStatement>(Assert.Single(statements));
        var output = insert.OutputTokens;
        Assert.NotNull(output);
        Assert.Equal(NzToken.MssqlOutput, output[0].Kind);
        Assert.Collection(output,
            t => Assert.Equal(NzToken.MssqlOutput, t.Kind),
            t => Assert.Equal("inserted", t.ToStringValue()),
            t => Assert.Equal(NzToken.Dot, t.Kind),
            t => Assert.Equal("id", t.ToStringValue()));
    }

    [Fact]
    public void ParseMssql_OutputOnUpdate_BeforeWhere()
    {
        var (statements, errors) = Parse(
            "UPDATE dbo.Orders SET status = 'x' OUTPUT inserted.id WHERE id = 1");
        Assert.Empty(errors);
        var update = Assert.IsType<UpdateStatement>(Assert.Single(statements));
        Assert.NotNull(update.OutputTokens);
        Assert.NotNull(update.Where);
    }

    [Fact]
    public void ParseMssql_OutputOnDelete_BeforeWhere()
    {
        var (statements, errors) = Parse(
            "DELETE FROM dbo.Orders OUTPUT deleted.id WHERE id = 1");
        Assert.Empty(errors);
        var delete = Assert.IsType<DeleteStatement>(Assert.Single(statements));
        Assert.NotNull(delete.OutputTokens);
        Assert.NotNull(delete.Where);
    }

    [Fact]
    public void ParseMssql_OutputOnUpdate_BeforeFrom_JoinSourceCaptured()
    {
        var (statements, errors) = Parse(
            "UPDATE dbo.Orders SET status = 'x' OUTPUT inserted.id FROM dbo.Source s WHERE s.id = 1");
        Assert.Empty(errors);
        var update = Assert.IsType<UpdateStatement>(Assert.Single(statements));
        Assert.NotNull(update.OutputTokens);
        Assert.NotNull(update.From);
        Assert.Equal("Source", Assert.Single(update.From!).Source.Table!.Name);
        Assert.NotNull(update.Where);
    }

    [Fact]
    public void ParseMssql_OutputOnDelete_BeforeFrom_JoinSourceCaptured()
    {
        var (statements, errors) = Parse(
            "DELETE FROM dbo.Orders OUTPUT deleted.id FROM dbo.Source s WHERE s.id = 1");
        Assert.Empty(errors);
        var delete = Assert.IsType<DeleteStatement>(Assert.Single(statements));
        Assert.NotNull(delete.OutputTokens);
        Assert.NotNull(delete.From);
        Assert.Equal("Source", Assert.Single(delete.From!).Source.Table!.Name);
        Assert.NotNull(delete.Where);
    }

    [Fact]
    public void ParseMssql_OutputIntoTableVariable_BeforeWhere()
    {
        var (statements, errors) = Parse(
            "UPDATE dbo.Orders SET status = 'x' OUTPUT inserted.id INTO @ids WHERE id = 1");
        Assert.Empty(errors);
        var update = Assert.IsType<UpdateStatement>(Assert.Single(statements));
        Assert.NotNull(update.OutputTokens);
        Assert.NotNull(update.Where);
    }

    [Fact]
    public void ParseMssql_Apply_IsCaptured()
    {
        var (statements, errors) = Parse(
            "SELECT a.id, x.v FROM dbo.A a CROSS APPLY dbo.fn(a.id) x");
        Assert.Empty(errors);
        var select = Assert.IsType<SelectStatement>(Assert.Single(statements));
        var tableRef = Assert.Single(select.From!);
        var apply = Assert.Single(tableRef.Applies!);
        Assert.False(apply.Outer);
        Assert.Equal("fn", apply.Source.Table!.Name);
    }

    [Fact]
    public void ParseMssql_GoSeparator_SplitsStatements()
    {
        var (statements, errors) = Parse("SELECT 1; GO SELECT 2");
        Assert.Empty(errors);
        Assert.Equal(2, statements.Count);
        Assert.All(statements, s => Assert.IsType<SelectStatement>(s));
    }

    [Fact]
    public void ParseMssql_ProcedureSingleStatementBody_StopsAtGo()
    {
        var (statements, errors) = Parse(
            "CREATE PROC dbo.quick AS SELECT 1 AS result GO SELECT 2");
        Assert.Empty(errors);
        Assert.Equal(2, statements.Count);
        Assert.IsType<MssqlProcedureUnitStatement>(statements[0]);
        Assert.IsType<SelectStatement>(statements[1]);
    }

    [Fact]
    public void ParseMssql_ProcedureTryCatchBody_IsSingleOpaqueUnit()
    {
        var sql = """
            CREATE PROCEDURE dbo.demo_try @p_in INT AS
            BEGIN TRY
              SELECT 1 / @p_in;
            END TRY
            BEGIN CATCH
              THROW;
            END CATCH
            """;
        var (statements, errors) = Parse(sql);
        Assert.Empty(errors);
        var unit = Assert.IsType<MssqlProcedureUnitStatement>(Assert.Single(statements));
        Assert.Equal("demo_try", unit.Name.Name, ignoreCase: true);
        var text = string.Join(" ", unit.Tokens.Select(t => t.ToStringValue()));
        Assert.Contains("TRY", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CATCH", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseMssql_ProcedureOuterBeginTryCatch_IsSingleOpaqueUnit()
    {
        var sql = """
            CREATE PROCEDURE dbo.demo_outer @p_in INT AS
            BEGIN
              BEGIN TRY
                SELECT 1 / @p_in;
              END TRY
              BEGIN CATCH
                THROW;
              END CATCH
            END
            """;
        var (statements, errors) = Parse(sql);
        Assert.Empty(errors);
        Assert.IsType<MssqlProcedureUnitStatement>(Assert.Single(statements));
    }

    [Fact]
    public void ParseMssql_TableHintWithNolock_NoCascadingErrors()
    {
        var (statements, errors) = Parse(
            "SELECT id FROM dbo.Orders WITH (NOLOCK) WHERE id = 1");
        Assert.Empty(errors);
        Assert.IsType<SelectStatement>(Assert.Single(statements));
    }

    [Fact]
    public void ParseMssql_TableHintOnJoinSource_NoErrors()
    {
        var (statements, errors) = Parse(
            "SELECT o.id FROM dbo.Orders o WITH (NOLOCK) JOIN dbo.Payments p WITH (INDEX (ix_1)) ON o.id = p.id");
        Assert.Empty(errors);
        Assert.IsType<SelectStatement>(Assert.Single(statements));
    }

    [Fact]
    public void ParseMssql_CreateProcedure_OpaqueUnit()
    {
        var sql = """
            CREATE PROCEDURE dbo.demo_proc @p_in INT, @p_out INT OUTPUT AS
            BEGIN
              SET NOCOUNT ON;
              SET @p_out = @p_in;
            END
            """;
        var (statements, errors) = Parse(sql);
        Assert.Empty(errors);
        var unit = Assert.IsType<MssqlProcedureUnitStatement>(Assert.Single(statements));
        Assert.Equal("demo_proc", unit.Name.Name, ignoreCase: true);
        Assert.Equal("dbo", unit.Name.Schema, ignoreCase: true);
        Assert.NotEmpty(unit.Tokens);
    }

    [Fact]
    public void ParseMssql_CreateProcedure_AsSingleStatement()
    {
        var (statements, errors) = Parse("CREATE PROC dbo.quick AS SELECT 1 AS result");
        Assert.Empty(errors);
        Assert.IsType<MssqlProcedureUnitStatement>(Assert.Single(statements));
    }

    [Fact]
    public void ParseMssql_BracketedTableName_NoErrors()
    {
        var (statements, errors) = Parse("SELECT * FROM [Sales].[Order Items] WHERE [Status] = 1");
        Assert.Empty(errors);
        var select = Assert.IsType<SelectStatement>(Assert.Single(statements));
        Assert.Equal("Order Items", select.From![0].Source.Table!.Name);
    }

    [Fact]
    public void ParseMssql_Variable_IsParameterExpression()
    {
        var (statements, errors) = Parse("SELECT @p");
        Assert.Empty(errors);
        var select = Assert.IsType<SelectStatement>(Assert.Single(statements));
        Assert.IsType<ParameterExpression>(Assert.Single(select.SelectList).Expression);
    }

    [Fact]
    public void ParseMssql_IdentityColumn_NoErrors()
    {
        var (statements, errors) = Parse(
            "CREATE TABLE dbo.T (id INT IDENTITY(1,1) PRIMARY KEY, name NVARCHAR(50))");
        Assert.Empty(errors);
        Assert.IsType<CreateTableStatement>(Assert.Single(statements));
    }
}
