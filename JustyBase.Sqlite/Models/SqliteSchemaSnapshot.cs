namespace JustyBase.Sqlite.Models;

/// <summary>Catalog object kind reported by <c>sqlite_schema.type</c>.</summary>
public enum SqliteObjectKind
{
    Table,
    View,
    Trigger,
    Index,
    Other
}

/// <summary>
/// Immutable SQLite metadata passed from a host-specific cache to SQL authoring.
/// It deliberately contains no ADO.NET, UI, or application-cache types.
/// </summary>
/// <param name="Tables">All table-like catalog objects (tables, views) in the snapshot.</param>
/// <param name="Triggers">Optional trigger definitions for hosts that surface trigger authoring.</param>
/// <param name="Version">Monotonically increasing version number for cache invalidation.</param>
/// <param name="IsPartial"><see langword="true"/> when loading was cancelled or one database failed and the snapshot is incomplete.</param>
/// <param name="LoadedAt">Moment the snapshot was produced (cache freshness bookkeeping).</param>
public sealed record SqliteSchemaSnapshot(
    IReadOnlyList<SqliteSchemaTable> Tables,
    IReadOnlyList<SqliteTriggerDefinition>? Triggers = null,
    long Version = 0,
    bool IsPartial = false,
    DateTimeOffset LoadedAt = default)
{
    /// <summary>A static empty snapshot singleton.</summary>
    public static SqliteSchemaSnapshot Empty { get; } = new([], Version: 0);
}

/// <summary>Metadata for a single SQLite table or view.</summary>
/// <param name="Name">Object name.</param>
/// <param name="Database">Owning database (main / temp / attached name), if known.</param>
/// <param name="Kind">Catalog object kind.</param>
/// <param name="IsView"><see langword="true"/> when this object is a view.</param>
/// <param name="Columns">Column definitions; <see langword="null"/> when unknown.</param>
/// <param name="Sql">The original CREATE statement stored in <c>sqlite_schema.sql</c>, when available.</param>
public sealed record SqliteSchemaTable(
    string Name,
    string? Database = null,
    SqliteObjectKind Kind = SqliteObjectKind.Table,
    bool IsView = false,
    IReadOnlyList<SqliteSchemaColumn>? Columns = null,
    string? Sql = null);

/// <summary>Metadata for a single column in a SQLite table.</summary>
/// <param name="Name">Column name.</param>
/// <param name="DataType">Declared type string (e.g. <c>INTEGER</c>, <c>VARCHAR(100)</c>); may be empty for typeless columns.</param>
/// <param name="NotNull"><see langword="true"/> when the column has a NOT NULL constraint.</param>
/// <param name="IsPrimaryKey"><see langword="true"/> when the column is part of the primary key.</param>
/// <param name="DefaultValue">Optional default value expression.</param>
public sealed record SqliteSchemaColumn(
    string Name,
    string? DataType = null,
    bool NotNull = false,
    bool IsPrimaryKey = false,
    string? DefaultValue = null);

/// <summary>Host-neutral metadata for a SQLite trigger.</summary>
public sealed record SqliteTriggerDefinition(
    string Name,
    string Sql,
    string? Database = null);
