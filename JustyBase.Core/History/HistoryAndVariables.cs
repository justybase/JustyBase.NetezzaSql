namespace JustyBase.Core.History;

public sealed record HistoryEntry(
    Guid Id,
    DateTimeOffset CreatedAt,
    string Sql,
    string? ConnectionName = null,
    string? DatabaseName = null,
    bool IsFavorite = false);

public sealed class HistoryService(int maxEntries = 1000)
{
    private readonly List<HistoryEntry> _entries = [];
    private readonly object _sync = new();

    public IReadOnlyList<HistoryEntry> Entries
    {
        get { lock (_sync) return _entries.ToArray(); }
    }

    public HistoryEntry Add(string sql, string? connectionName = null, string? databaseName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        var entry = new HistoryEntry(Guid.NewGuid(), DateTimeOffset.UtcNow, sql, connectionName, databaseName);
        lock (_sync)
        {
            _entries.Insert(0, entry);
            Prune();
        }
        return entry;
    }

    public void SetFavorite(Guid id, bool favorite)
    {
        lock (_sync)
        {
            int index = _entries.FindIndex(entry => entry.Id == id);
            if (index >= 0)
                _entries[index] = _entries[index] with { IsFavorite = favorite };
        }
    }

    public IReadOnlyList<HistoryEntry> Search(string? text)
    {
        lock (_sync)
        {
            if (string.IsNullOrWhiteSpace(text))
                return _entries.ToArray();
            return _entries.Where(entry => entry.Sql.Contains(text, StringComparison.OrdinalIgnoreCase)).ToArray();
        }
    }

    private void Prune()
    {
        while (_entries.Count > Math.Max(1, maxEntries))
        {
            int index = _entries.FindLastIndex(entry => !entry.IsFavorite);
            if (index < 0)
                break;
            _entries.RemoveAt(index);
        }
    }
}

public sealed record Snippet(string Name, string Sql, string? Description = null);

public sealed class SessionVariableStore
{
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> Values => _values;

    public void Set(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _values[name.TrimStart('&')] = value;
    }

    public bool Remove(string name) => _values.Remove(name.TrimStart('&'));

    public string Expand(string sql)
    {
        foreach (var pair in _values.OrderByDescending(pair => pair.Key.Length))
            sql = sql.Replace($"&{pair.Key}", pair.Value, StringComparison.OrdinalIgnoreCase);
        return sql;
    }
}
