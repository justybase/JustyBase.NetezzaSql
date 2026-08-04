using JustyBase.Core.Database;
using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.NetezzaSqlParser.Dialects;

namespace JustyBase.NetezzaSqlParser.Completion;

/// <summary>
/// Parser-backed <see cref="SqlWordListRequestExtractor"/>: mirrors what the
/// Avalonia host does before querying its live-DB word list — slices the SQL to
/// the current statement/window, runs <see cref="NzCompletionEngine"/> so the
/// scope collector populates, then reads the alias/CTE/temp-table hints via
/// <see cref="NzCompletionEngine.GetScopeHints"/> and attaches them to the
/// request. Used by headless consumers (LSP, tools) through
/// <see cref="SqlWordListService"/>.
/// </summary>
/// <remarks>
/// The fragment comes from the same text-based scan as
/// <see cref="SqlWordListRequest.FromText"/>, applied to the engine-sliced SQL
/// so the fragment matches what the engine saw. Hints are always merged with
/// the request; deciding whether to skip the DB fallback (host
/// <c>SqlCompletionMergePolicy</c>) remains a host concern.
/// </remarks>
public sealed class EngineSqlWordListRequestBuilder
{
    private readonly SqlDialect _dialect;

    public EngineSqlWordListRequestBuilder(SqlDialect dialect = SqlDialect.Netezza)
    {
        _dialect = dialect;
    }

    public SqlWordListRequest Build(
        string sqlText,
        int cursorOffset,
        string? connectionName,
        string? databaseName)
    {
        if (string.IsNullOrEmpty(sqlText))
            return SqlWordListRequest.FromText(sqlText ?? string.Empty, cursorOffset, connectionName, databaseName);

        int lineCount = SqlPerformancePolicy.CountLines(sqlText);
        (string engineSql, int engineCursor) = SqlAutocompleteWindow.SliceForEngine(
            sqlText, cursorOffset, lineCount, forcedAutocomplete: true);

        var engine = new NzCompletionEngine(
            catalog: DialectRuntime.AuthoringCatalogOrNull(_dialect),
            dialect: _dialect);
        engine.GetCompletions(engineSql, engineCursor);
        var (withHints, tempTableHints, aliasDbTable) = engine.GetScopeHints();

        SqlWordListRequest fromEngine = SqlWordListRequest.FromText(
            engineSql, engineCursor, connectionName, databaseName);

        return fromEngine with
        {
            AliasDbTable = ToReadOnly(aliasDbTable),
            WithHints = ToReadOnly(withHints),
            TempTableHints = ToReadOnly(tempTableHints)
        };
    }

    /// <summary>
    /// Rebuilds an engine hint dictionary as the contract's read-only shape,
    /// preserving the ordinal-ignore-case comparer used by the hosts.
    /// </summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ToReadOnly(
        Dictionary<string, List<string>> source)
        => source.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value,
            StringComparer.OrdinalIgnoreCase);
}
