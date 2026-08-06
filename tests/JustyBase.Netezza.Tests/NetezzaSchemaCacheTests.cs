using JustyBase.Netezza.Models;
using JustyBase.Netezza.Schema;

namespace JustyBase.Netezza.Tests;

public sealed class NetezzaSchemaCacheTests
{
    private static NetezzaSchemaSnapshot SnapshotWith(params string[] tables)
        => new(tables.Select(name => new NetezzaSchemaTable(name)).ToArray(), Version: 1);

    [Fact]
    public void Put_ThenTryGet_ReturnsSnapshot()
    {
        var cache = new NetezzaSchemaCache();
        var snapshot = SnapshotWith("T1");

        cache.Put("CONN1", "SALES", snapshot);

        Assert.True(cache.TryGet("CONN1", "SALES", out var actual));
        Assert.Same(snapshot, actual);
    }

    [Fact]
    public void TryGet_UnknownKey_ReturnsFalse()
    {
        var cache = new NetezzaSchemaCache();

        Assert.False(cache.TryGet("CONN1", "SALES", out _));
    }

    [Fact]
    public async Task TryGet_ExpiredEntry_ReturnsFalse()
    {
        var cache = new NetezzaSchemaCache(TimeSpan.FromMilliseconds(50));
        cache.Put("CONN1", "SALES", SnapshotWith("T1"));

        await Task.Delay(120);

        Assert.False(cache.TryGet("CONN1", "SALES", out _));
    }

    [Fact]
    public void Keys_AreCaseInsensitive()
    {
        var cache = new NetezzaSchemaCache();
        cache.Put("CONN1", "sales", SnapshotWith("T1"));

        Assert.True(cache.TryGet("conn1", "SALES", out _));
    }

    [Fact]
    public void Remove_DeletesSingleDatabase()
    {
        var cache = new NetezzaSchemaCache();
        cache.Put("CONN1", "SALES", SnapshotWith("T1"));
        cache.Put("CONN1", "SYSTEM", SnapshotWith("S1"));

        cache.Remove("CONN1", "SALES");

        Assert.False(cache.TryGet("CONN1", "SALES", out _));
        Assert.True(cache.TryGet("CONN1", "SYSTEM", out _));
    }

    [Fact]
    public void RemoveConnection_DeletesAllDatabases()
    {
        var cache = new NetezzaSchemaCache();
        cache.Put("CONN1", "SALES", SnapshotWith("T1"));
        cache.Put("CONN1", "SYSTEM", SnapshotWith("S1"));
        cache.Put("CONN2", "SALES", SnapshotWith("T2"));

        cache.RemoveConnection("CONN1");

        Assert.False(cache.TryGet("CONN1", "SALES", out _));
        Assert.True(cache.TryGet("CONN2", "SALES", out _));
    }

    [Fact]
    public async Task GetFresh_ExcludesExpiredEntries()
    {
        var cache = new NetezzaSchemaCache(TimeSpan.FromMilliseconds(50));
        cache.Put("CONN1", "SALES", SnapshotWith("T1"));

        await Task.Delay(120);

        Assert.Empty(cache.GetFresh("CONN1"));
    }

    [Fact]
    public void Generation_IncrementsOnMutations()
    {
        var cache = new NetezzaSchemaCache();
        int initial = cache.Generation;

        cache.Put("CONN1", "SALES", SnapshotWith("T1"));
        Assert.Equal(initial + 1, cache.Generation);

        cache.Put("CONN1", "SALES", SnapshotWith("T1"));
        Assert.Equal(initial + 2, cache.Generation);

        cache.Remove("CONN1", "SALES");
        Assert.Equal(initial + 3, cache.Generation);

        cache.Put("CONN2", "SALES", SnapshotWith("T2"));
        cache.RemoveConnection("CONN2");
        Assert.Equal(initial + 5, cache.Generation);

        cache.Put("CONN3", "SALES", SnapshotWith("T3"));
        cache.Clear();
        Assert.Equal(initial + 7, cache.Generation);

        int afterClear = cache.Generation;
        cache.Clear();
        Assert.Equal(afterClear, cache.Generation);
    }

    [Fact]
    public void Parallel_Stress_PutAndTryGet()
    {
        var cache = new NetezzaSchemaCache(TimeSpan.FromMinutes(30));

        Parallel.For(0, 1000, i =>
        {
            cache.Put("CONN1", $"DB{i % 50}", SnapshotWith($"T{i}"));
        });

        int hits = 0;
        Parallel.For(0, 1000, i =>
        {
            if (cache.TryGet("CONN1", $"DB{i % 50}", out var snapshot))
            {
                hits++;
            }
        });

        Assert.Equal(1000, hits);
    }
}
