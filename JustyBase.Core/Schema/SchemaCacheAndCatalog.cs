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

/// <param name="Id">Stable shared id (hosts map UI commands to this).</param>
/// <param name="Label">Default English label; hosts may localize.</param>
/// <param name="SqlTemplate">SQL with <c>{0}</c> = qualified object name. Empty when host-only.</param>
/// <param name="IsDestructive">UI should confirm before run.</param>
/// <param name="IsHostOnly">True when the host must supply DDL wizard / import UI / chart — template is a comment placeholder.</param>
public sealed record SchemaContextMenuAction(
    string Id,
    string Label,
    string SqlTemplate,
    bool IsDestructive = false,
    bool IsHostOnly = false);

/// <summary>
/// Shared SQL action templates for Netezza schema explorers.
/// Hosts keep richer UI trees (Create…, Refresh, charts) and map <see cref="SchemaContextMenuAction.Id"/> to commands.
/// </summary>
public static class SchemaContextMenuCatalog
{
    public static class Ids
    {
        public const string Ddl = "ddl";
        public const string Select = "select";
        public const string SelectTop100 = "select_top100";
        public const string Count = "count";
        public const string Duplicates = "duplicates";
        public const string Deleted = "deleted";
        public const string Comment = "comment";
        public const string Grant = "grant";
        public const string Statistics = "statistics";
        public const string Groom = "groom";
        public const string Distribution = "distribution";
        public const string Empty = "empty";
        public const string Recreate = "recreate";
        public const string Import = "import";
        public const string Export = "export";
        public const string Drop = "drop";
    }

    /// <summary>
    /// Declarative shared SQL defaults.
    /// Host-only actions keep comment placeholders. Groom/stats match <c>NetezzaMaintenanceSql</c> express defaults.
    /// </summary>
    public static IReadOnlyList<SchemaContextMenuAction> Default { get; } =
    [
        new(Ids.Ddl, "DDL to query", "-- Host: emit object DDL for {0}", IsHostOnly: true),
        new(Ids.Select, "Select rows", "SELECT * FROM {0};"),
        new(Ids.SelectTop100, "Select top 100", "SELECT * FROM {0} LIMIT 100;"),
        new(Ids.Count, "Count rows", "SELECT COUNT(*) FROM {0};"),
        new(Ids.Duplicates, "Find duplicates", "SELECT *, COUNT(*) AS cnt FROM {0} GROUP BY 1 HAVING COUNT(*) > 1;"),
        new(Ids.Deleted, "Show deleted rows", "SELECT * FROM {0} WHERE DATASLICEID IS NULL;"),
        new(Ids.Comment, "Comment table", "COMMENT ON TABLE {0} IS 'some comment';"),
        new(Ids.Grant, "Grant template", "GRANT SELECT ON {0} TO SOME_OWNER?;"),
        new(Ids.Statistics, "Generate statistics", "GENERATE EXPRESS STATISTICS ON {0};"),
        new(Ids.Groom, "Groom table", "GROOM TABLE {0} RECORDS ALL RECLAIM BACKUPSET NONE;", IsDestructive: true),
        new(Ids.Distribution, "Show distribution", "SELECT datasliceid, COUNT(*) FROM {0} GROUP BY 1 ORDER BY 1;"),
        new(Ids.Empty, "Empty table", "TRUNCATE TABLE {0};", IsDestructive: true),
        new(Ids.Recreate, "Recreate table", "CREATE TABLE {0}_NEW AS SELECT * FROM {0} DISTRIBUTE ON RANDOM;", IsHostOnly: true),
        new(Ids.Import, "Import data", "-- Host: open import wizard for {0}", IsHostOnly: true),
        new(Ids.Export, "Export data", "-- Host: open export wizard for {0}", IsHostOnly: true),
        new(Ids.Drop, "Drop table", "DROP TABLE {0};", IsDestructive: true)
    ];

    private static readonly Dictionary<string, SchemaContextMenuAction> ByIdLookup =
        Default.ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);

    public static bool TryGet(string id, out SchemaContextMenuAction action)
        => ByIdLookup.TryGetValue(id, out action!);

    public static SchemaContextMenuAction GetRequired(string id)
        => TryGet(id, out var action)
            ? action
            : throw new KeyNotFoundException($"Unknown schema context menu action id '{id}'.");

    public static string Format(SchemaContextMenuAction action, string qualifiedObject)
        => string.Format(System.Globalization.CultureInfo.InvariantCulture, action.SqlTemplate, qualifiedObject);

    public static string Format(string id, string qualifiedObject)
        => Format(GetRequired(id), qualifiedObject);
}
