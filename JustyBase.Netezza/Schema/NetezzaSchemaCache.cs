using JustyBase.Netezza.Metadata;
using JustyBase.Netezza.Models;

namespace JustyBase.Netezza.Schema;

/// <summary>
/// Typed per-connection/per-database schema snapshot cache with TTL freshness and a monotonic
/// generation counter (hosts use the generation to invalidate completion epochs).
/// </summary>
public sealed class NetezzaSchemaCache
{
    private readonly TimeSpan _ttl;
    private readonly Dictionary<string, Dictionary<string, CacheEntry>> _byConnection = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();
    private int _generation;

    public NetezzaSchemaCache(TimeSpan? ttl = null)
    {
        _ttl = ttl ?? MetadataPrefetchContract.DefaultTtl;
    }

    /// <summary>Monotonic counter bumped on every mutation (Put/Remove/RemoveConnection).</summary>
    public int Generation
    {
        get
        {
            lock (_sync)
            {
                return _generation;
            }
        }
    }

    public bool TryGet(string connection, string database, out NetezzaSchemaSnapshot snapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connection, nameof(connection));
        ArgumentException.ThrowIfNullOrWhiteSpace(database, nameof(database));

        lock (_sync)
        {
            if (_byConnection.TryGetValue(connection, out var databases)
                && databases.TryGetValue(database, out var entry)
                && DateTimeOffset.UtcNow - entry.LoadedAt <= _ttl)
            {
                snapshot = entry.Snapshot;
                return true;
            }

            snapshot = NetezzaSchemaSnapshot.Empty;
            return false;
        }
    }

    public void Put(string connection, string database, NetezzaSchemaSnapshot snapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connection, nameof(connection));
        ArgumentException.ThrowIfNullOrWhiteSpace(database, nameof(database));
        ArgumentNullException.ThrowIfNull(snapshot);

        lock (_sync)
        {
            if (!_byConnection.TryGetValue(connection, out var databases))
            {
                databases = new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
                _byConnection[connection] = databases;
            }

            databases[database] = new CacheEntry(snapshot, DateTimeOffset.UtcNow);
            _generation++;
        }
    }

    public void Remove(string connection, string database)
    {
        lock (_sync)
        {
            if (_byConnection.TryGetValue(connection, out var databases) && databases.Remove(database))
            {
                _generation++;
                if (databases.Count == 0)
                {
                    _byConnection.Remove(connection);
                }
            }
        }
    }

    public void RemoveConnection(string connection)
    {
        lock (_sync)
        {
            if (_byConnection.Remove(connection))
            {
                _generation++;
            }
        }
    }

    /// <summary>All non-expired snapshots for a connection (used for completion sync).</summary>
    public IReadOnlyList<(string Database, NetezzaSchemaSnapshot Snapshot)> GetFresh(string connection)
    {
        var now = DateTimeOffset.UtcNow;
        var result = new List<(string Database, NetezzaSchemaSnapshot Snapshot)>();

        lock (_sync)
        {
            if (!_byConnection.TryGetValue(connection, out var databases))
            {
                return result;
            }

            foreach (var (database, entry) in databases)
            {
                if (now - entry.LoadedAt > _ttl)
                {
                    continue;
                }

                result.Add((database, entry.Snapshot));
            }
        }

        return result;
    }

    public void Clear()
    {
        lock (_sync)
        {
            if (_byConnection.Count > 0)
            {
                _byConnection.Clear();
                _generation++;
            }
        }
    }

    private sealed record CacheEntry(NetezzaSchemaSnapshot Snapshot, DateTimeOffset LoadedAt);
}
