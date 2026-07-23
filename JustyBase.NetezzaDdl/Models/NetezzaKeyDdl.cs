namespace JustyBase.NetezzaDdl.Models;

/// <summary>Metadata for a table constraint (primary key, foreign key, or unique).</summary>
/// <param name="KeyType">Constraint type character: 'P' (primary), 'F' (foreign), 'U' (unique).</param>
/// <param name="KeyName">Constraint name.</param>
/// <param name="ColumnNames">Columns in the constraint.</param>
/// <param name="PkDatabase">Referenced primary key database (foreign key only).</param>
/// <param name="PkSchema">Referenced primary key schema (foreign key only).</param>
/// <param name="PkRelation">Referenced table name (foreign key only).</param>
/// <param name="ReferencedPkColumnNames">Referenced primary key columns (foreign key only).</param>
/// <param name="OnDelete">Foreign key ON DELETE action.</param>
/// <param name="OnUpdate">Foreign key ON UPDATE action.</param>
public sealed record NetezzaKeyDdl(
    char KeyType,
    string KeyName,
    IReadOnlyList<string> ColumnNames,
    string? PkDatabase = null,
    string? PkSchema = null,
    string? PkRelation = null,
    IReadOnlyList<string>? ReferencedPkColumnNames = null,
    string OnDelete = "NO ACTION",
    string OnUpdate = "NO ACTION");
