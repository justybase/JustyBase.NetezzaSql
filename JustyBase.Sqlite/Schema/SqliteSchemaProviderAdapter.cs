using JustyBase.Sqlite.Abstractions;
using JustyBase.Sqlite.Models;
using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.Sqlite.Schema;

/// <summary>Loads a neutral metadata snapshot into the parser's schema provider.</summary>
public static class SqliteSchemaProviderAdapter
{
    /// <summary>Default singleton instance that implements <see cref="ISqliteSchemaProviderAdapter"/>.</summary>
    public static ISqliteSchemaProviderAdapter Default { get; } = new DefaultSqliteSchemaProviderAdapter();

    /// <summary>Applies the snapshot to the given <paramref name="provider"/>.</summary>
    public static void Apply(
        InMemorySchemaProvider provider,
        SqliteSchemaSnapshot snapshot,
        bool clear = true)
        => Default.Apply(provider, snapshot, clear);
}

internal sealed class DefaultSqliteSchemaProviderAdapter : ISqliteSchemaProviderAdapter
{
    public void Apply(
        InMemorySchemaProvider provider,
        SqliteSchemaSnapshot snapshot,
        bool clear = true)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(snapshot);

        if (clear)
            provider.Clear();

        foreach (var table in snapshot.Tables)
        {
            if (table.Kind is not (SqliteObjectKind.Table or SqliteObjectKind.View))
                continue;

            var columns = table.Columns?.Select(column => new ColumnInfo(
                column.Name,
                DataType: column.DataType,
                Description: column.DefaultValue is null
                    ? null
                    : $"DEFAULT {column.DefaultValue}")).ToArray() ?? [];

            provider.AddTable(new TableInfo(
                table.Name,
                table.Database,
                Columns: columns,
                IsView: table.IsView));
        }

        provider.BumpMetadataEpoch();
    }
}
