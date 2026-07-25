namespace JustyBase.Netezza.Models;

/// <summary>
/// Immutable database metadata passed from a host-specific cache to SQL authoring.
/// It deliberately contains no ADO.NET, UI, or application-cache types.
/// </summary>
/// <param name="Tables">The list of tables and views in the snapshot.</param>
/// <param name="Version">Monotonically increasing version number for cache invalidation.</param>
/// <param name="Procedures">Optional procedure definitions for hosts that surface CALL completion.</param>
/// <param name="ExternalTables">Optional external tables (also applied into the schema provider as tables).</param>
public sealed record NetezzaSchemaSnapshot(
    IReadOnlyList<NetezzaSchemaTable> Tables,
    long Version = 0,
    IReadOnlyList<NetezzaProcedureDefinition>? Procedures = null,
    IReadOnlyList<NetezzaSchemaTable>? ExternalTables = null)
{
    /// <summary>A static empty snapshot singleton.</summary>
    public static NetezzaSchemaSnapshot Empty { get; } = new([], 0);
}

/// <summary>Metadata for a single table or view.</summary>
/// <param name="Name">Table or view name.</param>
/// <param name="Schema">Owning schema, if known.</param>
/// <param name="Database">Owning database, if known.</param>
/// <param name="IsView"><see langword="true"/> when this object is a view.</param>
/// <param name="Columns">Column definitions; <see langword="null"/> when unknown.</param>
/// <param name="Description">Optional table or view description.</param>
public sealed record NetezzaSchemaTable(
    string Name,
    string? Schema = null,
    string? Database = null,
    bool IsView = false,
    IReadOnlyList<NetezzaSchemaColumn>? Columns = null,
    string? Description = null);

/// <summary>Metadata for a single column in a table or view.</summary>
/// <param name="Name">Column name.</param>
/// <param name="DataType">Data type string (e.g. <c>INTEGER</c>, <c>VARCHAR(100)</c>).</param>
/// <param name="Nullable"><see langword="true"/> when the column allows <see langword="null"/>.</param>
/// <param name="Description">Optional column description.</param>
/// <param name="DefaultValue">Optional default value expression.</param>
public sealed record NetezzaSchemaColumn(
    string Name,
    string? DataType = null,
    bool Nullable = true,
    string? Description = null,
    string? DefaultValue = null);

/// <summary>Host-neutral metadata required to render a Netezza NZPLSQL procedure.</summary>
public sealed record NetezzaProcedureDefinition(
    string Database,
    string Schema,
    string Name,
    string Returns,
    string Source,
    string? Arguments = null,
    bool ExecuteAsOwner = false,
    string? Description = null);
