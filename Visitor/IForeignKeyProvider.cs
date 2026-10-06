namespace JustyBase.NetezzaSqlParser.Visitor;

/// <summary>
/// Declared foreign-key relationship between a table and a referenced table.
/// Column lists are positionally paired.
/// </summary>
public sealed record ForeignKeyRelation(
    IReadOnlyList<string> Columns,
    string ReferencedTable,
    IReadOnlyList<string> ReferencedColumns,
    string? ReferencedSchema = null,
    string? ReferencedDatabase = null,
    string? Name = null
);

/// <summary>
/// Optional schema-provider capability exposing declared foreign keys.
/// Providers that do not implement it simply yield no JOIN relation suggestions.
/// </summary>
public interface IForeignKeyProvider
{
    IReadOnlyList<ForeignKeyRelation>? GetForeignKeys(string? database, string? schema, string tableName);
}
