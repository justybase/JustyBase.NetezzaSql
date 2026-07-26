namespace JustyBase.Core.Schema;

public sealed record SchemaSnapshot(
    string Database,
    string? Schema,
    IReadOnlyList<string> Objects,
    DateTimeOffset LoadedAt);

public sealed class SchemaCache(TimeSpan? ttl = null)
{
    private readonly TimeSpan _ttl = ttl ?? TimeSpan.FromMinutes(5);
    private readonly Dictionary<string, SchemaSnapshot> _snapshots = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    public bool TryGet(string database, string? schema, out SchemaSnapshot snapshot)
    {
        string key = Key(database, schema);
        lock (_sync)
        {
            if (_snapshots.TryGetValue(key, out snapshot!)
                && DateTimeOffset.UtcNow - snapshot.LoadedAt <= _ttl)
                return true;
            snapshot = null!;
            return false;
        }
    }

    public void Put(SchemaSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_sync) _snapshots[Key(snapshot.Database, snapshot.Schema)] = snapshot;
    }

    public SchemaSnapshot Merge(SchemaSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_sync)
        {
            string key = Key(snapshot.Database, snapshot.Schema);
            if (!_snapshots.TryGetValue(key, out var existing))
            {
                _snapshots[key] = snapshot;
                return snapshot;
            }

            var merged = new SchemaSnapshot(
                snapshot.Database,
                snapshot.Schema,
                existing.Objects.Concat(snapshot.Objects).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray(),
                snapshot.LoadedAt);
            _snapshots[key] = merged;
            return merged;
        }
    }

    private static string Key(string database, string? schema) => $"{database}\0{schema}";
}

public sealed record SchemaContextMenuAction(string Id, string Label, string SqlTemplate, bool IsDestructive = false);

public static class SchemaContextMenuCatalog
{
    /// <summary>
    /// Declarative defaults. Hosts may replace host-only actions (import/export/DDL wizard)
    /// and should resolve groom/stats via <c>NetezzaMaintenanceSql</c> when packaging allows.
    /// Templates use <c>{0}</c> as the qualified object name.
    /// </summary>
    public static IReadOnlyList<SchemaContextMenuAction> Default { get; } =
    [
        new("ddl", "DDL to query", "-- Host: emit object DDL for {0}"),
        new("select", "Select rows", "SELECT * FROM {0};"),
        new("count", "Count rows", "SELECT COUNT(*) FROM {0};"),
        new("duplicates", "Find duplicates", "SELECT *, COUNT(*) AS cnt FROM {0} GROUP BY 1 HAVING COUNT(*) > 1;"),
        new("deleted", "Show deleted rows", "SELECT * FROM {0} WHERE DATASLICEID IS NULL;"),
        new("comment", "Comment table", "COMMENT ON TABLE {0} IS 'some comment';"),
        new("grant", "Grant template", "GRANT SELECT ON {0} TO SOME_OWNER?;"),
        // Keep in sync with JustyBase.NetezzaDdl.NetezzaMaintenanceSql defaults.
        new("statistics", "Generate statistics", "GENERATE EXPRESS STATISTICS ON {0};"),
        new("groom", "Groom table", "GROOM TABLE {0} RECORDS ALL RECLAIM BACKUPSET NONE;", true),
        new("distribution", "Show distribution", "SELECT datasliceid, COUNT(*) FROM {0} GROUP BY 1 ORDER BY 1;"),
        new("empty", "Empty table", "TRUNCATE TABLE {0};", true),
        new("recreate", "Recreate table", "CREATE TABLE {0}_NEW AS SELECT * FROM {0} DISTRIBUTE ON RANDOM;"),
        new("import", "Import data", "-- Host: open import wizard for {0}"),
        new("export", "Export data", "-- Host: open export wizard for {0}"),
        new("drop", "Drop table", "DROP TABLE {0};", true)
    ];

    public static string Format(SchemaContextMenuAction action, string qualifiedObject)
        => string.Format(System.Globalization.CultureInfo.InvariantCulture, action.SqlTemplate, qualifiedObject);
}
