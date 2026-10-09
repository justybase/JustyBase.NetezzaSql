using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.NetezzaSqlParser.Authoring;

/// <summary>
/// Column identities of one document text, collected once. Definition,
/// References, Hover, catalog targets and Rename are all answered from this
/// single analysis, so a host that asks several questions about the same text
/// does not tokenize, parse and resolve it again. Instances are immutable and
/// owned by the caller; they hold no cache and see no later edits.
/// </summary>
public sealed class NzColumnIdentityAnalysis
{
    private readonly NzColumnIdentityCollector _collector;
    private readonly ISchemaProvider? _schema;
    private readonly SqlDialect _dialect;

    private NzColumnIdentityAnalysis(string text, NzColumnIdentityCollector collector, ISchemaProvider? schema, SqlDialect dialect)
    {
        Text = text;
        _collector = collector;
        _schema = schema;
        _dialect = dialect;
    }

    /// <summary>The analyzed text.</summary>
    public string Text { get; }

    /// <summary>Analyzes <paramref name="text"/>; null when it is empty or cannot be analyzed.</summary>
    public static NzColumnIdentityAnalysis? Analyze(string text, ISchemaProvider? schema = null, SqlDialect dialect = SqlDialect.Netezza)
    {
        if (string.IsNullOrEmpty(text)) return null;
        try
        {
            return new NzColumnIdentityAnalysis(text, NzColumnIdentityCollector.Collect(text, schema, dialect), schema, dialect);
        }
        catch
        {
            // Authoring must stay available while SQL is incomplete.
            return null;
        }
    }

    /// <summary>The column at <paramref name="offset"/>; null when the offset is not on a column.</summary>
    public SqlColumnIdentity? Resolve(int offset)
    {
        var key = KeyAt(offset);
        return key is null ? null : Identity(key);
    }

    /// <summary>The local definition of the column; null for physical, ambiguous or unresolved columns.</summary>
    public SqlColumnOccurrence? GetDefinition(int offset) => Resolve(offset)?.Definition;

    /// <summary>All occurrences sharing the identity of the column at the offset.</summary>
    public IReadOnlyList<SqlColumnOccurrence> GetReferences(int offset, bool includeDeclaration)
    {
        var identity = Resolve(offset);
        if (identity is null || identity.Status != SqlColumnResolutionStatus.Resolved)
            return Array.Empty<SqlColumnOccurrence>();
        return identity.Occurrences.Where(occurrence => includeDeclaration || !occurrence.IsDefinition).ToArray();
    }

    /// <summary>
    /// The catalog column a host may reveal: the column itself when physical,
    /// otherwise the proven physical origin of a local projection. Null for
    /// computed, ambiguous and unresolved columns.
    /// </summary>
    public SqlColumnCatalogTarget? GetCatalogTarget(int offset)
    {
        var identity = Resolve(offset);
        if (identity is not { Status: SqlColumnResolutionStatus.Resolved }) return null;
        if (identity.Catalog is { } catalog)
            return new SqlColumnCatalogTarget(catalog.Database, catalog.Schema, catalog.Relation, catalog.Column, SqlColumnTargetVia.Catalog);
        if (identity.Origin is { } origin)
            return new SqlColumnCatalogTarget(origin.Database, origin.Schema, origin.Relation, origin.Column, SqlColumnTargetVia.Origin);
        return null;
    }

    /// <summary>Column hover facts; null when the offset is not on a column.</summary>
    public SqlColumnHoverInfo? GetHoverInfo(int offset)
    {
        var identity = Resolve(offset);
        if (identity is null) return null;
        var origin = identity.Status == SqlColumnResolutionStatus.Resolved ? identity.Catalog ?? identity.Origin : null;
        return new SqlColumnHoverInfo(identity.Name, identity.Status, identity.RelationKind, identity.Relation, origin,
            origin is null ? null : identity.DataType, identity.Candidates);
    }

    /// <summary>
    /// The occurrence to rename when the column at the offset is a renamable
    /// local column: a resolved identity whose definition is spelled by an
    /// alias or explicit CTE column list. Physical, ambiguous and unresolved
    /// columns, pass-through projections, <c>*</c> expansions and columns that
    /// feed another projection return null.
    /// </summary>
    public SqlColumnOccurrence? PrepareRename(int offset)
    {
        var key = KeyAt(offset);
        var identity = key is null ? null : Identity(key);
        if (key is null || identity is not { Status: SqlColumnResolutionStatus.Resolved, Definition: { } definition }) return null;
        if (Text.AsSpan(definition.StartOffset, definition.EndOffset - definition.StartOffset).SequenceEqual("*")) return null;
        if (identity.Occurrences.Any(occurrence => HasOtherOccurrenceAt(key, occurrence.StartOffset, occurrence.EndOffset))) return null;
        return identity.Occurrences.FirstOrDefault(occurrence => occurrence.StartOffset <= offset && offset <= occurrence.EndOffset)
               ?? definition;
    }

    /// <summary>
    /// Edits renaming the local column at the offset everywhere its identity
    /// occurs, using the relation rename name and quoting policy. Null when the
    /// name is malformed or the rename is unsafe: the edited text must resolve
    /// every column occurrence exactly as before, which rejects capture,
    /// shadowing, new ambiguity and sibling collisions. Physical catalog columns
    /// are never renamed and no DDL is produced.
    /// </summary>
    public IReadOnlyList<SqlTextEdit>? GetRenameEdits(int offset, string newName)
    {
        if (NzRenameService.FormatReplacement("a", newName, _dialect) is null || PrepareRename(offset) is null) return null;
        var identity = Resolve(offset)!;
        var edits = new List<SqlTextEdit>();
        foreach (var occurrence in identity.Occurrences.OrderBy(occurrence => occurrence.StartOffset))
        {
            var replacement = NzRenameService.FormatReplacement(
                Text[occurrence.StartOffset..occurrence.EndOffset], newName, _dialect);
            if (replacement is null) return null;
            edits.Add(new SqlTextEdit(occurrence.StartOffset, occurrence.EndOffset, replacement));
        }

        var renamed = Text;
        for (var i = edits.Count - 1; i >= 0; i--)
            renamed = renamed[..edits[i].StartOffset] + edits[i].NewText + renamed[edits[i].EndOffset..];
        var after = Analyze(renamed, _schema, _dialect);
        if (after is null) return null;

        int MapOffset(int position)
        {
            var delta = 0;
            foreach (var edit in edits)
            {
                if (edit.EndOffset > position) break;
                delta += edit.NewText.Length - (edit.EndOffset - edit.StartOffset);
            }
            return position + delta;
        }

        (int, int) MapRange(int start, int end)
        {
            var edit = edits.FirstOrDefault(candidate => candidate.StartOffset == start && candidate.EndOffset == end);
            var mappedStart = MapOffset(start);
            return edit is null ? (mappedStart, MapOffset(end)) : (mappedStart, mappedStart + edit.NewText.Length);
        }

        return Partition(MapRange).SequenceEqual(after.Partition((start, end) => (start, end)), StringComparer.Ordinal)
            ? edits
            : null;
    }

    private string? KeyAt(int offset)
    {
        // A projection like `SELECT CUSTOMER_ID FROM T` is both a reference and a
        // definition; the reference wins. Among definitions, a relation column
        // wins over the output-alias view of the same alias.
        var occurrences = _collector.Occurrences;
        var occurrence = occurrences
            .Where(candidate => candidate.Start <= offset && offset < candidate.End)
            .OrderBy(candidate => candidate.IsDefinition ? 1 : 0)
            .ThenBy(candidate => candidate.Key.StartsWith("O|", StringComparison.Ordinal) ? 1 : 0)
            .FirstOrDefault()
            ?? occurrences
                .Where(candidate => candidate.End == offset)
                .OrderBy(candidate => candidate.IsDefinition ? 1 : 0)
                .FirstOrDefault();
        return occurrence is not null && _collector.Identities.ContainsKey(occurrence.Key) ? occurrence.Key : null;
    }

    private SqlColumnIdentity? Identity(string key)
    {
        if (!_collector.Identities.TryGetValue(key, out var info)) return null;
        var occurrences = _collector.Occurrences
            .Where(candidate => candidate.Key == key)
            .OrderBy(candidate => candidate.Start)
            .Select(candidate => new SqlColumnOccurrence(candidate.Start, candidate.End, candidate.IsDefinition))
            .Distinct()
            .ToArray();
        var definition = info.Definition is { } range ? new SqlColumnOccurrence(range.Start, range.End, true) : null;
        return new SqlColumnIdentity(info.Name, info.Status, info.RelationKind, info.Relation, definition,
            info.Catalog, info.Origin, info.Candidates, occurrences)
        {
            DataType = _collector.TypeOf(info.Catalog ?? info.Origin),
        };
    }

    /// <summary>
    /// True when another identity also occurs at exactly [start, end): as a
    /// reference, or as a definition other than the output-alias view of the
    /// same alias token.
    /// </summary>
    private bool HasOtherOccurrenceAt(string key, int start, int end)
        => _collector.Occurrences.Any(occurrence => occurrence.Key != key && occurrence.Start == start && occurrence.End == end
            && (!occurrence.IsDefinition || !occurrence.Key.StartsWith("O|", StringComparison.Ordinal)));

    /// <summary>One signature per identity: its status and occurrence ranges after <paramref name="mapRange"/>.</summary>
    private IReadOnlyList<string> Partition(Func<int, int, (int Start, int End)> mapRange)
    {
        var groups = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var occurrence in _collector.Occurrences)
        {
            var (start, end) = mapRange(occurrence.Start, occurrence.End);
            if (!groups.TryGetValue(occurrence.Key, out var ranges)) groups[occurrence.Key] = ranges = new SortedSet<string>(StringComparer.Ordinal);
            ranges.Add($"{start}:{end}:{(occurrence.IsDefinition ? 'd' : 'r')}");
        }
        return groups
            .Select(group => $"{(_collector.Identities.TryGetValue(group.Key, out var info) ? info.Status.ToString() : "?")}|{string.Join(',', group.Value)}")
            .OrderBy(signature => signature, StringComparer.Ordinal)
            .ToArray();
    }
}
