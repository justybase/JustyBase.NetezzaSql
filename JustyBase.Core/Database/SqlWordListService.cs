namespace JustyBase.Core.Database;

/// <summary>
/// Headless orchestrator over the <see cref="ISqlDbWordListProvider"/> contract:
/// turns raw SQL text + caret offset into a <see cref="SqlWordListRequest"/>
/// (via an injected <see cref="SqlWordListRequestExtractor"/>) and streams the
/// provider's neutral items. This is the shared seam for non-UI consumers such
/// as the LSP server, CLI tools, and tests — no host or editor primitives are
/// involved.
/// </summary>
/// <remarks>
/// The default extractor (<see cref="SqlWordListRequest.FromText"/>) computes
/// only the dotted word fragment and no hints. Consumers that can parse SQL
/// should inject the parser-backed extractor
/// (<c>JustyBase.NetezzaSqlParser.Completion.EngineSqlWordListRequestBuilder</c>)
/// so alias/CTE/temp-table hints reach the provider.
/// </remarks>
public sealed class SqlWordListService
{
    private readonly ISqlDbWordListProvider _provider;
    private readonly SqlWordListRequestExtractor _extractor;

    public SqlWordListService(
        ISqlDbWordListProvider provider,
        SqlWordListRequestExtractor? extractor = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _extractor = extractor ?? SqlWordListRequest.FromText;
    }

    public IAsyncEnumerable<SqlWordListItem> GetWordsListAsync(
        string sqlText,
        int cursorOffset,
        string? connectionName = null,
        string? databaseName = null,
        CancellationToken cancellationToken = default)
    {
        SqlWordListRequest request = _extractor(sqlText, cursorOffset, connectionName, databaseName);
        return _provider.GetWordsListAsync(request, cancellationToken);
    }
}
