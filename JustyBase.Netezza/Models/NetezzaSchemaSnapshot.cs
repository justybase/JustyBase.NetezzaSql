namespace JustyBase.Netezza.Models;

/// <summary>Catalog object kind reported by <c>_V_OBJECT_DATA.OBJTYPE</c> (modern catalog SQL).</summary>
public enum NetezzaObjectKind
{
    Table,
    View,
    Procedure,
    Function,
    Sequence,
    Synonym,
    ExternalTable,
    Fluid,
    Aggregate,
    Index,
    Partition,
    Other
}

/// <summary>
/// Immutable database metadata passed from a host-specific cache to SQL authoring.
/// It deliberately contains no ADO.NET, UI, or application-cache types.
/// </summary>
/// <param name="Tables">All catalog objects (tables, views, procedures, functions, sequences, synonyms, …) in the snapshot.</param>
/// <param name="Version">Monotonically increasing version number for cache invalidation.</param>
/// <param name="Procedures">Optional procedure definitions for hosts that surface CALL completion.</param>
/// <param name="ExternalTables">Optional external tables (also applied into the schema provider as tables).</param>
/// <param name="IsPartial"><see langword="true"/> when loading was cancelled or one database failed and the snapshot is incomplete.</param>
/// <param name="LoadedAt">Moment the snapshot was produced (cache freshness bookkeeping).</param>
public sealed record NetezzaSchemaSnapshot(
    IReadOnlyList<NetezzaSchemaTable> Tables,
    long Version = 0,
    IReadOnlyList<NetezzaProcedureDefinition>? Procedures = null,
    IReadOnlyList<NetezzaSchemaTable>? ExternalTables = null,
    bool IsPartial = false,
    DateTimeOffset LoadedAt = default)
{
    /// <summary>A static empty snapshot singleton.</summary>
    public static NetezzaSchemaSnapshot Empty { get; } = new([], 0);
}

/// <summary>Metadata for a single catalog object (table, view, procedure, synonym, …).</summary>
/// <param name="Name">Object name.</param>
/// <param name="Schema">Owning schema, if known.</param>
/// <param name="Database">Owning database, if known.</param>
/// <param name="Kind">Catalog object kind.</param>
/// <param name="IsView"><see langword="true"/> when this object is a view.</param>
/// <param name="Columns">Column definitions; <see langword="null"/> when unknown.</param>
/// <param name="Description">Optional object description.</param>
/// <param name="Owner">Optional object owner.</param>
/// <param name="CatalogId">Optional catalog object id (<c>_V_OBJECT_DATA.OBJID</c>), used to correlate column rows.</param>
/// <param name="Created">Optional catalog creation timestamp (<c>_V_OBJECT_DATA.CREATEDATE</c>).</param>
/// <param name="TextType">Optional raw catalog object type (<c>_V_OBJECT_DATA.OBJTYPE</c>, e.g. <c>TABLE</c>, <c>FLUID</c>).</param>
/// <param name="Keys">Declared key constraints (primary and foreign) for this object; <see langword="null"/> when unknown.</param>
public sealed record NetezzaSchemaTable(
    string Name,
    string? Schema = null,
    string? Database = null,
    NetezzaObjectKind Kind = NetezzaObjectKind.Table,
    bool IsView = false,
    IReadOnlyList<NetezzaSchemaColumn>? Columns = null,
    string? Description = null,
    string? Owner = null,
    int CatalogId = 0,
    DateTime? Created = null,
    string? TextType = null,
    IReadOnlyList<NetezzaReferenceKey>? Keys = null);

/// <summary>
/// Neutral declared key metadata from the catalog. One record is one constraint
/// (primary key or foreign key); multi-column keys preserve catalog order.
/// This model deliberately carries no parser types.
/// </summary>
/// <param name="ConstraintName">Catalog constraint name, if available.</param>
/// <param name="ConstraintType">Raw catalog CONTYPE (<c>p</c> primary, <c>f</c> foreign, <c>u</c> unique).</param>
/// <param name="Columns">Ordered local column list.</param>
/// <param name="ReferencedDatabase">Declared referenced database for foreign keys.</param>
/// <param name="ReferencedSchema">Declared referenced schema for foreign keys.</param>
/// <param name="ReferencedTable">Declared referenced table for foreign keys.</param>
/// <param name="ReferencedColumns">Ordered referenced column list for foreign keys.</param>
public sealed record NetezzaReferenceKey(
    string ConstraintName,
    string ConstraintType,
    IReadOnlyList<string> Columns,
    string? ReferencedDatabase = null,
    string? ReferencedSchema = null,
    string? ReferencedTable = null,
    IReadOnlyList<string>? ReferencedColumns = null)
{
    /// <summary><see langword="true"/> for primary-key constraints.</summary>
    public bool IsPrimaryKey => string.Equals(ConstraintType, "p", StringComparison.OrdinalIgnoreCase);

    /// <summary><see langword="true"/> for foreign-key constraints.</summary>
    public bool IsForeignKey => string.Equals(ConstraintType, "f", StringComparison.OrdinalIgnoreCase);
}

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

/// <summary>
/// One raw key-constraint row from the catalog (one local column of one constraint).
/// Rows are grouped into <see cref="NetezzaReferenceKey"/> by constraint.
/// </summary>
/// <param name="ObjectId">Catalog object id of the constrained table.</param>
/// <param name="Schema">Schema of the constrained table.</param>
/// <param name="Relation">Name of the constrained table.</param>
/// <param name="ConstraintName">Catalog constraint name.</param>
/// <param name="ConstraintType">Raw catalog CONTYPE (<c>p</c>, <c>f</c>, <c>u</c>).</param>
/// <param name="Column">Local column participating in the key.</param>
/// <param name="ReferencedDatabase">Declared referenced database for foreign keys.</param>
/// <param name="ReferencedSchema">Declared referenced schema for foreign keys.</param>
/// <param name="ReferencedTable">Declared referenced table for foreign keys.</param>
/// <param name="ReferencedColumn">Referenced column for foreign keys.</param>
public sealed record NetezzaSchemaKeyRow(
    int ObjectId,
    string? Schema,
    string? Relation,
    string ConstraintName,
    string ConstraintType,
    string Column,
    string? ReferencedDatabase = null,
    string? ReferencedSchema = null,
    string? ReferencedTable = null,
    string? ReferencedColumn = null);

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
