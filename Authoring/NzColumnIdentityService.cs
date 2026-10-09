using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.NetezzaSqlParser.Authoring;

/// <summary>
/// Column semantic identity for editor navigation. Definition and References
/// for columns are answered from the same identity, never from text matching.
/// </summary>
public static class NzColumnIdentityService
{
    /// <summary>
    /// Resolves the column at <paramref name="offset"/>. Returns null when the
    /// offset is not on a column reference or projected column definition.
    /// </summary>
    public static SqlColumnIdentity? Resolve(string text, int offset, ISchemaProvider? schema = null,
        SqlDialect dialect = SqlDialect.Netezza)
    {
        if (string.IsNullOrEmpty(text)) return null;
        try
        {
            var collector = NzColumnIdentityCollector.Collect(text, schema, dialect);
            var occurrence = FindAt(collector.Occurrences, offset);
            if (occurrence is null || !collector.Identities.TryGetValue(occurrence.Key, out var info)) return null;

            var occurrences = collector.Occurrences
                .Where(candidate => candidate.Key == occurrence.Key)
                .OrderBy(candidate => candidate.Start)
                .Select(candidate => new SqlColumnOccurrence(candidate.Start, candidate.End, candidate.IsDefinition))
                .Distinct()
                .ToArray();
            var definition = info.Definition is { } range ? new SqlColumnOccurrence(range.Start, range.End, true) : null;
            return new SqlColumnIdentity(info.Name, info.Status, info.RelationKind, info.Relation, definition,
                info.Catalog, info.Origin, info.Candidates, occurrences);
        }
        catch
        {
            // Authoring must stay available while SQL is incomplete.
            return null;
        }
    }

    /// <summary>The local definition of the column at the offset; null for physical, ambiguous or unresolved columns.</summary>
    public static SqlColumnOccurrence? GetDefinition(string text, int offset, ISchemaProvider? schema = null,
        SqlDialect dialect = SqlDialect.Netezza)
        => Resolve(text, offset, schema, dialect)?.Definition;

    /// <summary>All occurrences sharing the identity of the column at the offset.</summary>
    public static IReadOnlyList<SqlColumnOccurrence> GetReferences(string text, int offset, bool includeDeclaration,
        ISchemaProvider? schema = null, SqlDialect dialect = SqlDialect.Netezza)
    {
        var identity = Resolve(text, offset, schema, dialect);
        if (identity is null || identity.Status != SqlColumnResolutionStatus.Resolved)
            return Array.Empty<SqlColumnOccurrence>();
        return identity.Occurrences.Where(occurrence => includeDeclaration || !occurrence.IsDefinition).ToArray();
    }

    private static NzColumnIdentityCollector.Occurrence? FindAt(IReadOnlyList<NzColumnIdentityCollector.Occurrence> occurrences, int offset)
    {
        // A projection like `SELECT CUSTOMER_ID FROM T` is both a reference and a
        // definition; the reference wins. Among definitions, a relation column
        // wins over the output-alias view of the same alias.
        return occurrences
            .Where(candidate => candidate.Start <= offset && offset < candidate.End)
            .OrderBy(candidate => candidate.IsDefinition ? 1 : 0)
            .ThenBy(candidate => candidate.Key.StartsWith("O|", StringComparison.Ordinal) ? 1 : 0)
            .FirstOrDefault()
            ?? occurrences
                .Where(candidate => candidate.End == offset)
                .OrderBy(candidate => candidate.IsDefinition ? 1 : 0)
                .FirstOrDefault();
    }
}
