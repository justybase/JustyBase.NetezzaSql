using MySqlConnector;

namespace JustyBase.NetezzaSql.MySqlLiveTests;

internal static class MySqlLiveTestHost
{
    public static bool TryOpen(out MySqlConnection? connection)
    {
        connection = null;
        var host = Environment.GetEnvironmentVariable("MYSQL_LIVE_TEST_HOST");
        var database = Environment.GetEnvironmentVariable("MYSQL_LIVE_TEST_DATABASE");
        var user = Environment.GetEnvironmentVariable("MYSQL_LIVE_TEST_USER");
        var password = Environment.GetEnvironmentVariable("MYSQL_LIVE_TEST_PASSWORD");
        var port = int.TryParse(Environment.GetEnvironmentVariable("MYSQL_LIVE_TEST_PORT"), out var p) ? p : 3306;
        var required = IsTrue("MYSQL_LIVE_TEST_REQUIRED");
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(database)
            || string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(password))
        {
            const string message = "MySQL live test not executed: set MYSQL_LIVE_TEST_HOST, MYSQL_LIVE_TEST_DATABASE, MYSQL_LIVE_TEST_USER and MYSQL_LIVE_TEST_PASSWORD.";
            if (required) throw new InvalidOperationException(message);
            Console.WriteLine(message);
            return false;
        }

        var connectString = Environment.GetEnvironmentVariable("MYSQL_LIVE_TEST_CONNECT_STRING");
        if (string.IsNullOrWhiteSpace(connectString))
        {
            var builder = new MySqlConnectionStringBuilder
            {
                Server = host, Port = (uint)port, Database = database, UserID = user, Password = password,
                SslMode = MySqlSslMode.Preferred, ConnectionTimeout = 15,
            };
            connectString = builder.ConnectionString;
        }

        try
        {
            var conn = new MySqlConnection(connectString);
            conn.Open();
            connection = conn;
            return true;
        }
        catch (Exception ex) when (!required)
        {
            Console.WriteLine($"MySQL live test soft-skipped (driver/connection): {ex.Message.Trim()}");
            return false;
        }
    }

    public static string QuoteIdent(string name) => "`" + name.Replace("`", "``", StringComparison.Ordinal) + "`";
    public static string Qualify(string database, string table) => $"{QuoteIdent(database)}.{QuoteIdent(table)}";

    public static void Execute(MySqlConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public static object? ExecuteScalar(MySqlConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    public static void TryExecute(MySqlConnection connection, string sql)
    {
        try { Execute(connection, sql); }
        catch (Exception ex) { Console.WriteLine($"MySQL live cleanup soft-failed: {ex.Message.Trim()}"); }
    }

    public static List<string> ListTables(MySqlConnection connection, int limit = 20)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = DATABASE() ORDER BY TABLE_NAME LIMIT {limit}";
        var result = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) result.Add(reader.GetString(0));
        return result;
    }

    private static bool IsTrue(string name) => string.Equals(Environment.GetEnvironmentVariable(name), "true", StringComparison.OrdinalIgnoreCase);
}
