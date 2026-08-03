using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Linter;
using MySqlConnector;

namespace JustyBase.NetezzaSql.MySqlLiveTests;

public sealed class MySqlLiveFixture : IDisposable
{
    public MySqlConnection? Connection { get; }
    public string Database { get; }
    public string TableName { get; }
    public string QualifiedTable { get; }
    public bool Ready { get; }

    public MySqlLiveFixture()
    {
        TableName = $"jb_mysql_lv_{DateTime.UtcNow:yyyyMMddHHmmssfff}";
        Ready = MySqlLiveTestHost.TryOpen(out var connection);
        Connection = connection;
        Database = Ready ? connection!.Database : string.Empty;
        QualifiedTable = Ready ? MySqlLiveTestHost.Qualify(Database, TableName) : string.Empty;
        if (!Ready || Connection is null) return;
        MySqlLiveTestHost.Execute(Connection, $"""
            CREATE TABLE {QualifiedTable} (
                id INT PRIMARY KEY AUTO_INCREMENT,
                name VARCHAR(64) NOT NULL,
                note VARCHAR(200) NULL
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
            """);
        MySqlLiveTestHost.Execute(Connection, $"INSERT INTO {QualifiedTable} (name, note) VALUES ('Alice', 'ok'), ('Bob', 'ok')");
    }

    public void Dispose()
    {
        if (Connection is null) return;
        MySqlLiveTestHost.TryExecute(Connection, $"DROP TABLE IF EXISTS {QualifiedTable}");
        Connection.Dispose();
    }
}

public sealed class MySqlLiveParserLinterTests : IClassFixture<MySqlLiveFixture>
{
    private readonly MySqlLiveFixture _fixture;
    public MySqlLiveParserLinterTests(MySqlLiveFixture fixture) => _fixture = fixture;

    private bool RequireLive()
    {
        if (_fixture.Ready && _fixture.Connection is not null) return true;
        Console.WriteLine("MySQL live test not executed: MYSQL_LIVE_TEST_* not configured or driver unavailable.");
        return false;
    }

    private static IReadOnlyList<ValidationError> ParseErrors(string sql)
    {
        var tokens = DialectRuntime.Tokenize(sql, SqlDialect.MySql).ToArray();
        var parser = DialectRuntime.CreateParser(tokens, SqlDialect.MySql);
        parser.Parse();
        return parser.Errors;
    }

    private void AssertParsesAndExecutes(string sql)
    {
        Assert.Empty(ParseErrors(sql));
        MySqlLiveTestHost.Execute(_fixture.Connection!, sql);
    }

    [Fact, Trait("Category", "Live")]
    public void Live_CanConnectAndSeeSchemaObjects()
    {
        if (!RequireLive()) return;
        var version = Convert.ToString(MySqlLiveTestHost.ExecuteScalar(_fixture.Connection!, "SELECT VERSION()"));
        Assert.False(string.IsNullOrWhiteSpace(version));
        Assert.Contains(_fixture.TableName, MySqlLiveTestHost.ListTables(_fixture.Connection!), StringComparer.OrdinalIgnoreCase);
    }

    [Fact, Trait("Category", "Live")]
    public void Live_SelectIfAndLimitFormsParseAndExecute()
    {
        if (!RequireLive()) return;
        AssertParsesAndExecutes($"SELECT IF(id > 0, 'Y', 'N') FROM {_fixture.QualifiedTable} # comment LIMIT 1");
        AssertParsesAndExecutes($"SELECT id FROM {_fixture.QualifiedTable} LIMIT 0, 1");
        AssertParsesAndExecutes($"SELECT id FROM {_fixture.QualifiedTable} LIMIT 1 OFFSET 0");
    }

    [Fact, Trait("Category", "Live")]
    public void Live_InsertIgnoreDuplicateUpdateParsesAndExecutes()
    {
        if (!RequireLive()) return;
        AssertParsesAndExecutes($"INSERT IGNORE INTO {_fixture.QualifiedTable} (id, name, note) VALUES (1, 'Alice', 'updated') ON DUPLICATE KEY UPDATE note = 'updated'");
        Assert.Equal("updated", Convert.ToString(MySqlLiveTestHost.ExecuteScalar(_fixture.Connection!, $"SELECT note FROM {_fixture.QualifiedTable} WHERE id = 1")));
    }

    [Fact, Trait("Category", "Live")]
    public void Live_MySqlDdlTypesAndOptionsParseAndExecute()
    {
        if (!RequireLive()) return;
        var table = MySqlLiveTestHost.QuoteIdent($"{_fixture.TableName}_ddl");
        try
        {
            AssertParsesAndExecutes($"CREATE TABLE {table} (id INT PRIMARY KEY AUTO_INCREMENT, flags SET('a','b'), payload JSON, CHECK (payload IS NOT NULL)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
        }
        finally { MySqlLiveTestHost.TryExecute(_fixture.Connection!, $"DROP TABLE IF EXISTS {table}"); }
    }

    [Fact, Trait("Category", "Live")]
    public void Live_MySqlLintProfileDoesNotApplyOtherDialectRules()
    {
        if (!RequireLive()) return;
        var registry = DialectRuntime.QualityRules(SqlDialect.MySql);
        Assert.Empty(registry.AllRules);
    }
}
