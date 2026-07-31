using IBM.Data.Db2;

namespace JustyBase.NetezzaSql.Db2LiveTests;

/// <summary>
/// Environment-gated Db2 LUW live connection helpers (<c>DB2_LIVE_TEST_*</c>).
/// Soft-skips when required variables are missing or the native driver fails;
/// fails when <c>DB2_LIVE_TEST_REQUIRED=true</c> and configuration is incomplete.
/// </summary>
internal static class Db2LiveTestHost
{
    public static bool TryOpen(out DB2Connection? connection, out string schema)
    {
        schema = string.Empty;
        connection = null;

        var host = Environment.GetEnvironmentVariable("DB2_LIVE_TEST_HOST");
        var database = Environment.GetEnvironmentVariable("DB2_LIVE_TEST_DATABASE");
        var user = Environment.GetEnvironmentVariable("DB2_LIVE_TEST_USER");
        var password = Environment.GetEnvironmentVariable("DB2_LIVE_TEST_PASSWORD");
        var portText = Environment.GetEnvironmentVariable("DB2_LIVE_TEST_PORT");
        var port = int.TryParse(portText, out var parsedPort) ? parsedPort : 50000;
        var required = string.Equals(
            Environment.GetEnvironmentVariable("DB2_LIVE_TEST_REQUIRED"),
            "true",
            StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(host)
            || string.IsNullOrWhiteSpace(database)
            || string.IsNullOrWhiteSpace(user)
            || string.IsNullOrWhiteSpace(password))
        {
            var message =
                "Db2 live test not executed: set DB2_LIVE_TEST_HOST, DB2_LIVE_TEST_DATABASE, DB2_LIVE_TEST_USER and DB2_LIVE_TEST_PASSWORD.";
            if (required)
                throw new InvalidOperationException(message);
            Console.WriteLine(message);
            return false;
        }

        var connectString = Environment.GetEnvironmentVariable("DB2_LIVE_TEST_CONNECT_STRING");
        if (string.IsNullOrWhiteSpace(connectString))
        {
            connectString =
                $"Server={host}:{port};Database={database};UID={user};PWD={password};";
        }

        try
        {
            var conn = new DB2Connection(connectString);
            conn.Open();

            var currentSchema = Environment.GetEnvironmentVariable("DB2_LIVE_TEST_CURRENT_SCHEMA")
                ?? Environment.GetEnvironmentVariable("DB2_LIVE_TEST_SCHEMA");
            if (!string.IsNullOrWhiteSpace(currentSchema))
            {
                using var alter = conn.CreateCommand();
                alter.CommandText = $"SET CURRENT SCHEMA = {QuoteIdent(currentSchema)}";
                alter.ExecuteNonQuery();
                schema = currentSchema.Trim().ToUpperInvariant();
            }
            else
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT CURRENT SCHEMA FROM SYSIBM.SYSDUMMY1";
                schema = Convert.ToString(cmd.ExecuteScalar())?.Trim().ToUpperInvariant()
                    ?? user.ToUpperInvariant();
            }

            connection = conn;
            return true;
        }
        catch (Exception ex) when (!required)
        {
            Console.WriteLine($"Db2 live test soft-skipped (driver/connection): {ex.Message.Trim()}");
            return false;
        }
    }

    public static string QuoteIdent(string name) =>
        "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    public static string Qualify(string schema, string name) =>
        $"{QuoteIdent(schema)}.{QuoteIdent(name)}";

    public static void Execute(DB2Connection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public static object? ExecuteScalar(DB2Connection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }

    public static void TryExecute(DB2Connection connection, string sql)
    {
        try
        {
            Execute(connection, sql);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Db2 live cleanup soft-failed: {ex.Message.Trim()}");
        }
    }

    public static List<string> ListSchemaTables(DB2Connection connection, string schema, int limit = 20)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT TABNAME
            FROM SYSCAT.TABLES
            WHERE TABSCHEMA = '{schema.Replace("'", "''", StringComparison.Ordinal)}'
              AND TYPE = 'T'
            ORDER BY TABNAME
            FETCH FIRST {limit} ROWS ONLY
            """;
        var tables = new List<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            tables.Add(reader.GetString(0));
        return tables;
    }
}
