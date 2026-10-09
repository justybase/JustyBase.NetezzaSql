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
}
