using JustyBase.NetezzaSqlLsp.Services;
using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Linter;
using JustyBase.NetezzaSqlParser.Parser;
using IBM.Data.Db2;

namespace JustyBase.NetezzaSql.Db2LiveTests;

public sealed class Db2LiveFixture : IDisposable
{
    public DB2Connection? Connection { get; }
    public string Schema { get; }
    public string TableName { get; }
    public string QualifiedTable { get; }
    public string AliasName { get; }
    public string QualifiedAlias { get; }
    public bool Ready { get; }

    public Db2LiveFixture()
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
        TableName = $"JB_DB2_LV_{stamp}";
        AliasName = $"JB_DB2_A_{stamp}";
        Ready = Db2LiveTestHost.TryOpen(out var connection, out var schema);
        Connection = connection;
        Schema = schema;
        QualifiedTable = Ready ? Db2LiveTestHost.Qualify(Schema, TableName) : string.Empty;
        QualifiedAlias = Ready ? Db2LiveTestHost.Qualify(Schema, AliasName) : string.Empty;
        if (!Ready || Connection is null)
            return;

        Db2LiveTestHost.Execute(Connection, $"""
            CREATE TABLE {QualifiedTable} (
                ID INTEGER NOT NULL PRIMARY KEY,
                NAME VARCHAR(64) NOT NULL,
                NOTE VARCHAR(200)
            )
            """);
        Db2LiveTestHost.Execute(Connection, $"""
            INSERT INTO {QualifiedTable} (ID, NAME, NOTE) VALUES (1, 'Alice', 'ok')
            """);
        Db2LiveTestHost.Execute(Connection, $"""
            INSERT INTO {QualifiedTable} (ID, NAME, NOTE) VALUES (2, 'Bob', 'ok')
            """);
        Db2LiveTestHost.Execute(Connection, $"""
            INSERT INTO {QualifiedTable} (ID, NAME, NOTE) VALUES (3, 'Carol', 'ok')
            """);
        Db2LiveTestHost.Execute(Connection, "COMMIT");
    }

    public void Dispose()
    {
        if (Connection is null)
            return;
        Db2LiveTestHost.TryExecute(Connection, $"DROP ALIAS {QualifiedAlias}");
        Db2LiveTestHost.TryExecute(Connection, $"DROP TABLE {QualifiedTable}");
        Connection.Dispose();
    }
}

/// <summary>
/// Live Db2 LUW verification for the C# Db2 lexer/parser/linter.
/// Soft-skips without DB2_LIVE_TEST_* or when the IBM driver/clidriver is unavailable.
/// </summary>
public sealed class Db2LiveParserLinterTests : IClassFixture<Db2LiveFixture>
{
    private readonly Db2LiveFixture _fx;

    public Db2LiveParserLinterTests(Db2LiveFixture fx) => _fx = fx;

    private bool RequireLive()
    {
        if (_fx.Ready && _fx.Connection is not null)
            return true;
        Console.WriteLine("Db2 live test not executed: DB2_LIVE_TEST_* not configured or driver unavailable.");
        return false;
    }

    private static (IReadOnlyList<Statement> Statements, IReadOnlyList<ValidationError> Errors) ParseDb2(string sql)
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

    private void AssertParsesAndExecutes(string sql, bool expectRows = false)
    {
        var (statements, errors) = ParseDb2(sql);
        Assert.True(errors.Count == 0,
            $"Parser errors for live SQL:{Environment.NewLine}{string.Join(Environment.NewLine, errors.Select(e => e.Message))}{Environment.NewLine}SQL:{Environment.NewLine}{sql}");
        Assert.NotEmpty(statements);

        if (expectRows)
            Assert.NotNull(Db2LiveTestHost.ExecuteScalar(_fx.Connection!, sql));
        else
            Db2LiveTestHost.Execute(_fx.Connection!, sql);
    }

    [Fact]
    [Trait("Category", "Live")]
    public void Live_CanConnectAndSeeSchemaObjects()
    {
        if (!RequireLive()) return;
        var tables = Db2LiveTestHost.ListSchemaTables(_fx.Connection!, _fx.Schema);
        Assert.Contains(_fx.TableName, tables, StringComparer.OrdinalIgnoreCase);
        Console.WriteLine($"Db2 live schema={_fx.Schema}; sample tables={string.Join(", ", tables.Take(8))}");
    }

    [Fact]
    [Trait("Category", "Live")]
    public void Live_SelectSysdummy_ParsesAndExecutes()
    {
        if (!RequireLive()) return;
        AssertParsesAndExecutes("SELECT 1 AS N FROM SYSIBM.SYSDUMMY1", expectRows: true);
    }

    [Fact]
    [Trait("Category", "Live")]
    public void Live_FetchFirst_WithUr_ParsesAndExecutes()
    {
        if (!RequireLive()) return;
        AssertParsesAndExecutes(
            $"SELECT ID, NAME FROM {_fx.QualifiedTable} ORDER BY ID FETCH FIRST 2 ROWS ONLY WITH UR",
            expectRows: true);
    }

    [Fact]
    [Trait("Category", "Live")]
    public void Live_OptimizeFor_ForReadOnly_ParsesAndExecutes()
    {
        if (!RequireLive()) return;
        AssertParsesAndExecutes(
            $"SELECT ID FROM {_fx.QualifiedTable} ORDER BY ID FETCH FIRST 1 ROW ONLY OPTIMIZE FOR 1 ROW FOR READ ONLY WITH CS",
            expectRows: true);
    }

    [Fact]
    [Trait("Category", "Live")]
    public void Live_OffsetFetch_FirstNextAndOffsetOnly_ParseAndExecute()
    {
        if (!RequireLive()) return;

        AssertParsesAndExecutes(
            $"SELECT ID FROM {_fx.QualifiedTable} ORDER BY ID OFFSET 1 ROWS FETCH FIRST 1 ROW ONLY",
            expectRows: true);
        AssertParsesAndExecutes(
            $"SELECT ID FROM {_fx.QualifiedTable} ORDER BY ID OFFSET 1 ROW FETCH NEXT 1 ROW ONLY",
            expectRows: true);
        AssertParsesAndExecutes(
            $"SELECT ID FROM {_fx.QualifiedTable} ORDER BY ID OFFSET 1 ROWS",
            expectRows: true);
    }

    [Fact]
    [Trait("Category", "Live")]
    public void Live_Merge_ParsesAndExecutes()
    {
        if (!RequireLive()) return;
        var sql = $"""
            MERGE INTO {_fx.QualifiedTable} AS T
            USING (SELECT 4 AS ID, 'Dave' AS NAME, 'ok' AS NOTE FROM SYSIBM.SYSDUMMY1) AS S
            ON (T.ID = S.ID)
            WHEN MATCHED THEN UPDATE SET T.NOTE = S.NOTE
            WHEN NOT MATCHED THEN INSERT (ID, NAME, NOTE) VALUES (S.ID, S.NAME, S.NOTE)
            """;
        AssertParsesAndExecutes(sql);
        Db2LiveTestHost.Execute(_fx.Connection!, "COMMIT");
    }

    [Fact]
    [Trait("Category", "Live")]
    public void Live_Dgtt_ParsesAndExecutes()
    {
        if (!RequireLive()) return;
        var tmp = $"SESSION.JB_TMP_{DateTime.UtcNow:HHmmssfff}";
        AssertParsesAndExecutes($"""
            DECLARE GLOBAL TEMPORARY TABLE {tmp} (ID INTEGER) ON COMMIT PRESERVE ROWS NOT LOGGED
            """);
        Db2LiveTestHost.TryExecute(_fx.Connection!, $"DROP TABLE {tmp}");
    }

    [Fact]
    [Trait("Category", "Live")]
    public void Live_CreateAlias_ParsesAndExecutes()
    {
        if (!RequireLive()) return;
        AssertParsesAndExecutes($"CREATE ALIAS {_fx.QualifiedAlias} FOR {_fx.QualifiedTable}");
        AssertParsesAndExecutes($"SELECT ID FROM {_fx.QualifiedAlias} FETCH FIRST 1 ROW ONLY", expectRows: true);
    }

    [Fact]
    [Trait("Category", "Live")]
    public void Live_Lint_Db2001_Through_Db2008()
    {
        if (!RequireLive()) return;

        var selectStar = LintService.Lint($"SELECT * FROM {_fx.QualifiedTable}", null, SqlDialect.Db2);
        Assert.Contains(selectStar, d => d.Code == "DB2001");

        var deleteAll = LintService.Lint($"DELETE FROM {_fx.QualifiedTable}", null, SqlDialect.Db2);
        Assert.Contains(deleteAll, d => d.Code == "DB2002");

        var updateAll = LintService.Lint(
            $"UPDATE {_fx.QualifiedTable} SET NOTE = 'x'",
            null,
            SqlDialect.Db2);
        Assert.Contains(updateAll, d => d.Code == "DB2003");

        Assert.Contains(
            LintService.Lint("GROOM TABLE T", null, SqlDialect.Db2),
            d => d.Code == "DB2004");
        Assert.Contains(
            LintService.Lint("CREATE TABLE T (A INT) DISTRIBUTE ON (A)", null, SqlDialect.Db2),
            d => d.Code == "DB2005");
        Assert.Contains(
            LintService.Lint($"SELECT ID FROM {_fx.QualifiedTable} FETCH FIRST 5 ROWS ONLY", null, SqlDialect.Db2),
            d => d.Code == "DB2006");
        Assert.Contains(
            LintService.Lint("SELECT * FROM T LIMIT 10", null, SqlDialect.Db2),
            d => d.Code == "DB2007");
        Assert.Contains(
            LintService.Lint("SELECT * FROM DB..TABLE", null, SqlDialect.Db2),
            d => d.Code == "DB2008");

        Assert.DoesNotContain(
            LintService.Lint($"SELECT * FROM {_fx.QualifiedTable}", null, SqlDialect.Db2),
            d => d.Code?.StartsWith("NZ", StringComparison.Ordinal) == true);

        var registry = new QualityRuleRegistry(Db2LintRules.AllRules);
        Assert.Equal(8, registry.AllRules.Count);
    }

    [Fact]
    [Trait("Category", "Live")]
    public void Live_Reject_LimitAndGroom_AtParser()
    {
        if (!RequireLive()) return;
        var (_, limitErrors) = ParseDb2($"SELECT ID FROM {_fx.QualifiedTable} LIMIT 1");
        Assert.NotEmpty(limitErrors);
        var (_, groomErrors) = ParseDb2($"GROOM TABLE {_fx.QualifiedTable}");
        Assert.NotEmpty(groomErrors);
    }
}
