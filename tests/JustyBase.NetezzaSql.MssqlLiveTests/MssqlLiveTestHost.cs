using Microsoft.Data.SqlClient;

namespace JustyBase.NetezzaSql.MssqlLiveTests;

/// <summary>
/// Environment-gated SQL Server live connection helpers (<c>MSSQL_LIVE_TEST_*</c>).
/// Soft-skips when required variables are missing or the driver cannot connect;
/// fails when <c>MSSQL_LIVE_TEST_REQUIRED=true</c> and configuration is incomplete.
/// </summary>
internal static class MssqlLiveTestHost
{
    public static bool TryOpen(out SqlConnection? connection)
    {
        connection = null;

        var host = Environment.GetEnvironmentVariable("MSSQL_LIVE_TEST_HOST");
        var database = Environment.GetEnvironmentVariable("MSSQL_LIVE_TEST_DATABASE");
        var user = Environment.GetEnvironmentVariable("MSSQL_LIVE_TEST_USER");
        var password = Environment.GetEnvironmentVariable("MSSQL_LIVE_TEST_PASSWORD");
        var portText = Environment.GetEnvironmentVariable("MSSQL_LIVE_TEST_PORT");
        var port = int.TryParse(portText, out var parsedPort) ? parsedPort : 1433;
        var required = string.Equals(
            Environment.GetEnvironmentVariable("MSSQL_LIVE_TEST_REQUIRED"),
            "true",
            StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(host)
            || string.IsNullOrWhiteSpace(database)
            || string.IsNullOrWhiteSpace(user)
            || string.IsNullOrWhiteSpace(password))
        {
            var message =
                "MSSQL live test not executed: set MSSQL_LIVE_TEST_HOST, MSSQL_LIVE_TEST_DATABASE, MSSQL_LIVE_TEST_USER and MSSQL_LIVE_TEST_PASSWORD.";
            if (required)
                throw new InvalidOperationException(message);
            Console.WriteLine(message);
            return false;
        }

        var connectString = Environment.GetEnvironmentVariable("MSSQL_LIVE_TEST_CONNECT_STRING");
        if (string.IsNullOrWhiteSpace(connectString))
        {
            var builder = new SqlConnectionStringBuilder
            {
                DataSource = $"{host},{port}",
                InitialCatalog = database,
                UserID = user,
                Password = password,
                Encrypt = true,
                TrustServerCertificate = true,
                ConnectTimeout = 15,
            };
            connectString = builder.ConnectionString;
        }

        try
        {
            var conn = new SqlConnection(connectString);
            conn.Open();
            connection = conn;
            return true;
        }
        catch (Exception ex) when (!required)
        {
            Console.WriteLine($"MSSQL live test soft-skipped (driver/connection): {ex.Message.Trim()}");
            return false;
        }
    }

    public static string QuoteIdent(string name) =>
        "[" + name.Replace("]", "]]", StringComparison.Ordinal) + "]";

    public static string Qualify(string schema, string name) =>
        $"{QuoteIdent(schema)}.{QuoteIdent(name)}";

    public static void Execute(SqlConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public static object? ExecuteScalar(SqlConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }

    public static void TryExecute(SqlConnection connection, string sql)
    {
        try
        {
            Execute(connection, sql);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"MSSQL live cleanup soft-failed: {ex.Message.Trim()}");
        }
    }

    public static List<string> ListSchemaTables(SqlConnection connection, int limit = 20)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT TOP {limit} TABLE_NAME
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_TYPE = 'BASE TABLE'
            ORDER BY TABLE_NAME
            """;
        var tables = new List<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            tables.Add(reader.GetString(0));
        return tables;
    }
}
