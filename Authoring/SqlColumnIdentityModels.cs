namespace JustyBase.NetezzaSqlParser.Authoring;

/// <summary>How a column reference resolved.</summary>
public enum SqlColumnResolutionStatus
{
    Resolved,
    /// <summary>More than one visible source provides the column; never guessed.</summary>
    Ambiguous,
    Unresolved
}

/// <summary>The kind of relation that owns a resolved column.</summary>
public enum SqlColumnRelationKind
{
    Table,
    Cte,
    DerivedTable,
    ScriptLocalTable,
    OutputAlias
}

/// <summary>A physical catalog column. Database and schema are null when unknown.</summary>
public sealed record SqlCatalogColumn(string? Database, string? Schema, string Relation, string Column);

/// <summary>A column occurrence in the document (half-open UTF-16 offsets).</summary>
public sealed record SqlColumnOccurrence(int StartOffset, int EndOffset, bool IsDefinition);

/// <summary>
/// The semantic identity of the column at an offset. A local projection (CTE,
/// derived table, CTAS, output alias) has a document <see cref="Definition"/>;
/// a physical column has a <see cref="Catalog"/> target instead. <see cref="Origin"/>
/// is the physical column a local projection reduces to through plain column references.
/// </summary>
public sealed record SqlColumnIdentity(
    string Name,
    SqlColumnResolutionStatus Status,
    SqlColumnRelationKind? RelationKind,
    string? Relation,
    SqlColumnOccurrence? Definition,
    SqlCatalogColumn? Catalog,
    SqlCatalogColumn? Origin,
    IReadOnlyList<string> Candidates,
    IReadOnlyList<SqlColumnOccurrence> Occurrences)
{
    /// <summary>True when the identity navigates to a definition inside the document.</summary>
    public bool IsLocalDefinition => Definition is not null;

    /// <summary>Metadata data type of <see cref="Catalog"/> or <see cref="Origin"/>; null when unknown.</summary>
    public string? DataType { get; init; }
}

/// <summary>How a catalog target was reached.</summary>
public enum SqlColumnTargetVia
{
    /// <summary>The column itself is physical.</summary>
    Catalog,
    /// <summary>The proven physical origin of a local projection.</summary>
    Origin
}

/// <summary>
/// A catalog column a host can reveal in its schema browser. Physical columns
/// never get a document range; this is their navigation target.
/// </summary>
public sealed record SqlColumnCatalogTarget(string? Database, string? Schema, string Relation, string Column, SqlColumnTargetVia Via);

/// <summary>
/// What column hover shows, from the identity that drives Definition and
/// References. <see cref="Origin"/> and <see cref="DataType"/> are null when
/// they cannot be proven.
/// </summary>
public sealed record SqlColumnHoverInfo(
    string Name,
    SqlColumnResolutionStatus Status,
    SqlColumnRelationKind? RelationKind,
    string? Relation,
    SqlCatalogColumn? Origin,
    string? DataType,
    IReadOnlyList<string> Candidates);
