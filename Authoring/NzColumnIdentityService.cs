using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.NetezzaSqlParser.Authoring;

/// <summary>
/// Column semantic identity for editor navigation. Definition, References,
/// Hover, catalog targets and Rename for columns are answered from the same
/// identity, never from text matching. Each call analyzes the text once; hosts
/// asking several questions about one text should reuse
/// <see cref="NzColumnIdentityAnalysis"/> from <see cref="Analyze"/>.
/// </summary>
public static class NzColumnIdentityService
{
    /// <summary>One reusable analysis of <paramref name="text"/>; null when it cannot be analyzed.</summary>
    public static NzColumnIdentityAnalysis? Analyze(string text, ISchemaProvider? schema = null,
        SqlDialect dialect = SqlDialect.Netezza)
        => NzColumnIdentityAnalysis.Analyze(text, schema, dialect);

    /// <summary>
    /// Resolves the column at <paramref name="offset"/>. Returns null when the
    /// offset is not on a column reference or projected column definition.
    /// </summary>
    public static SqlColumnIdentity? Resolve(string text, int offset, ISchemaProvider? schema = null,
        SqlDialect dialect = SqlDialect.Netezza)
        => Analyze(text, schema, dialect)?.Resolve(offset);

    /// <summary>The local definition of the column at the offset; null for physical, ambiguous or unresolved columns.</summary>
    public static SqlColumnOccurrence? GetDefinition(string text, int offset, ISchemaProvider? schema = null,
        SqlDialect dialect = SqlDialect.Netezza)
        => Analyze(text, schema, dialect)?.GetDefinition(offset);

    /// <summary>All occurrences sharing the identity of the column at the offset.</summary>
    public static IReadOnlyList<SqlColumnOccurrence> GetReferences(string text, int offset, bool includeDeclaration,
        ISchemaProvider? schema = null, SqlDialect dialect = SqlDialect.Netezza)
        => Analyze(text, schema, dialect)?.GetReferences(offset, includeDeclaration) ?? Array.Empty<SqlColumnOccurrence>();

    /// <summary>The catalog column a host may reveal for the column at the offset.</summary>
    public static SqlColumnCatalogTarget? GetCatalogTarget(string text, int offset, ISchemaProvider? schema = null,
        SqlDialect dialect = SqlDialect.Netezza)
        => Analyze(text, schema, dialect)?.GetCatalogTarget(offset);

    /// <summary>The occurrence to rename when the column at the offset is a renamable local column.</summary>
    public static SqlColumnOccurrence? PrepareRename(string text, int offset, ISchemaProvider? schema = null,
        SqlDialect dialect = SqlDialect.Netezza)
        => Analyze(text, schema, dialect)?.PrepareRename(offset);

    /// <summary>Safe local column rename edits; null when malformed or unsafe.</summary>
    public static IReadOnlyList<SqlTextEdit>? GetRenameEdits(string text, int offset, string newName,
        ISchemaProvider? schema = null, SqlDialect dialect = SqlDialect.Netezza)
        => Analyze(text, schema, dialect)?.GetRenameEdits(offset, newName);
}
