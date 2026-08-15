using System.Data;
using System.Data.Common;
using JustyBase.Sqlite.Models;

namespace JustyBase.Sqlite.Schema;

/// <summary>Loader policy knobs. Defaults match host production behavior.</summary>
public sealed record SqliteCatalogLoadOptions
{
    /// <summary>Hydrate columns eagerly unless the table-like object count reaches <see cref="LazyColumnThreshold"/>.</summary>
    public bool EagerColumns { get; init; } = true;

    /// <summary>Load trigger definitions (from <c>sqlite_schema</c>) along with the object list.</summary>
    public bool LoadTriggers { get; init; } = true;

    /// <summary>Table-like object count at which column hydration is deferred.</summary>
    public int LazyColumnThreshold { get; init; } = 200;
}

/// <summary>
/// Shared SQLite catalog loader. Reads <c>PRAGMA database_list</c>,
/// <c>sqlite_schema</c> and <c>PRAGMA table_info</c> over any ADO.NET
/// <see cref="DbConnection"/> (Microsoft.Data.Sqlite, System.Data.SQLite, ODBC,
/// test doubles) and produces host-neutral <see cref="SqliteSchemaSnapshot"/>
/// values. The loader performs no caching itself; pair it with
/// <see cref="SqliteSchemaCache"/>.
/// </summary>
public static class SqliteSchemaLoader
{
    /// <summary>Loads the attached database list (main, temp and ATTACHed databases).</summary>
    public static async Task<IReadOnlyList<string>> LoadDatabasesAsync(
        DbConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        await EnsureOpenAsync(connection, cancellationToken).ConfigureAwait(false);

        var databases = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA database_list";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            string? name = ReadStringOrNull(reader, 1);
            if (!string.IsNullOrWhiteSpace(name))
                databases.Add(name);
        }

        return databases;
    }

    /// <summary>Loads the complete catalog for one attached database (objects, eager or deferred columns, triggers).</summary>
    public static async Task<SqliteSchemaSnapshot> LoadCatalogAsync(
        DbConnection connection,
        string database,
        SqliteCatalogLoadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(database, nameof(database));
        options ??= new SqliteCatalogLoadOptions();

        var tables = await LoadObjectsAsync(connection, database, cancellationToken).ConfigureAwait(false);

        bool deferColumns = tables.Count >= options.LazyColumnThreshold;
        if (options.EagerColumns && !deferColumns)
        {
            await AttachColumnsAsync(connection, database, tables, cancellationToken).ConfigureAwait(false);
        }

        IReadOnlyList<SqliteTriggerDefinition>? triggers = null;
        if (options.LoadTriggers)
        {
            triggers = await LoadTriggersAsync(connection, database, cancellationToken).ConfigureAwait(false);
        }

        return new SqliteSchemaSnapshot(
            tables,
            Triggers: triggers,
            Version: DateTime.UtcNow.Ticks,
            IsPartial: false,
            LoadedAt: DateTimeOffset.UtcNow);
    }

    /// <summary>Loads columns for a single table (lazy hydration path, <c>PRAGMA table_info</c>).</summary>
    public static async Task<IReadOnlyList<SqliteSchemaColumn>> HydrateColumnsAsync(
        DbConnection connection,
        string database,
        string tableName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        await EnsureOpenAsync(connection, cancellationToken).ConfigureAwait(false);

        return await ReadTableInfoAsync(connection, database, tableName, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Loads trigger definitions for one attached database.</summary>
    public static async Task<IReadOnlyList<SqliteTriggerDefinition>> LoadTriggersAsync(
        DbConnection connection,
        string database,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        await EnsureOpenAsync(connection, cancellationToken).ConfigureAwait(false);

        var triggers = new List<SqliteTriggerDefinition>();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT name, sql FROM {QuoteCatalog(database)}.sqlite_schema WHERE type = 'trigger' AND name NOT LIKE 'sqlite_%' ORDER BY name";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            triggers.Add(new SqliteTriggerDefinition(
                reader.GetString(0),
                ReadStringOrNull(reader, 1) ?? string.Empty,
                database));
        }

        return triggers;
    }

    private static async Task<List<SqliteSchemaTable>> LoadObjectsAsync(
        DbConnection connection,
        string database,
        CancellationToken cancellationToken)
    {
        await EnsureOpenAsync(connection, cancellationToken).ConfigureAwait(false);

        var tables = new List<SqliteSchemaTable>();
        await using var command = connection.CreateCommand();
        // Virtual-table shadow tables (FTS5 _config/_content/_data/...) are
        // created internally with a single-quoted name (CREATE TABLE 'x'...).
        // Excluding them keeps only real user objects in the catalog; the
        // pattern matches SQLite's own shadow-table detection heuristic.
        command.CommandText =
            $"SELECT name, type, sql FROM {QuoteCatalog(database)}.sqlite_schema " +
            "WHERE type IN ('table', 'view') AND name NOT LIKE 'sqlite_%' " +
            "AND NOT (type = 'table' AND sql LIKE 'CREATE TABLE ''%') ORDER BY name";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            string name = reader.GetString(0);
            string type = reader.GetString(1);
            bool isView = type.Equals("view", StringComparison.OrdinalIgnoreCase);
            tables.Add(new SqliteSchemaTable(
                name,
                database,
                isView ? SqliteObjectKind.View : SqliteObjectKind.Table,
                IsView: isView,
                Sql: ReadStringOrNull(reader, 2)));
        }

        return tables;
    }

    private static async Task AttachColumnsAsync(
        DbConnection connection,
        string database,
        List<SqliteSchemaTable> tables,
        CancellationToken cancellationToken)
    {
        for (var i = 0; i < tables.Count; i++)
        {
            var table = tables[i];
            if (table.IsView)
                continue;
            cancellationToken.ThrowIfCancellationRequested();
            var columns = await ReadTableInfoAsync(connection, database, table.Name, cancellationToken).ConfigureAwait(false);
            tables[i] = table with { Columns = columns };
        }
    }

    private static async Task<IReadOnlyList<SqliteSchemaColumn>> ReadTableInfoAsync(
        DbConnection connection,
        string database,
        string tableName,
        CancellationToken cancellationToken)
    {
        var columns = new List<SqliteSchemaColumn>();
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {QuoteCatalog(database)}.table_info({QuoteIdentifier(tableName)})";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            columns.Add(new SqliteSchemaColumn(
                reader.GetString(1),
                ReadStringOrNull(reader, 2),
                NotNull: ReadInt(reader, 3) != 0,
                IsPrimaryKey: ReadInt(reader, 5) > 0,
                DefaultValue: ReadStringOrNull(reader, 4)));
        }

        return columns;
    }

    private static string QuoteCatalog(string database)
        => database.Equals("temp", StringComparison.OrdinalIgnoreCase) ? "temp" : QuoteIdentifier(database);

    private static string QuoteIdentifier(string value)
        => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static int ReadInt(DbDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal));

    private static string? ReadStringOrNull(DbDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : Convert.ToString(reader.GetValue(ordinal));

    private static async Task EnsureOpenAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
