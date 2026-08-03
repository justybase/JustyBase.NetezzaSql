using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Ast;
using Npgsql;

namespace JustyBase.NetezzaSql.PostgreSqlLiveTests;

public sealed class PostgreSqlLiveFixture : IDisposable
{
    public NpgsqlConnection? Connection { get; }
    public string TableName { get; } = $"jb_pg_live_{DateTime.UtcNow:yyyyMMddHHmmssfff}";
    public bool Ready { get; }

    public PostgreSqlLiveFixture()
    {
        if (!PostgreSqlLiveHost.TryOpen(out var connection)) return;
        Connection = connection;
        Ready = true;
        PostgreSqlLiveHost.Execute(Connection!, $"CREATE TEMP TABLE {TableName} (id integer PRIMARY KEY, payload jsonb NOT NULL, tags integer[] NOT NULL)");
        PostgreSqlLiveHost.Execute(Connection!, $"INSERT INTO {TableName} (id, payload, tags) VALUES (1, '{{\"name\":\"Alice\"}}', ARRAY[1,2]), (2, '{{\"name\":\"Bob\"}}', ARRAY[2,3])");
    }

    public void Dispose()
    {
        if (Connection is null) return;
        PostgreSqlLiveHost.TryExecute(Connection, $"DROP TABLE IF EXISTS {TableName}");
        Connection.Dispose();
    }
}

public sealed class PostgreSqlLiveParserTests : IClassFixture<PostgreSqlLiveFixture>
{
    private readonly PostgreSqlLiveFixture _fixture;
    public PostgreSqlLiveParserTests(PostgreSqlLiveFixture fixture) => _fixture = fixture;

    private bool RequireLive()
    {
        if (_fixture.Ready && _fixture.Connection is not null) return true;
        Console.WriteLine("PostgreSQL live test not executed: POSTGRES_LIVE_TEST_* not configured or driver unavailable.");
        return false;
    }

    private static IReadOnlyList<ValidationError> ParseErrors(string sql)
    {
        var tokens = DialectRuntime.Tokenize(sql, SqlDialect.PostgreSql).ToArray();
        var parser = DialectRuntime.CreateParser(tokens, SqlDialect.PostgreSql);
        parser.Parse();
        return parser.Errors;
    }

    private void AssertParsesAndExecutes(string sql)
    {
        Assert.Empty(ParseErrors(sql));
        PostgreSqlLiveHost.Execute(_fixture.Connection!, sql);
    }

    [Fact, Trait("Category", "Live")]
    public void Live_ConnectsAndReadsTemporaryTable()
    {
        if (!RequireLive()) return;
        Assert.Equal(2L, Convert.ToInt64(PostgreSqlLiveHost.Scalar(_fixture.Connection!, $"SELECT count(*) FROM {_fixture.TableName}")));
    }

    [Fact, Trait("Category", "Live")]
    public void Live_PostgreSqlQueriesParseAndExecute()
    {
        if (!RequireLive()) return;
        AssertParsesAndExecutes($"SELECT DISTINCT ON (id) id, payload->>'name' FROM {_fixture.TableName} ORDER BY id");
        AssertParsesAndExecutes($"SELECT t.id FROM {_fixture.TableName} t CROSS JOIN LATERAL (SELECT t.id) x");
        AssertParsesAndExecutes($"SELECT ARRAY[1, 2]::integer[] FROM {_fixture.TableName}");
        AssertParsesAndExecutes($"SELECT payload #>> '{{name}}' FROM {_fixture.TableName}");
    }

    [Fact, Trait("Category", "Live")]
    public void Live_DmlReturningAndConflictParseAndExecute()
    {
        if (!RequireLive()) return;
        AssertParsesAndExecutes($"INSERT INTO {_fixture.TableName} (id, payload, tags) VALUES (1, '{{}}', ARRAY[9]) ON CONFLICT (id) DO NOTHING RETURNING id");
        AssertParsesAndExecutes($"UPDATE {_fixture.TableName} SET payload = '{{}}' WHERE id = 1 RETURNING id");
        AssertParsesAndExecutes($"DELETE FROM {_fixture.TableName} WHERE id = -1 RETURNING id");
    }

    [Fact, Trait("Category", "Live")]
    public void Live_RejectsNetezzaSyntax()
    {
        if (!RequireLive()) return;
        Assert.NotEmpty(ParseErrors($"SELECT * FROM {_fixture.TableName} DISTRIBUTE ON (id)"));
        Assert.NotEmpty(ParseErrors($"SELECT * FROM db..{_fixture.TableName}"));
    }
}

internal static class PostgreSqlLiveHost
{
    public static bool TryOpen(out NpgsqlConnection? connection)
    {
        connection = null;
        var connectString = Environment.GetEnvironmentVariable("POSTGRES_LIVE_TEST_CONNECT_STRING");
        var host = Environment.GetEnvironmentVariable("POSTGRES_LIVE_TEST_HOST");
        var database = Environment.GetEnvironmentVariable("POSTGRES_LIVE_TEST_DATABASE");
        var user = Environment.GetEnvironmentVariable("POSTGRES_LIVE_TEST_USER");
        var password = Environment.GetEnvironmentVariable("POSTGRES_LIVE_TEST_PASSWORD");
        if (string.IsNullOrWhiteSpace(connectString) &&
            (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(database) || string.IsNullOrWhiteSpace(user) || password is null))
            return false;

        try
        {
            if (string.IsNullOrWhiteSpace(connectString))
            {
                var builder = new NpgsqlConnectionStringBuilder
                {
                    Host = host!,
                    Port = int.TryParse(Environment.GetEnvironmentVariable("POSTGRES_LIVE_TEST_PORT"), out var port) ? port : 5432,
                    Database = database!, Username = user!, Password = password!,
                };
                connectString = builder.ConnectionString;
            }
            connection = new NpgsqlConnection(connectString);
            connection.Open();
            return true;
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException)
        {
            connection?.Dispose();
            connection = null;
            return false;
        }
    }

    public static void Execute(NpgsqlConnection connection, string sql)
    {
        using var command = new NpgsqlCommand(sql, connection);
        command.ExecuteNonQuery();
    }

    public static object? Scalar(NpgsqlConnection connection, string sql)
    {
        using var command = new NpgsqlCommand(sql, connection);
        return command.ExecuteScalar();
    }

    public static bool TryExecute(NpgsqlConnection connection, string sql)
    {
        try { Execute(connection, sql); return true; }
        catch (NpgsqlException) { return false; }
    }
}
