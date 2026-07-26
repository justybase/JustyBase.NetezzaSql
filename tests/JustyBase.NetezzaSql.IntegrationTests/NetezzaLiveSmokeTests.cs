using JustyBase.NetezzaDriver;
using JustyBase.NetezzaDdl;
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

    [Fact]
    [Trait("Category", "Live")]
    public void SharedUsingOptions_are_accepted_by_live_netezza()
    {
        var host = Environment.GetEnvironmentVariable("NZ_DEV_HOST");
        var database = Environment.GetEnvironmentVariable("NZ_DEV_DATABASE");
        var user = Environment.GetEnvironmentVariable("NZ_DEV_USER");
        var password = Environment.GetEnvironmentVariable("NZ_DEV_PASSWORD");
        var port = int.TryParse(Environment.GetEnvironmentVariable("NZ_DEV_PORT"), out var parsedPort)
            ? parsedPort
            : 5480;

        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(database)
            || string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(password))
        {
            Console.WriteLine("Live USING test not executed: NZ_DEV_* variables are incomplete.");
            return;
        }

        string usingClause = NetezzaImportSql.BuildUsingClause(new NetezzaImportUsingOptions
        {
            Delimiter = ",",
            NullValue = string.Empty,
            MaxRows = 0,
            MaxErrors = 0
        });

        Assert.Contains("NULLVALUE ''", usingClause, StringComparison.Ordinal);
        Assert.Contains("MAXERRORS 0", usingClause, StringComparison.Ordinal);
        Assert.Contains("REMOTESOURCE 'dotnet'", usingClause, StringComparison.Ordinal);
        Assert.DoesNotContain("MAXROWS", usingClause, StringComparison.Ordinal);

        string emptyFile = Path.Combine(Path.GetTempPath(), "jb_nz_using_" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(emptyFile, string.Empty);
        try
        {
            // REMOTESOURCE 'dotnet' resolves DATAOBJECT on the client (Windows path).
            string dataObject = emptyFile.Replace("\\", "\\\\", StringComparison.Ordinal);
            using var connection = new NzConnection(user, password, host, database, port);
            connection.Open();
            using var command = connection.CreateCommand(
                $"SELECT COUNT(*) FROM EXTERNAL '{dataObject}' (ID INTEGER) {usingClause};");
            Assert.Equal(0L, Convert.ToInt64(command.ExecuteScalar()));
        }
        finally
        {
            try { File.Delete(emptyFile); } catch { /* best-effort */ }
        }
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
