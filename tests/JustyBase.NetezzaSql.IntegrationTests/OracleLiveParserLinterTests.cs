using JustyBase.NetezzaSqlLsp.Services;
using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Linter;
using JustyBase.NetezzaSqlParser.Parser;
using JustyBase.NetezzaSqlParser.Visitor;
using Oracle.ManagedDataAccess.Client;

namespace JustyBase.NetezzaSql.IntegrationTests;

/// <summary>
/// Shared Oracle live fixture: one connection + disposable table/package for the suite.
/// </summary>
public sealed class OracleLiveFixture : IDisposable
{
    public OracleConnection? Connection { get; }
    public string Schema { get; }
    public string TableName { get; }
    public string QualifiedTable { get; }
    public string PackageName { get; }
    public string QualifiedPackage { get; }
    public bool Ready { get; }

    public OracleLiveFixture()
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
        TableName = $"JB_ORA_LV_{stamp}";
        PackageName = $"JB_ORA_PKG_{stamp}";
        Ready = OracleLiveTestHost.TryOpen(out var connection, out var schema);
        Connection = connection;
        Schema = schema;
        QualifiedTable = Ready ? OracleLiveTestHost.Qualify(Schema, TableName) : string.Empty;
        QualifiedPackage = Ready ? OracleLiveTestHost.Qualify(Schema, PackageName) : string.Empty;
        if (!Ready || Connection is null)
            return;

        OracleLiveTestHost.Execute(Connection, $"""
            CREATE TABLE {QualifiedTable} (
                ID NUMBER(10) PRIMARY KEY,
                NAME VARCHAR2(64) NOT NULL,
                DEPT_ID NUMBER(10),
                SALARY NUMBER(12,2),
                NOTE VARCHAR2(200)
            )
            """);
        OracleLiveTestHost.Execute(Connection, $"""
            INSERT INTO {QualifiedTable} (ID, NAME, DEPT_ID, SALARY, NOTE) VALUES (1, 'Alice', 10, 1000, 'ok')
            """);
        OracleLiveTestHost.Execute(Connection, $"""
            INSERT INTO {QualifiedTable} (ID, NAME, DEPT_ID, SALARY, NOTE) VALUES (2, 'Bob', 10, 1200, 'ok')
            """);
        OracleLiveTestHost.Execute(Connection, $"""
            INSERT INTO {QualifiedTable} (ID, NAME, DEPT_ID, SALARY, NOTE) VALUES (3, 'Carol', 20, 1500, 'ok')
            """);
        OracleLiveTestHost.Execute(Connection, "COMMIT");
    }

    public void Dispose()
    {
        if (Connection is null)
            return;
        OracleLiveTestHost.TryExecute(Connection, $"DROP PACKAGE {QualifiedPackage}");
        OracleLiveTestHost.TryExecute(Connection, $"DROP TABLE {QualifiedTable} PURGE");
        Connection.Dispose();
    }
}

/// <summary>
/// Live Oracle verification for the C# Oracle lexer/parser/linter.
/// Soft-skips without ORACLE_LIVE_TEST_*; creates disposable fixtures when connected.
/// </summary>
public sealed class OracleLiveParserLinterTests : IClassFixture<OracleLiveFixture>
{
    private readonly OracleLiveFixture _fx;

    public OracleLiveParserLinterTests(OracleLiveFixture fx) => _fx = fx;

    private bool RequireLive()
    {
        if (_fx.Ready && _fx.Connection is not null)
            return true;
        Console.WriteLine("Oracle live test not executed: ORACLE_LIVE_TEST_* not configured.");
        return false;
    }

    private static (IReadOnlyList<Statement> Statements, IReadOnlyList<ValidationError> Errors) ParseOracle(string sql)
    {
        var tokens = OracleLexer.Tokenize(sql).ToArray();
        var parser = new OracleSqlParser(tokens);
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
        var (statements, errors) = ParseOracle(sql);
        Assert.True(errors.Count == 0,
            $"Parser errors for live SQL:{Environment.NewLine}{string.Join(Environment.NewLine, errors.Select(e => e.Message))}{Environment.NewLine}SQL:{Environment.NewLine}{sql}");
        Assert.NotEmpty(statements);

        if (expectRows)
            Assert.NotNull(OracleLiveTestHost.ExecuteScalar(_fx.Connection!, sql));
        else
            OracleLiveTestHost.Execute(_fx.Connection!, sql);
    }

    [Fact]
    [Trait("Category", "Live")]
    public void Live_CanConnectAndSeeSchemaObjects()
    {
        if (!RequireLive()) return;
        var tables = OracleLiveTestHost.ListUserTables(_fx.Connection!);
        Assert.Contains(_fx.TableName, tables, StringComparer.OrdinalIgnoreCase);
        Console.WriteLine($"Oracle live schema={_fx.Schema}; sample tables={string.Join(", ", tables.Take(8))}");
    }

    [Fact]
    [Trait("Category", "Live")]
    public void Live_SelectDual_ParsesAndExecutes()
    {
        if (!RequireLive()) return;
        AssertParsesAndExecutes("SELECT 1 AS N FROM DUAL", expectRows: true);
    }

    [Fact]
    [Trait("Category", "Live")]
    public void Live_FetchFirst_ParsesAndExecutes()
    {
        if (!RequireLive()) return;
        AssertParsesAndExecutes(
            $"SELECT ID, NAME FROM {_fx.QualifiedTable} ORDER BY ID FETCH FIRST 2 ROWS ONLY",
            expectRows: true);
    }

    [Fact]
    [Trait("Category", "Live")]
    public void Live_FetchNextPercentWithTies_Parses()
    {
        if (!RequireLive()) return;
        var (_, errors) = ParseOracle(
            $"SELECT ID FROM {_fx.QualifiedTable} ORDER BY SALARY DESC FETCH NEXT 50 PERCENT ROWS WITH TIES");
        Assert.Empty(errors);

        AssertParsesAndExecutes(
            $"SELECT ID FROM {_fx.QualifiedTable} ORDER BY ID FETCH NEXT 1 ROWS ONLY",
            expectRows: true);
    }

    [Fact]
    [Trait("Category", "Live")]
    public void Live_Pivot_ParsesAndExecutes()
    {
        if (!RequireLive()) return;
        var sql = $"""
            SELECT * FROM (
                SELECT DEPT_ID, NAME, SALARY FROM {_fx.QualifiedTable}
            ) PIVOT (
                SUM(SALARY) FOR NAME IN ('Alice' AS alice, 'Bob' AS bob)
            )
            """;
        AssertParsesAndExecutes(sql, expectRows: true);
    }

    [Fact]
    [Trait("Category", "Live")]
    public void Live_HierarchicalQuery_ParsesAndExecutes()
    {
        if (!RequireLive()) return;
        var sql = $"""
            SELECT ID, DEPT_ID
            FROM {_fx.QualifiedTable}
            START WITH DEPT_ID = 10
            CONNECT BY PRIOR ID = DEPT_ID
            ORDER SIBLINGS BY ID
            """;
        var (statements, errors) = ParseOracle(sql);
        Assert.Empty(errors);
        Assert.NotEmpty(statements);
        _ = OracleLiveTestHost.ExecuteScalar(_fx.Connection!, sql);
    }

    [Fact]
    [Trait("Category", "Live")]
    public void Live_QQuoteUpdate_ParsesAndExecutes()
    {
        if (!RequireLive()) return;
        AssertParsesAndExecutes($"""
            UPDATE {_fx.QualifiedTable}
            SET NOTE = q'[x; y]'
            WHERE ID = 1
            """);
        OracleLiveTestHost.Execute(_fx.Connection!, "COMMIT");
    }

    [Fact]
    [Trait("Category", "Live")]
    public void Live_AnonymousBlockWithBind_ParsesAndExecutes()
    {
        if (!RequireLive()) return;
        AssertParsesAndExecutes($"""
            DECLARE
                v_count NUMBER;
            BEGIN
                SELECT COUNT(*) INTO v_count FROM {_fx.QualifiedTable};
                IF v_count IS NULL THEN
                    NULL;
                END IF;
            END;
            """);
    }

    [Fact]
    [Trait("Category", "Live")]
    public void Live_PackageWithMember_ParsesAndExecutes()
    {
        if (!RequireLive()) return;
        var endName = OracleLiveTestHost.QuoteIdent(_fx.PackageName);
        var spec = $"""
            CREATE OR REPLACE PACKAGE {_fx.QualifiedPackage} AS
                FUNCTION value RETURN NUMBER;
            END {endName};
            """;
        var body = $"""
            CREATE OR REPLACE PACKAGE BODY {_fx.QualifiedPackage} AS
                FUNCTION value RETURN NUMBER IS
                BEGIN
                    RETURN 42;
                END value;
            END {endName};
            """;

        AssertParsesAndExecutes(spec);
        AssertParsesAndExecutes(body);

        var result = OracleLiveTestHost.ExecuteScalar(
            _fx.Connection!,
            $"SELECT {_fx.QualifiedPackage}.value FROM DUAL");
        Assert.Equal(42m, Convert.ToDecimal(result));
    }

    [Fact]
    [Trait("Category", "Live")]
    public void Live_ReturningInto_ParsesAndExecutes()
    {
        if (!RequireLive()) return;
        AssertParsesAndExecutes($"""
            DECLARE
                v_id NUMBER;
            BEGIN
                INSERT INTO {_fx.QualifiedTable} (ID, NAME, DEPT_ID, SALARY, NOTE)
                VALUES (99, 'Temp', 10, 1, 'r')
                RETURNING ID INTO v_id;
                DELETE FROM {_fx.QualifiedTable} WHERE ID = v_id;
            END;
            """);
        OracleLiveTestHost.Execute(_fx.Connection!, "COMMIT");
    }

    [Fact]
    [Trait("Category", "Live")]
    public void Live_Lint_OraRulesAgainstFixture()
    {
        if (!RequireLive()) return;

        var selectStar = LintService.Lint($"SELECT * FROM {_fx.QualifiedTable}", null, SqlDialect.Oracle);
        Assert.Contains(selectStar, d => d.Code == "ORA001");
        Assert.DoesNotContain(selectStar, d => d.Code?.StartsWith("NZ", StringComparison.Ordinal) == true);

        var deleteAll = LintService.Lint($"DELETE FROM {_fx.QualifiedTable}", null, SqlDialect.Oracle);
        Assert.Contains(deleteAll, d => d.Code == "ORA002");

        var updateAll = LintService.Lint(
            $"UPDATE {_fx.QualifiedTable} SET NOTE = 'X'",
            null,
            SqlDialect.Oracle);
        Assert.Contains(updateAll, d => d.Code == "ORA003");

        var rownumOrder = LintService.Lint(
            $"SELECT ID FROM {_fx.QualifiedTable} WHERE ROWNUM <= 10 ORDER BY ID",
            null,
            SqlDialect.Oracle);
        Assert.Contains(rownumOrder, d => d.Code == "ORA004");

        var safeUpdate = LintService.Lint(
            $"UPDATE {_fx.QualifiedTable} SET NOTE = q'[x; y]' WHERE ID = 1",
            null,
            SqlDialect.Oracle);
        Assert.DoesNotContain(safeUpdate, d => d.Code == "ORA003");
    }

    [Fact]
    [Trait("Category", "Live")]
    public void Live_Parser_RejectsNetezzaOnlyGroom()
    {
        if (!RequireLive()) return;
        var (_, errors) = ParseOracle($"GROOM TABLE {_fx.QualifiedTable};");
        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Code == "PAR001");
    }

    [Fact]
    [Trait("Category", "Live")]
    public void Live_SchemaAware_UnknownColumn_ReportsSql004()
    {
        if (!RequireLive()) return;
        var cols = OracleLiveTestHost.ListColumns(_fx.Connection!, _fx.TableName);
        Assert.NotEmpty(cols);

        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo(
            Name: _fx.TableName,
            Schema: _fx.Schema,
            Columns: cols.Select(c => new ColumnInfo(c.Name, DataType: c.DataType)).ToArray()));

        var diagnostics = LintService.Lint(
            $"SELECT BAD_COL_THAT_DOES_NOT_EXIST FROM {_fx.QualifiedTable}",
            schema,
            SqlDialect.Oracle);

        Assert.Contains(diagnostics, d => d.Code == "SQL004");
    }

    [Fact]
    [Trait("Category", "Live")]
    public void Live_DirectOraRegistry_MatchesAuthoringSurface()
    {
        if (!RequireLive()) return;
        var registry = new QualityRuleRegistry(OracleLintRules.AllRules);
        Assert.True(registry.HasRule("ORA001"));
        Assert.True(registry.HasRule("ORA002"));
        Assert.True(registry.HasRule("ORA003"));
        Assert.True(registry.HasRule("ORA004"));
        Assert.False(registry.HasRule("NZ001"));

        var engine = new LintEngine(registry);
        var issues = engine.RunCheapRules(
            $"SELECT * FROM {_fx.QualifiedTable}; DELETE FROM {_fx.QualifiedTable};",
            registry.BuildEffectiveSeverities()).ToList();
        Assert.Contains(issues, i => i.RuleId == "ORA001");
        Assert.Contains(issues, i => i.RuleId == "ORA002");
    }
}
