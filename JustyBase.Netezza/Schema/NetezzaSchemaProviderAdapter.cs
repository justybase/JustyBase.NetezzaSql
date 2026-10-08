using JustyBase.Netezza.Abstractions;
using JustyBase.Netezza.Models;
using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.Netezza.Schema;

/// <summary>Loads a neutral metadata snapshot into the parser's schema provider.</summary>
public static class NetezzaSchemaProviderAdapter
{
    /// <summary>Default singleton instance that implements <see cref="INetezzaSchemaProviderAdapter"/>.</summary>
    public static INetezzaSchemaProviderAdapter Default { get; } = new DefaultNetezzaSchemaProviderAdapter();

    /// <summary>Applies the snapshot to the given <paramref name="provider"/>.</summary>
    public static void Apply(
        InMemorySchemaProvider provider,
        NetezzaSchemaSnapshot snapshot,
        bool clear = true)
        => Default.Apply(provider, snapshot, clear);
}

internal sealed class DefaultNetezzaSchemaProviderAdapter : INetezzaSchemaProviderAdapter
{
    public void Apply(
        InMemorySchemaProvider provider,
        NetezzaSchemaSnapshot snapshot,
        bool clear = true)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(snapshot);

        if (clear)
            provider.Clear();

        foreach (var table in snapshot.Tables)
        {
            if (IsTableLike(table.Kind))
                AddTable(provider, table);
        }

        if (snapshot.ExternalTables is { Count: > 0 } externals)
        {
            foreach (var table in externals)
                AddTable(provider, table);
        }

        // Procedures are carried on the snapshot for host CALL UX; InMemorySchemaProvider
        // is table/column oriented, so they are not projected into table metadata.

        provider.BumpMetadataEpoch();
    }

    private static bool IsTableLike(NetezzaObjectKind kind)
        => kind is NetezzaObjectKind.Table
            or NetezzaObjectKind.View
            or NetezzaObjectKind.ExternalTable
            or NetezzaObjectKind.Synonym;

    private static void AddTable(InMemorySchemaProvider provider, NetezzaSchemaTable table)
    {
        var columns = table.Columns?.Select(column => new ColumnInfo(
            column.Name,
            DataType: column.DataType,
            Description: column.Description)).ToArray() ?? [];

        provider.AddTable(new TableInfo(
            table.Name,
            table.Schema,
            table.Database,
            Columns: columns,
            IsView: table.IsView || table.Kind == NetezzaObjectKind.View,
            IsExternal: table.Kind == NetezzaObjectKind.ExternalTable));

        AddDeclaredForeignKeys(provider, table);
    }

    /// <summary>
    /// Projects declared foreign keys from the neutral snapshot into the parser's
    /// <see cref="IForeignKeyProvider"/> capability. Primary keys are not registered as
    /// relations: the completion layer discovers reverse relationships from foreign keys,
    /// so duplicating them would double-count.
    /// </summary>
    private static void AddDeclaredForeignKeys(InMemorySchemaProvider provider, NetezzaSchemaTable table)
    {
        if (table.Keys is not { Count: > 0 })
        {
            return;
        }

        foreach (var key in table.Keys)
        {
            if (!key.IsForeignKey
                || key.Columns.Count == 0
                || string.IsNullOrEmpty(key.ReferencedTable))
            {
                continue;
            }

            var referencedColumns = key.ReferencedColumns is { Count: > 0 }
                ? key.ReferencedColumns
                : key.Columns;
            if (referencedColumns.Count != key.Columns.Count)
            {
                continue; // malformed key row — never invent a partial pairing.
            }

            provider.AddForeignKey(
                table.Database,
                table.Schema,
                table.Name,
                new ForeignKeyRelation(
                    key.Columns,
                    key.ReferencedTable,
                    referencedColumns,
                    key.ReferencedSchema,
                    key.ReferencedDatabase,
                    key.ConstraintName));
        }
    }
}
