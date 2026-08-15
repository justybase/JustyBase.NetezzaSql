using JustyBase.Sqlite.Models;
using JustyBase.Sqlite.Schema;

namespace JustyBase.Sqlite.Tests;

public sealed class SqliteSchemaCacheTests
{
    private static SqliteSchemaSnapshot Snapshot() => new(
        [new SqliteSchemaTable("t", "main", Columns: [new SqliteSchemaColumn("id", "INTEGER")])],
        Version: 1,
        LoadedAt: DateTimeOffset.UtcNow);

    [Fact]
    public void PutThenGet_ReturnsSnapshot()
    {
        var cache = new SqliteSchemaCache(TimeSpan.FromMinutes(10));
        cache.Put("c1", "main", Snapshot());
        Assert.True(cache.TryGet("c1", "main", out var snapshot));
        Assert.Equal("t", snapshot.Tables[0].Name);
    }

    [Fact]
    public void ExpiredEntry_IsEvicted()
    {
        var cache = new SqliteSchemaCache(TimeSpan.FromMilliseconds(50));
        cache.Put("c1", "main", Snapshot());
        Thread.Sleep(80);
        Assert.False(cache.TryGet("c1", "main", out _));
    }

    [Fact]
    public void Generation_BumpsOnMutation()
    {
        var cache = new SqliteSchemaCache();
        int initial = cache.Generation;
        cache.Put("c1", "main", Snapshot());
        Assert.True(cache.Generation > initial);
    }

    [Fact]
    public void RemoveConnection_ClearsEntries()
    {
        var cache = new SqliteSchemaCache();
        cache.Put("c1", "main", Snapshot());
        cache.Put("c1", "aux", Snapshot());
        cache.RemoveConnection("c1");
        Assert.False(cache.TryGet("c1", "main", out _));
        Assert.False(cache.TryGet("c1", "aux", out _));
    }

    [Fact]
    public void GetFresh_ReturnsOnlyNonExpired()
    {
        var cache = new SqliteSchemaCache(TimeSpan.FromMilliseconds(50));
        cache.Put("c1", "main", Snapshot());
        Thread.Sleep(80);
        cache.Put("c1", "aux", Snapshot());
        var fresh = cache.GetFresh("c1");
        Assert.Single(fresh);
        Assert.Equal("aux", fresh[0].Database);
    }

    [Fact]
    public void Empty_IsStaticSingleton()
    {
        Assert.Same(SqliteSchemaSnapshot.Empty, SqliteSchemaSnapshot.Empty);
        Assert.Empty(SqliteSchemaSnapshot.Empty.Tables);
    }
}
