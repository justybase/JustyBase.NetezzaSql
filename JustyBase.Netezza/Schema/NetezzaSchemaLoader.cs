using System.Data;
using System.Data.Common;
using JustyBase.Netezza.Models;
using JustyBase.Netezza.Metadata;
using JustyBase.NetezzaCatalogSql;

namespace JustyBase.Netezza.Schema;

/// <summary>One database row from the catalog (<c>_v_database</c>).</summary>
public sealed record NetezzaDatabaseInfo(
    string Name,
    string? DefaultSchema = null,
    string? Owner = null,
    int CatalogId = 0);

/// <summary>Loader policy knobs. Defaults match host production behavior.</summary>
public sealed record NetezzaCatalogLoadOptions
{
    /// <summary>Hydrate columns eagerly unless the table-like object count reaches <see cref="LazyColumnThreshold"/>.</summary>
    public bool EagerColumns { get; init; } = true;

    /// <summary>Load procedure definitions (sources) along with the object list.</summary>
    public bool LoadProcedures { get; init; } = true;

    /// <summary>When <see langword="true"/>, a failing database aborts <see cref="NetezzaSchemaLoader.LoadAllAsync"/>. When <see langword="false"/>,
    /// the failure is collected and the remaining databases are still loaded.</summary>
    public bool FailOnDatabaseError { get; init; }

    /// <summary>Table-like object count at which column hydration is deferred (see <see cref="MetadataPrefetchContract"/>).</summary>
    public int LazyColumnThreshold { get; init; } = MetadataPrefetchContract.LazyColumnsObjectThreshold;
}

/// <summary>
/// Shared Netezza catalog loader. Reads the modern catalog SQL (<see cref="NetezzaCatalogSql"/>) over any
/// ADO.NET <see cref="DbConnection"/> (native <c>NzConnection</c>, ODBC, test doubles) and produces
/// host-neutral <see cref="NetezzaSchemaSnapshot"/> values. The loader performs no caching itself;
/// pair it with <see cref="NetezzaSchemaCache"/>.
/// </summary>
public static class NetezzaSchemaLoader
{
    /// <summary>Loads the database list for a connection.</summary>
    public static async Task<IReadOnlyList<NetezzaDatabaseInfo>> LoadDatabasesAsync(
        DbConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        await EnsureOpenAsync(connection, cancellationToken).ConfigureAwait(false);

        var databases = new List<NetezzaDatabaseInfo>();
        await using var command = connection.CreateCommand();
        command.CommandText = JustyBase.NetezzaCatalogSql.NetezzaCatalogSql.DatabasesSql;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            databases.Add(new NetezzaDatabaseInfo(
                reader.GetString(2),
                ReadStringOrNull(reader, 4),
                ReadStringOrNull(reader, 3),
                ReadInt(reader, 0)));
        }

        return databases;
    }

    /// <summary>Loads the complete catalog for one database (objects, eager or deferred columns, procedures).</summary>
    public static async Task<NetezzaSchemaSnapshot> LoadCatalogAsync(
        DbConnection connection,
        string database,
        NetezzaCatalogLoadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(database, nameof(database));
        options ??= new NetezzaCatalogLoadOptions();

        var tables = await LoadObjectsAsync(connection, database, cancellationToken).ConfigureAwait(false);

        int tableLikeCount = CountTableLike(tables);
        bool deferColumns = tableLikeCount >= options.LazyColumnThreshold;
        if (options.EagerColumns && !deferColumns)
        {
            await AttachColumnsAsync(connection, database, tables, cancellationToken).ConfigureAwait(false);
        }

        // Keys are independent of column deferral: declared relationships must be
        // available to authoring even when column hydration is lazy.
        await AttachKeysAsync(connection, database, tables, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<NetezzaProcedureDefinition>? procedures = null;
        if (options.LoadProcedures)
        {
            procedures = await LoadProceduresAsync(connection, database, cancellationToken).ConfigureAwait(false);
        }

        return new NetezzaSchemaSnapshot(
            tables,
            Version: DateTime.UtcNow.Ticks,
            Procedures: procedures,
            IsPartial: false,
            LoadedAt: DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Loads every database in the catalog. When <see cref="NetezzaCatalogLoadOptions.FailOnDatabaseError"/> is
    /// <see langword="false"/> (default), a failing database produces a partial snapshot instead of aborting the batch.
    /// </summary>
    public static async Task<IReadOnlyList<(string Database, NetezzaSchemaSnapshot Snapshot)>> LoadAllAsync(
        DbConnection connection,
        NetezzaCatalogLoadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        options ??= new NetezzaCatalogLoadOptions();

        IReadOnlyList<NetezzaDatabaseInfo> databases;
        try
        {
            databases = await LoadDatabasesAsync(connection, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception) {
            if (options.FailOnDatabaseError)
                throw;
            return [(string.Empty, new NetezzaSchemaSnapshot([], IsPartial: true, LoadedAt: DateTimeOffset.UtcNow))];
        }

        var results = new List<(string Database, NetezzaSchemaSnapshot Snapshot)>(databases.Count);
        foreach (var database in databases)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var snapshot = await LoadCatalogAsync(connection, database.Name, options, cancellationToken).ConfigureAwait(false);
                results.Add((database.Name, snapshot));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Cancellation aborts the whole batch — never hand back a partial snapshot
                // that looks like a successful completion (matches LoadDatabasesAsync).
                throw;
            }
            catch (Exception) {
                if (options.FailOnDatabaseError)
                    throw;
                results.Add((database.Name, new NetezzaSchemaSnapshot([], IsPartial: true, LoadedAt: DateTimeOffset.UtcNow)));
            }
        }

        return results;
    }

    /// <summary>Loads columns for a single table (lazy hydration path, <c>GetTableColumnsSql</c>).</summary>
    public static async Task<IReadOnlyList<NetezzaSchemaColumn>> HydrateColumnsAsync(
        DbConnection connection,
        string database,
        string schema,
        string tableName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        await EnsureOpenAsync(connection, cancellationToken).ConfigureAwait(false);

        var columns = new List<NetezzaSchemaColumn>();
        await using var command = connection.CreateCommand();
        command.CommandText = JustyBase.NetezzaCatalogSql.NetezzaCatalogSql.GetTableColumnsSql(database, schema, tableName);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            columns.Add(ReadColumnRow(reader));
        }

        return columns;
    }

    private static async Task<List<NetezzaSchemaTable>> LoadObjectsAsync(
        DbConnection connection,
        string database,
        CancellationToken cancellationToken)
    {
        await EnsureOpenAsync(connection, cancellationToken).ConfigureAwait(false);

        // (schema, name) -> table; catalog ORDER BY already groups schema/name.
        var byKey = new Dictionary<(string, string), NetezzaSchemaTable>(new SchemaObjectKeyComparer());
        var orderedKeys = new List<(string Schema, string Name)>();

        await using var command = connection.CreateCommand();
        command.CommandText = JustyBase.NetezzaCatalogSql.NetezzaCatalogSql.GetSqlTablesAndOtherObjects(database);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            int catalogId = ReadInt(reader, 0);
            string name = reader.GetString(1);
            string? description = ReadStringOrNull(reader, 2);
            string schema = reader.GetString(3);
            string objectType = reader.GetString(4);
            string? owner = ReadStringOrNull(reader, 5);
            DateTime? created = ReadDateTimeOrNull(reader, 6);
            NetezzaObjectKind kind = MapObjectKind(objectType);

            var key = (schema, name);
            if (!byKey.ContainsKey(key))
            {
                byKey[key] = new NetezzaSchemaTable(
                    name,
                    schema,
                    database,
                    Kind: kind,
                    IsView: kind == NetezzaObjectKind.View,
                    Columns: null,
                    Description: description,
                    Owner: owner,
                    CatalogId: catalogId,
                    Created: created,
                    TextType: objectType);
                orderedKeys.Add(key);
            }
        }

        var tables = new List<NetezzaSchemaTable>(orderedKeys.Count);
        foreach (var key in orderedKeys)
        {
            tables.Add(byKey[key]);
        }

        return tables;
    }

    private static async Task AttachColumnsAsync(
        DbConnection connection,
        string database,
        List<NetezzaSchemaTable> tables,
        CancellationToken cancellationToken)
    {
        await EnsureOpenAsync(connection, cancellationToken).ConfigureAwait(false);

        var byCatalogId = new Dictionary<int, NetezzaSchemaTable>();
        foreach (var table in tables)
        {
            if (table.CatalogId != 0 && !byCatalogId.ContainsKey(table.CatalogId))
            {
                byCatalogId[table.CatalogId] = table;
            }
        }

        if (byCatalogId.Count == 0)
        {
            return;
        }

        // (objectId) -> columns, preserving ATTNU order.
        var columnsByObjectId = new Dictionary<int, List<NetezzaSchemaColumn>>();

        await using var command = connection.CreateCommand();
        command.CommandText = JustyBase.NetezzaCatalogSql.NetezzaCatalogSql.GetSqlOfColumns(database);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            int objectId = ReadInt(reader, 0);
            if (!byCatalogId.ContainsKey(objectId))
            {
                continue; // orphan column row — no matching catalog object.
            }

            if (!columnsByObjectId.TryGetValue(objectId, out var list))
            {
                columnsByObjectId[objectId] = list = [];
            }

            list.Add(ReadColumnRow(reader));
        }

        if (columnsByObjectId.Count == 0)
        {
            return;
        }

        for (int i = 0; i < tables.Count; i++)
        {
            var table = tables[i];
            if (table.CatalogId != 0
                && columnsByObjectId.TryGetValue(table.CatalogId, out var columns))
            {
                tables[i] = table with { Columns = columns };
            }
        }
    }

    private static async Task AttachKeysAsync(
        DbConnection connection,
        string database,
        List<NetezzaSchemaTable> tables,
        CancellationToken cancellationToken)
    {
        await EnsureOpenAsync(connection, cancellationToken).ConfigureAwait(false);

        if (tables.Count == 0)
        {
            return;
        }

        var rows = new List<NetezzaSchemaKeyRow>();
        await using var command = connection.CreateCommand();
        command.CommandText = JustyBase.NetezzaCatalogSql.NetezzaCatalogSql.GetBulkRelationKeysSql(database);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new NetezzaSchemaKeyRow(
                ReadInt(reader, 0),
                ReadStringOrNull(reader, 1),
                ReadStringOrNull(reader, 2),
                ReadStringOrNull(reader, 3) ?? string.Empty,
                ReadStringOrNull(reader, 4) ?? string.Empty,
                ReadStringOrNull(reader, 6) ?? string.Empty,
                NonEmpty(ReadStringOrNull(reader, 7)),
                NonEmpty(ReadStringOrNull(reader, 8)),
                NonEmpty(ReadStringOrNull(reader, 9)),
                NonEmpty(ReadStringOrNull(reader, 10))));
        }

        if (rows.Count == 0)
        {
            return;
        }

        var updated = AttachReferenceKeys(tables, rows, database);
        for (int i = 0; i < tables.Count; i++)
        {
            tables[i] = updated[i];
        }
    }

    /// <summary>
    /// Groups raw catalog key rows into per-object <see cref="NetezzaReferenceKey"/> records.
    /// Catalog order (<c>OBJID, CONSTRAINTNAME, CONSEQ</c>) is preserved so composite keys
    /// stay paired. Pure and offline-testable; the loader supplies rows from
    /// <c>GetBulkRelationKeysSql</c>.
    /// </summary>
    public static IReadOnlyList<NetezzaSchemaTable> AttachReferenceKeys(
        IReadOnlyList<NetezzaSchemaTable> tables,
        IReadOnlyList<NetezzaSchemaKeyRow> rows,
        string database)
    {
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(rows);

        var byCatalogId = new Dictionary<int, NetezzaSchemaTable>();
        foreach (var table in tables)
        {
            if (table.CatalogId != 0 && !byCatalogId.ContainsKey(table.CatalogId))
            {
                byCatalogId[table.CatalogId] = table;
            }
        }

        var buildersByObjectId = new Dictionary<int, Dictionary<string, KeyAccumulator>>();
        foreach (var row in rows)
        {
            if (!byCatalogId.ContainsKey(row.ObjectId))
            {
                continue; // orphan key row — no matching catalog object.
            }

            if (!buildersByObjectId.TryGetValue(row.ObjectId, out var builders))
            {
                buildersByObjectId[row.ObjectId] = builders =
                    new Dictionary<string, KeyAccumulator>(StringComparer.OrdinalIgnoreCase);
            }

            string accumulatorKey = $"{row.ConstraintType}\u0000{row.ConstraintName}";
            if (!builders.TryGetValue(accumulatorKey, out var accumulator))
            {
                builders[accumulatorKey] = accumulator = new KeyAccumulator(row.ConstraintName, row.ConstraintType);
            }

            if (!string.IsNullOrEmpty(row.Column))
            {
                accumulator.Columns.Add(row.Column);
            }

            if (!string.IsNullOrEmpty(row.ReferencedColumn))
            {
                accumulator.ReferencedColumns.Add(row.ReferencedColumn);
                accumulator.ReferencedDatabase ??= row.ReferencedDatabase;
                accumulator.ReferencedSchema ??= row.ReferencedSchema;
                accumulator.ReferencedTable ??= row.ReferencedTable;
            }
        }

        var result = new List<NetezzaSchemaTable>(tables.Count);
        foreach (var table in tables)
        {
            if (table.CatalogId == 0
                || !buildersByObjectId.TryGetValue(table.CatalogId, out var builders))
            {
                result.Add(table);
                continue;
            }

            var keys = new List<NetezzaReferenceKey>(builders.Count);
            foreach (var accumulator in builders.Values)
            {
                if (accumulator.Columns.Count == 0)
                {
                    continue;
                }

                var columns = accumulator.Columns.ToArray();
                keys.Add(accumulator.IsPrimaryKey
                    ? new NetezzaReferenceKey(
                        accumulator.Name,
                        accumulator.Type,
                        columns,
                        table.Database,
                        table.Schema,
                        table.Name,
                        columns)
                    : new NetezzaReferenceKey(
                        accumulator.Name,
                        accumulator.Type,
                        columns,
                        accumulator.ReferencedDatabase ?? table.Database,
                        accumulator.ReferencedSchema,
                        accumulator.ReferencedTable,
                        accumulator.ReferencedColumns.Count > 0 ? accumulator.ReferencedColumns.ToArray() : columns));
            }

            result.Add(keys.Count > 0 ? table with { Keys = keys } : table);
        }

        return result;
    }

    private static string? NonEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private sealed class KeyAccumulator(string name, string type)
    {
        public string Name { get; } = name;
        public string Type { get; } = type;
        public List<string> Columns { get; } = [];
        public string? ReferencedDatabase { get; set; }
        public string? ReferencedSchema { get; set; }
        public string? ReferencedTable { get; set; }
        public List<string> ReferencedColumns { get; } = [];
        public bool IsPrimaryKey => string.Equals(Type, "p", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<IReadOnlyList<NetezzaProcedureDefinition>> LoadProceduresAsync(
        DbConnection connection,
        string database,
        CancellationToken cancellationToken)
    {
        await EnsureOpenAsync(connection, cancellationToken).ConfigureAwait(false);

        var procedures = new List<NetezzaProcedureDefinition>();
        await using var command = connection.CreateCommand();
        command.CommandText = JustyBase.NetezzaCatalogSql.NetezzaCatalogSql.GetProceduresSql(database, "");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            string? schema = ReadStringOrNull(reader, 0);
            string? source = ReadStringOrNull(reader, 1);
            string? returns = ReadStringOrNull(reader, 3);
            string? signature = ReadStringOrNull(reader, 6);
            string? arguments = ReadStringOrNull(reader, 7);
            string? description = ReadStringOrNull(reader, 5);
            bool executeAsOwner = ReadBool(reader, 4);

            if (string.IsNullOrWhiteSpace(signature))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(returns))
            {
                returns = NetezzaProcTypes.FixProcedureReturnType(returns);
            }

            procedures.Add(new NetezzaProcedureDefinition(
                database,
                schema ?? string.Empty,
                signature,
                returns ?? string.Empty,
                source ?? string.Empty,
                arguments,
                executeAsOwner,
                description));
        }

        return procedures;
    }

    private static NetezzaSchemaColumn ReadColumnRow(DbDataReader reader)
    {
        string columnName = reader.GetString(1);
        string? description = ReadStringOrNull(reader, 2);
        string dataType = reader.GetString(3);
        bool notNull = ReadBool(reader, 4);
        string? defaultValue = ReadStringOrNull(reader, 5);

        return new NetezzaSchemaColumn(columnName, dataType, !notNull, description, defaultValue);
    }

    private static int CountTableLike(IReadOnlyList<NetezzaSchemaTable> tables)
    {
        int count = 0;
        foreach (var table in tables)
        {
            switch (table.Kind)
            {
                case NetezzaObjectKind.Table:
                case NetezzaObjectKind.View:
                case NetezzaObjectKind.ExternalTable:
                case NetezzaObjectKind.Synonym:
                case NetezzaObjectKind.Sequence:
                    count++;
                    break;
            }
        }

        return count;
    }

    private static NetezzaObjectKind MapObjectKind(string objectType)
    {
        return objectType switch
        {
            "TABLE" or "BASE TABLE" or "TYPED TABLE" or "HIERARCHY TABLE" or "DETACHED TABLE" or "MATERIALIZED QUERY TABLE" => NetezzaObjectKind.Table,
            "VIEW" or "TYPED VIEW" => NetezzaObjectKind.View,
            "PROCEDURE" => NetezzaObjectKind.Procedure,
            "FUNCTION" => NetezzaObjectKind.Function,
            "SEQUENCE" or "IDENTITY SEQUENCE" => NetezzaObjectKind.Sequence,
            "SYNONYM" or "NICKNAME" => NetezzaObjectKind.Synonym,
            "EXTERNAL TABLE" => NetezzaObjectKind.ExternalTable,
            "FLUID" => NetezzaObjectKind.Fluid,
            "AGGREGATE" => NetezzaObjectKind.Aggregate,
            "INDEX" => NetezzaObjectKind.Index,
            "PARTITION" or "PARTITION TABLE" => NetezzaObjectKind.Partition,
            _ => NetezzaObjectKind.Other,
        };
    }

    private static int ReadInt(DbDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal));
    }

    private static string? ReadStringOrNull(DbDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : Convert.ToString(reader.GetValue(ordinal));
    }

    private static DateTime? ReadDateTimeOrNull(DbDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return reader.GetValue(ordinal) switch
        {
            DateTime value => value,
            DateTimeOffset value => value.DateTime,
            _ => Convert.ToDateTime(reader.GetValue(ordinal)),
        };
    }

    private static bool ReadBool(DbDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return false;
        }

        return reader.GetValue(ordinal) switch
        {
            bool value => value,
            short value => value == 1,
            int value => value != 0,
            _ => false,
        };
    }

    private static async Task EnsureOpenAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class SchemaObjectKeyComparer : IEqualityComparer<(string Schema, string Name)>
    {
        public bool Equals((string Schema, string Name) x, (string Schema, string Name) y)
            => string.Equals(x.Schema, y.Schema, StringComparison.OrdinalIgnoreCase)
               && string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Schema, string Name) obj)
            => HashCode.Combine(
                obj.Schema.ToUpperInvariant(),
                obj.Name.ToUpperInvariant());
    }
}
