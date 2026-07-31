using Oracle.ManagedDataAccess.Client;

namespace JustyBase.NetezzaSql.IntegrationTests;

/// <summary>
/// Environment-gated Oracle live connection helpers (<c>ORACLE_LIVE_TEST_*</c>).
/// Soft-skips when required variables are missing; fails when
/// <c>ORACLE_LIVE_TEST_REQUIRED=true</c> and configuration is incomplete.
/// </summary>
internal static class OracleLiveTestHost
{
    public static bool TryOpen(out OracleConnection? connection, out string schema)
    {
        schema = string.Empty;
        connection = null;

        var host = Environment.GetEnvironmentVariable("ORACLE_LIVE_TEST_HOST");
        var database = Environment.GetEnvironmentVariable("ORACLE_LIVE_TEST_DATABASE");
        var user = Environment.GetEnvironmentVariable("ORACLE_LIVE_TEST_USER");
        var password = Environment.GetEnvironmentVariable("ORACLE_LIVE_TEST_PASSWORD");
        var portText = Environment.GetEnvironmentVariable("ORACLE_LIVE_TEST_PORT");
        var port = int.TryParse(portText, out var parsedPort) ? parsedPort : 1521;
        var required = string.Equals(
            Environment.GetEnvironmentVariable("ORACLE_LIVE_TEST_REQUIRED"),
            "true",
            StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(host)
            || string.IsNullOrWhiteSpace(database)
            || string.IsNullOrWhiteSpace(user)
            || string.IsNullOrWhiteSpace(password))
        {
            var message =
                "Oracle live test not executed: set ORACLE_LIVE_TEST_HOST, ORACLE_LIVE_TEST_DATABASE, ORACLE_LIVE_TEST_USER and ORACLE_LIVE_TEST_PASSWORD.";
            if (required)
                throw new InvalidOperationException(message);
            Console.WriteLine(message);
            return false;
        }

        var connectString = Environment.GetEnvironmentVariable("ORACLE_LIVE_TEST_CONNECT_STRING");
        if (string.IsNullOrWhiteSpace(connectString))
            connectString = $"{host}:{port}/{database}";

        var builder = new OracleConnectionStringBuilder
        {
            UserID = user,
            Password = password,
            DataSource = connectString,
        };

        var timeoutText = Environment.GetEnvironmentVariable("ORACLE_LIVE_TEST_CONNECT_TIMEOUT");
        if (int.TryParse(timeoutText, out var timeoutSeconds) && timeoutSeconds > 0)
            builder.ConnectionTimeout = timeoutSeconds;

        var conn = new OracleConnection(builder.ConnectionString);
        conn.Open();

        var currentSchema = Environment.GetEnvironmentVariable("ORACLE_LIVE_TEST_CURRENT_SCHEMA");
        if (!string.IsNullOrWhiteSpace(currentSchema))
        {
            using var alter = conn.CreateCommand();
            alter.CommandText = $"ALTER SESSION SET CURRENT_SCHEMA = {QuoteIdent(currentSchema)}";
            alter.ExecuteNonQuery();
            schema = currentSchema.Trim().ToUpperInvariant();
        }
        else
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA') FROM DUAL";
            schema = Convert.ToString(cmd.ExecuteScalar())?.ToUpperInvariant() ?? user.ToUpperInvariant();
        }

        connection = conn;
        return true;
    }

    public static string QuoteIdent(string name) =>
        "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    public static string Qualify(string schema, string name) =>
        $"{QuoteIdent(schema)}.{QuoteIdent(name)}";

    public static void Execute(OracleConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.BindByName = true;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public static object? ExecuteScalar(OracleConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.BindByName = true;
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }

    public static void TryExecute(OracleConnection connection, string sql)
    {
        try
        {
            Execute(connection, sql);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Oracle live cleanup soft-failed: {ex.Message.Trim()}");
        }
    }

    public static List<string> ListUserTables(OracleConnection connection, int limit = 20)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT table_name
            FROM user_tables
            WHERE table_name NOT LIKE 'BIN$%'
            ORDER BY table_name
            FETCH FIRST {limit} ROWS ONLY
            """;
        var names = new List<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            names.Add(reader.GetString(0));
        return names;
    }

    public static List<(string Name, string? DataType)> ListColumns(
        OracleConnection connection,
        string tableName)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT column_name, data_type
            FROM user_tab_columns
            WHERE table_name = :t
            ORDER BY column_id
            """;
        cmd.Parameters.Add("t", OracleDbType.Varchar2).Value = tableName.ToUpperInvariant();
        var cols = new List<(string, string?)>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            cols.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1)));
        return cols;
    }
}
