using JustyBase.NetezzaSqlLsp.Services;
using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Linter;
using JustyBase.NetezzaSqlParser.Parser;
using Microsoft.Data.SqlClient;

namespace JustyBase.NetezzaSql.MssqlLiveTests;

public sealed class MssqlLiveFixture : IDisposable
{
    public SqlConnection? Connection { get; }
    public string Database { get; }
    public string TableName { get; }
    public string QualifiedTable { get; }
    public bool Ready { get; }

    public MssqlLiveFixture()
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
        TableName = $"JB_MSSQL_LV_{stamp}";
        Ready = MssqlLiveTestHost.TryOpen(out var connection);
        Connection = connection;
        Database = Ready ? connection!.Database : string.Empty;
        QualifiedTable = Ready ? MssqlLiveTestHost.Qualify("dbo", TableName) : string.Empty;
        if (!Ready || Connection is null)
            return;

        MssqlLiveTestHost.Execute(Connection, $"""
            CREATE TABLE {QualifiedTable} (
                ID INT NOT NULL PRIMARY KEY,
                NAME NVARCHAR(64) NOT NULL,
                NOTE NVARCHAR(200) NULL
            )
            """);
        MssqlLiveTestHost.Execute(Connection, $"""
            INSERT INTO {QualifiedTable} (ID, NAME, NOTE) VALUES (1, N'Alice', N'ok')
            """);
        MssqlLiveTestHost.Execute(Connection, $"""
            INSERT INTO {QualifiedTable} (ID, NAME, NOTE) VALUES (2, N'Bob', N'ok')
            """);
        MssqlLiveTestHost.Execute(Connection, $"""
            INSERT INTO {QualifiedTable} (ID, NAME, NOTE) VALUES (3, N'Carol', N'ok')
            """);
    }

    public void Dispose()
    {
        if (Connection is null)
            return;
        MssqlLiveTestHost.TryExecute(Connection, $"DROP TABLE {QualifiedTable}");
        Connection.Dispose();
    }
}

/// <summary>
/// Live SQL Server verification for the C# MSSQL lexer/parser/linter.
/// Soft-skips without MSSQL_LIVE_TEST_* or when the driver cannot connect.
/// </summary>
public sealed class MssqlLiveParserLinterTests : IClassFixture<MssqlLiveFixture>
{
    private readonly MssqlLiveFixture _fx;

    public MssqlLiveParserLinterTests(MssqlLiveFixture fx) => _fx = fx;

    private bool RequireLive()
    {
        if (_fx.Ready && _fx.Connection is not null)
            return true;
        Console.WriteLine("MSSQL live test not executed: MSSQL_LIVE_TEST_* not configured or driver unavailable.");
        return false;
    }

    private static (IReadOnlyList<Statement> Statements, IReadOnlyList<ValidationError> Errors) ParseMssql(string sql)
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

    private void AssertParsesAndExecutes(string sql, bool expectRows = false)
    {
        var (statements, errors) = ParseMssql(sql);
        Assert.True(errors.Count == 0,
            $"Parser errors for live SQL:{Environment.NewLine}{string.Join(Environment.NewLine, errors.Select(e => e.Message))}{Environment.NewLine}SQL:{Environment.NewLine}{sql}");
        Assert.NotEmpty(statements);

        if (expectRows)
            Assert.NotNull(MssqlLiveTestHost.ExecuteScalar(_fx.Connection!, sql));
        else
            MssqlLiveTestHost.Execute(_fx.Connection!, sql);
    }

    [Fact]
    [Trait("Category", "Live")]
    public void Live_CanConnectAndSeeSchemaObjects()
    {
        if (!RequireLive()) return;
        var tables = MssqlLiveTestHost.ListSchemaTables(_fx.Connection!);
        Assert.Contains(_fx.TableName, tables, StringComparer.OrdinalIgnoreCase);
        Console.WriteLine($"MSSQL live database={_fx.Database}; sample tables={string.Join(", ", tables.Take(8))}");
    }

    [Fact]
    [Trait("Category", "Live")]
    public void Live_SelectTop_ParsesAndExecutes()
    {
        if (!RequireLive()) return;
        AssertParsesAndExecutes(
            $"SELECT TOP 2 ID, NAME FROM {_fx.QualifiedTable} ORDER BY ID",
            expectRows: true);
        AssertParsesAndExecutes(
            $"SELECT TOP (1) ID FROM {_fx.QualifiedTable} ORDER BY ID",
            expectRows: true);
    }

    [Fact]
    [Trait("Category", "Live")]
    public void Live_OffsetFetch_ParsesAndExecutes()
    {
        if (!RequireLive()) return;
        AssertParsesAndExecutes(
            $"SELECT ID FROM {_fx.QualifiedTable} ORDER BY ID OFFSET 1 ROWS FETCH NEXT 1 ROW ONLY",
            expectRows: true);
        AssertParsesAndExecutes(
            $"SELECT ID FROM {_fx.QualifiedTable} ORDER BY ID OFFSET 1 ROWS",
            expectRows: true);
    }

    [Fact]
    [Trait("Category", "Live")]
    public void Live_CrossApply_ParsesAndExecutes()
    {
        if (!RequireLive()) return;
        AssertParsesAndExecutes(
            $"""
            SELECT t1.ID, x.NOTE
            FROM {_fx.QualifiedTable} t1
            CROSS APPLY (SELECT TOP 1 t2.NOTE FROM {_fx.QualifiedTable} t2 WHERE t2.ID = t1.ID) x
            ORDER BY t1.ID
            """,
            expectRows: true);
        AssertParsesAndExecutes(
            $"""
            SELECT t1.ID, x.NOTE
            FROM {_fx.QualifiedTable} t1
            OUTER APPLY (SELECT TOP 1 t2.NOTE FROM {_fx.QualifiedTable} t2 WHERE t2.ID = 99) x
            ORDER BY t1.ID
            """,
            expectRows: true);
    }

    [Fact]
    [Trait("Category", "Live")]
    public void Live_OutputClause_ParsesAndExecutes()
    {
        if (!RequireLive()) return;
        var inserted = MssqlLiveTestHost.ExecuteScalar(_fx.Connection!,
            $"INSERT INTO {_fx.QualifiedTable} (ID, NAME, NOTE) OUTPUT inserted.ID VALUES (42, N'Dave', N'ok')");
        Assert.Equal(42, Convert.ToInt32(inserted));
    }

    [Fact]
    [Trait("Category", "Live")]
    public void Live_BracketedIdentifiers_ParsesAndExecutes()
    {
        if (!RequireLive()) return;
        AssertParsesAndExecutes(
            $"SELECT [ID] FROM [dbo].[{_fx.TableName}] ORDER BY [ID]",
            expectRows: true);
    }

    [Fact]
    [Trait("Category", "Live")]
    public void Live_UpdateOutputFromJoin_ParsesAndExecutes()
    {
        if (!RequireLive()) return;
        AssertParsesAndExecutes(
            $"""
            UPDATE {_fx.QualifiedTable} SET NOTE = N'live' OUTPUT inserted.ID
            FROM {_fx.QualifiedTable} src WHERE src.ID = 999
            """);
    }

    [Fact]
    [Trait("Category", "Live")]
    public void Live_DeleteOutputFromJoin_ParsesAndExecutes()
    {
        if (!RequireLive()) return;
        AssertParsesAndExecutes(
            $"""
            DELETE FROM {_fx.QualifiedTable} OUTPUT deleted.ID
            FROM {_fx.QualifiedTable} src WHERE src.ID = 999
            """);
    }

    [Fact]
    [Trait("Category", "Live")]
    public void Live_TableHintWithNolock_ParsesAndExecutes()
    {
        if (!RequireLive()) return;
        AssertParsesAndExecutes(
            $"SELECT TOP 2 ID FROM {_fx.QualifiedTable} WITH (NOLOCK) ORDER BY ID",
            expectRows: true);
    }

    [Fact]
    [Trait("Category", "Live")]
    public void Live_CreateProcedure_ParsesAndExecutes()
    {
        if (!RequireLive()) return;
        var proc = $"JB_MSSQL_P_{DateTime.UtcNow:HHmmssfff}";
        try
        {
            var sql = $"""
                CREATE PROCEDURE {MssqlLiveTestHost.Qualify("dbo", proc)} @p_in INT, @p_out INT OUTPUT AS
                BEGIN
                  SET NOCOUNT ON;
                  SET @p_out = @p_in * 2;
                END
                """;
            AssertParsesAndExecutes(sql);

            using var cmd = _fx.Connection!.CreateCommand();
            cmd.CommandText = $"""
                EXEC {MssqlLiveTestHost.Qualify("dbo", proc)} @p_in = 21, @p_out = @o OUTPUT;
                SELECT @o AS RESULT;
                """;
            cmd.Parameters.Add("@o", System.Data.SqlDbType.Int).Direction = System.Data.ParameterDirection.Output;
            using var reader = cmd.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal(42, Convert.ToInt32(reader["RESULT"]));
        }
        finally
        {
            MssqlLiveTestHost.TryExecute(_fx.Connection!, $"DROP PROCEDURE {MssqlLiveTestHost.Qualify("dbo", proc)}");
        }
    }

    [Fact]
    [Trait("Category", "Live")]
    public void Live_Lint_Mss001_Through_Mss008()
    {
        if (!RequireLive()) return;

        var selectStar = LintService.Lint($"SELECT * FROM {_fx.QualifiedTable}", null, SqlDialect.Mssql);
        Assert.Contains(selectStar, d => d.Code == "MSS001");

        var deleteAll = LintService.Lint($"DELETE FROM {_fx.QualifiedTable}", null, SqlDialect.Mssql);
        Assert.Contains(deleteAll, d => d.Code == "MSS002");

        var updateAll = LintService.Lint(
            $"UPDATE {_fx.QualifiedTable} SET NOTE = N'x'",
            null,
            SqlDialect.Mssql);
        Assert.Contains(updateAll, d => d.Code == "MSS003");

        Assert.Contains(
            LintService.Lint("GROOM TABLE T", null, SqlDialect.Mssql),
            d => d.Code == "MSS004");
        Assert.Contains(
            LintService.Lint("CREATE TABLE T (A INT) DISTRIBUTE ON (A)", null, SqlDialect.Mssql),
            d => d.Code == "MSS005");
        Assert.Contains(
            LintService.Lint($"SELECT TOP 5 ID FROM {_fx.QualifiedTable}", null, SqlDialect.Mssql),
            d => d.Code == "MSS006");
        Assert.Contains(
            LintService.Lint("SELECT * FROM T LIMIT 10", null, SqlDialect.Mssql),
            d => d.Code == "MSS007");
        Assert.Contains(
            LintService.Lint("SELECT * FROM DB..TABLE", null, SqlDialect.Mssql),
            d => d.Code == "MSS008");

        Assert.DoesNotContain(
            LintService.Lint($"SELECT * FROM {_fx.QualifiedTable}", null, SqlDialect.Mssql),
            d => d.Code?.StartsWith("NZ", StringComparison.Ordinal) == true);

        var registry = new QualityRuleRegistry(MssqlLintRules.AllRules);
        Assert.Equal(8, registry.AllRules.Count);
    }

    [Fact]
    [Trait("Category", "Live")]
    public void Live_Reject_LimitAndGroom_AtParser()
    {
        if (!RequireLive()) return;
        var (_, limitErrors) = ParseMssql($"SELECT ID FROM {_fx.QualifiedTable} LIMIT 1");
        Assert.NotEmpty(limitErrors);
        var (_, groomErrors) = ParseMssql($"GROOM TABLE {_fx.QualifiedTable}");
        Assert.NotEmpty(groomErrors);
    }
}
