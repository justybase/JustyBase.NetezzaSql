using JustyBase.NetezzaDriver;
using CatalogSql = JustyBase.NetezzaCatalogSql.NetezzaCatalogSql;

namespace JustyBase.NetezzaSql.IntegrationTests;

/// <summary>
/// Optional live checks using the repository's Netezza driver. They never
/// open a connection in the normal offline test suite.
/// </summary>
public sealed class NetezzaLiveSmokeTests
{
    [Fact]
    [Trait("Category", "Live")]
    public void ConfiguredConnection_ExecutesSmokeAndMetadataQueries()
    {
        var host = Environment.GetEnvironmentVariable("NZ_DEV_HOST");
        var database = Environment.GetEnvironmentVariable("NZ_DEV_DATABASE");
        var user = Environment.GetEnvironmentVariable("NZ_DEV_USER");
        var password = Environment.GetEnvironmentVariable("NZ_DEV_PASSWORD");
        var port = int.TryParse(Environment.GetEnvironmentVariable("NZ_DEV_PORT"), out var parsedPort)
            ? parsedPort
            : 5480;

        if (string.IsNullOrWhiteSpace(host) ||
            string.IsNullOrWhiteSpace(database) ||
            string.IsNullOrWhiteSpace(user) ||
            string.IsNullOrWhiteSpace(password))
        {
            Console.WriteLine(
                "Live test not executed: set NZ_DEV_HOST, NZ_DEV_DATABASE, NZ_DEV_USER and NZ_DEV_PASSWORD.");
            return;
        }

        using var connection = new NzConnection(user, password, host, database, port);
        connection.Open();

        using var smoke = connection.CreateCommand("SELECT 1");
        Assert.Equal(1, Convert.ToInt32(smoke.ExecuteScalar()));
        Assert.True(ExecuteReader(connection, CatalogSql.GetSchemasSql(database)) >= 0);
        Assert.True(ExecuteReader(connection, CatalogSql.GetObjectTypesSql(database)) >= 0);
    }

    private static int ExecuteReader(NzConnection connection, string sql)
    {
        using var command = connection.CreateCommand(sql);
        using var reader = command.ExecuteReader();
        int rows = 0;
        while (reader.Read())
            rows++;
        return rows;
    }
}
