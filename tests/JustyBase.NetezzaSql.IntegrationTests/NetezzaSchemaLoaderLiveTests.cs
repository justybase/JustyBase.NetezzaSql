using JustyBase.Netezza.Models;
using JustyBase.Netezza.Schema;
using JustyBase.NetezzaDriver;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.NetezzaSql.IntegrationTests;

/// <summary>
/// Live catalog loader tests gated by NZ_DEV_* environment variables (soft-skip when absent),
/// following the <see cref="NetezzaLiveTestHost"/> convention.
/// </summary>
public sealed class NetezzaSchemaLoaderLiveTests
{
    private static async Task<NetezzaSchemaTable> WaitForTableAsync(
        NzConnection conn,
        string tableName,
        NetezzaCatalogLoadOptions options)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            var snapshot = await NetezzaSchemaLoader.LoadCatalogAsync(conn, conn.Database, options);
            var table = snapshot.Tables.FirstOrDefault(t =>
                t.Name.Equals(tableName, StringComparison.OrdinalIgnoreCase));
            if (table is not null)
            {
                return table;
            }

            await Task.Delay(500);
        }

        throw new TimeoutException($"Timed out waiting for catalog entry of {tableName}.");
    }

    [Fact]
    public async Task LoadCatalog_LiveDatabase_ReturnsCreatedTableWithColumns()
    {
        if (!NetezzaLiveTestHost.TryCreateConnection(out var connection))
        {
            return;
        }

        using var conn = connection!;
        conn.Open();
        string tableName = $"TEMP_JB_LOADER_{Guid.NewGuid():N}".ToUpperInvariant();
        try
        {
            NetezzaLiveTestHost.Execute(
                conn,
                $"CREATE TABLE {tableName} (ID INTEGER NOT NULL, NAME VARCHAR(20), PRICE DECIMAL(10,2) DEFAULT 0.0)");

            var table = await WaitForTableAsync(
                conn,
                tableName,
                new NetezzaCatalogLoadOptions { LazyColumnThreshold = int.MaxValue });

            Assert.Equal(NetezzaObjectKind.Table, table.Kind);
            Assert.NotNull(table.Columns);
            Assert.Equal(3, table.Columns!.Count);
            Assert.Equal("ID", table.Columns![0].Name);
            Assert.False(table.Columns[0].Nullable);
            Assert.Equal("NAME", table.Columns[1].Name);
            Assert.True(table.Columns[1].Nullable);
            Assert.Equal("PRICE", table.Columns[2].Name);
            var columns = table.Columns!;
            string priceType = columns[2].DataType ?? string.Empty;
            Assert.True(
                priceType.Contains("DECIMAL", StringComparison.OrdinalIgnoreCase)
                || priceType.Contains("NUMERIC", StringComparison.OrdinalIgnoreCase),
                $"unexpected type: {priceType}");
        }
        finally
        {
            NetezzaLiveTestHost.TryDrop(conn, tableName);
            conn.Close();
        }
    }

    [Fact]
    public async Task LoadCatalog_LiveLazyMode_ThenHydrateColumns()
    {
        if (!NetezzaLiveTestHost.TryCreateConnection(out var connection))
        {
            return;
        }

        using var conn = connection!;
        conn.Open();
        string tableName = $"TEMP_JB_LAZY_{Guid.NewGuid():N}".ToUpperInvariant();
        try
        {
            NetezzaLiveTestHost.Execute(conn, $"CREATE TABLE {tableName} (A INTEGER, B VARCHAR(5))");

            var table = await WaitForTableAsync(
                conn,
                tableName,
                new NetezzaCatalogLoadOptions { LazyColumnThreshold = 1 });
            Assert.Null(table.Columns);

            var columns = await NetezzaSchemaLoader.HydrateColumnsAsync(
                conn,
                conn.Database,
                table.Schema!,
                table.Name);

            Assert.Equal(2, columns.Count);
            Assert.Equal("A", columns[0].Name);
        }
        finally
        {
            NetezzaLiveTestHost.TryDrop(conn, tableName);
            conn.Close();
        }
    }

    [Fact]
    public async Task LoadDatabases_LiveConnection_ReturnsDatabaseList()
    {
        if (!NetezzaLiveTestHost.TryCreateConnection(out var connection))
        {
            return;
        }

        using var conn = connection!;
        conn.Open();
        try
        {
            var databases = await NetezzaSchemaLoader.LoadDatabasesAsync(conn);

            Assert.NotEmpty(databases);
            Assert.Contains(databases, d => d.Name.Equals(conn.Database, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            conn.Close();
        }
    }

    [Fact]
    public async Task LoadCatalog_Live_CompletionProviderSeesTable()
    {
        if (!NetezzaLiveTestHost.TryCreateConnection(out var connection))
        {
            return;
        }

        using var conn = connection!;
        conn.Open();
        string tableName = $"TEMP_JB_COMP_{Guid.NewGuid():N}".ToUpperInvariant();
        try
        {
            NetezzaLiveTestHost.Execute(conn, $"CREATE TABLE {tableName} (ID INTEGER)");

            var table = await WaitForTableAsync(
                conn,
                tableName,
                new NetezzaCatalogLoadOptions { LazyColumnThreshold = int.MaxValue });

            var snapshot = await NetezzaSchemaLoader.LoadCatalogAsync(
                conn,
                conn.Database,
                new NetezzaCatalogLoadOptions { LazyColumnThreshold = int.MaxValue });
            var provider = new InMemorySchemaProvider();
            NetezzaSchemaProviderAdapter.Apply(provider, snapshot);

            Assert.True(provider.TableExists(conn.Database, table.Schema, table.Name));
            Assert.Single(provider.GetTable(conn.Database, table.Schema, table.Name)!.Columns!);
        }
        finally
        {
            NetezzaLiveTestHost.TryDrop(conn, tableName);
            conn.Close();
        }
    }

    [Fact]
    public void SchemaCache_LivePutTryGetWithShortTtl()
    {
        var cache = new NetezzaSchemaCache(TimeSpan.FromSeconds(1));
        var snapshot = new NetezzaSchemaSnapshot([new NetezzaSchemaTable("X", "PUBLIC", "DB")]);

        cache.Put("CONN", "DB", snapshot);
        Assert.True(cache.TryGet("CONN", "DB", out _));

        Thread.Sleep(1_500);
        Assert.False(cache.TryGet("CONN", "DB", out _));
    }
}
