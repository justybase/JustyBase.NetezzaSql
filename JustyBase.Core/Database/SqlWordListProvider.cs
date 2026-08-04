namespace JustyBase.Core.Database;

/// <summary>
/// Neutral kind of a database word-list completion item, shared by all hosts.
/// The enum is the union of the host icon vocabularies (Avalonia <c>Glyph</c>,
/// WinForms <c>CompletionIconKind</c>); host adapters map to/from it.
/// </summary>
public enum SqlWordListKind
{
    Database,
    Schema,
    Table,
    View,
    Procedure,
    Synonym,
    ExternalTable,
    Function,
    Column,
    Alias,
    /// <summary>CTE (WITH) name or a column of a WITH table.</summary>
    With,
    /// <summary>Temp-table name or a column of a temp table.</summary>
    TempTable,
    /// <summary>Subquery alias or a column of a subquery.</summary>
    Subquery,
    Keyword,
    DataType,
    Variable,
    Snippet,
    Reference
}

/// <summary>
/// A single database word-list completion item in the host-agnostic contract.
/// <see cref="Label"/> is the insertable text and MAY be qualified
/// (<c>schema.object</c>) or unqualified (<c>object</c>) depending on the
/// dialect and the typed <see cref="SqlWordListRequest.Fragment"/> — each host
/// engine preserves its own insert-text fidelity (for example the WinForms DB2
/// path yields <c>SCHEMA.OBJECT</c> after the user typed <c>SCHEMA.</c>).
/// Consumers must treat <see cref="Label"/> as opaque insert text, not as a
/// plain identifier. <see cref="Detail"/> is the right-hand metadata (data
/// type, Table/View, kind); <see cref="Description"/> is optional documentation
/// text.
/// </summary>
public sealed record SqlWordListItem(
    string Label,
    SqlWordListKind Kind,
    string? Detail = null,
    string? Description = null);

/// <summary>
/// Query for the live-database word-list fallback. <see cref="Fragment"/> is the
/// typed prefix in dot notation (for example <c>JUST_DATA..dim</c> or
/// <c>ADMIN.</c>); the hint dictionaries are the same scoping data both hosts
/// already pass to their engines (alias → qualified table, subquery aliases,
/// WITH aliases, temp tables).
/// </summary>
public sealed record SqlWordListRequest(
    string Fragment,
    IReadOnlyDictionary<string, IReadOnlyList<string>> AliasDbTable,
    IReadOnlyDictionary<string, IReadOnlyList<string>> SubqueryHints,
    IReadOnlyDictionary<string, IReadOnlyList<string>> WithHints,
    IReadOnlyDictionary<string, IReadOnlyList<string>> TempTableHints,
    string? ConnectionName = null,
    string? DatabaseName = null)
{
    public static SqlWordListRequest Empty(string fragment, string? connectionName = null, string? databaseName = null)
        => new(
            fragment,
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase),
            connectionName,
            databaseName);

    /// <summary>
    /// Builds a request from raw SQL text and a caret offset without any SQL
    /// parsing: the fragment is the dotted word tail before the caret (for
    /// example <c>ADMIN.</c> or <c>JUST_DATA..dim</c>) and all hint dictionaries
    /// are empty. Quoted/backtick identifier delimiters (<c>"</c>, <c>`</c>,
    /// <c>[</c>, <c>]</c>) are included in the scan so fragments like
    /// <c>"SCHEMA"."TAB</c> keep their quotes. Use this as the default
    /// extractor for headless consumers that have no parser; hosts with a
    /// parser use the engine-backed extractor in
    /// <c>JustyBase.NetezzaSqlParser.Completion</c> which fills in the hints.
    /// </summary>
    public static SqlWordListRequest FromText(
        string sqlText,
        int cursorOffset,
        string? connectionName = null,
        string? databaseName = null)
    {
        if (string.IsNullOrEmpty(sqlText))
            return Empty(string.Empty, connectionName, databaseName);

        int end = Math.Clamp(cursorOffset, 0, sqlText.Length);
        int start = end;
        while (start > 0)
        {
            char c = sqlText[start - 1];
            if (char.IsLetterOrDigit(c) || c is '_' or '$' or '#' or '.' or '"' or '`' or '[' or ']')
                start--;
            else
                break;
        }

        return Empty(sqlText[start..end], connectionName, databaseName);
    }
}

/// <summary>
/// Builds a <see cref="SqlWordListRequest"/> for a caret position in raw SQL
/// text. The headless <see cref="SqlWordListService"/> uses this to translate
/// text + caret into the contract query; parser-backed implementations (in the
/// SQL parser library) additionally fill the alias/CTE/temp-table hint
/// dictionaries. The delegate keeps <c>JustyBase.Core</c> parser-agnostic.
/// </summary>
public delegate SqlWordListRequest SqlWordListRequestExtractor(
    string sqlText,
    int cursorOffset,
    string? connectionName,
    string? databaseName);

/// <summary>
/// Shared contract for the live-database word-list completion fallback used by
/// SQL editors (Avalonia RoslynPad and WinForms FCTB). Implementations adapt a
/// host-owned database/schema layer; the contract itself is UI-agnostic so
/// headless and LSP consumers can query either host engine.
/// </summary>
public interface ISqlDbWordListProvider
{
    IAsyncEnumerable<SqlWordListItem> GetWordsListAsync(
        SqlWordListRequest request,
        CancellationToken cancellationToken = default);
}
