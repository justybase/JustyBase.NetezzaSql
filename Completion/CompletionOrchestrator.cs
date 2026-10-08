using JustyBase.Core.Database;
using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.NetezzaSqlParser.Caching;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.NetezzaSqlParser.Completion;

/// <summary>Options controlling a single completion orchestration request.</summary>
public sealed record CompletionOrchestrationOptions
{
    /// <summary>
    /// True for a dot or explicit request. Passive requests use the smaller
    /// statement limit on oversized documents.
    /// </summary>
    public bool ForcedAutocomplete { get; init; } = true;

    /// <summary>
    /// Optional host callback that hydrates columns for a qualified identifier.
    /// Returning true causes the engine to run once more against the same slice.
    /// </summary>
    public Func<string, int, IReadOnlyList<CompletionItem>, bool>? HydrateColumns { get; init; }

    /// <summary>
    /// Neutral trigger kind. Automatic requests must not offer query-continuation
    /// keywords after a completed relation reference; explicit requests may.
    /// Defaults to <see cref="CompletionTriggerKind.Explicit"/> so existing callers
    /// keep their current behavior.
    /// </summary>
    public CompletionTriggerKind TriggerKind { get; init; } = CompletionTriggerKind.Explicit;
}

/// <summary>Neutral completion result shared by UI hosts and headless consumers.</summary>
public sealed record CompletionOrchestrationResult(
    IReadOnlyList<CompletionItem> EngineItems,
    IReadOnlyList<SqlWordListItem> WordListItems);

/// <summary>
/// Headless engine-first completion pipeline. It contains no editor or UI
/// dependencies; consumers map the neutral result to their own protocol.
/// </summary>
public static class CompletionOrchestrator
{
    public static async Task<CompletionOrchestrationResult> GetCompletions(
        string text,
        int offset,
        ISchemaProvider? schema,
        SqlDialect dialect = SqlDialect.Netezza,
        ISqlDbWordListProvider? wordListProvider = null,
        Func<IReadOnlyList<CompletionItem>, string, bool>? mergePolicy = null,
        DocumentParsingCoordinator? coordinator = null,
        string? documentUri = null,
        string? connectionName = null,
        string? databaseName = null,
        CompletionOrchestrationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        cancellationToken.ThrowIfCancellationRequested();

        options ??= new CompletionOrchestrationOptions();
        offset = Math.Clamp(offset, 0, text.Length);

        IReadOnlyList<CompletionItem> engineItems = [];
        string engineSql = text;
        int engineCursor = offset;
        (Dictionary<string, List<string>> WithHints,
         Dictionary<string, List<string>> TempTableHints,
         Dictionary<string, List<string>> AliasDbTable)? scopeHints = null;
        bool engineRan = false;

        int lineCount = SqlPerformancePolicy.CountLines(text);
        if (SqlAutocompleteWindow.ShouldRunEngine(
                text, offset, lineCount, options.ForcedAutocomplete))
        {
            engineRan = true;
            (engineSql, engineCursor) = SqlAutocompleteWindow.SliceForEngine(
                text, offset, lineCount, options.ForcedAutocomplete);

            var engine = new NzCompletionEngine(
                schema,
                coordinator,
                catalog: DialectRuntime.AuthoringCatalogOrNull(dialect),
                dialect: dialect,
                activeDatabase: databaseName);
            engine.SetDocumentUri(documentUri);
            engine.TriggerKind = options.TriggerKind;
            engineItems = engine.GetCompletions(engineSql, engineCursor);

            if (options.HydrateColumns is not null
                && options.HydrateColumns(engineSql, engineCursor, engineItems))
            {
                engineItems = engine.GetCompletions(engineSql, engineCursor);
            }

            scopeHints = engine.GetScopeHints();
        }

        var distinctEngineItems = DistinctEngineItems(engineItems);
        bool shouldRunWordList = wordListProvider is not null
            && (mergePolicy is null || mergePolicy(distinctEngineItems, text));

        if (!shouldRunWordList)
            return new CompletionOrchestrationResult(distinctEngineItems, []);

        cancellationToken.ThrowIfCancellationRequested();

        // Reuse the same slice and hints when the engine ran. If a passive
        // request skipped the engine, the builder supplies the forced fallback
        // slice required by the existing word-list contract.
        SqlWordListRequest request;
        if (engineRan && scopeHints is { } hints)
        {
            request = new EngineSqlWordListRequestBuilder(dialect).BuildFromEngineResult(
                engineSql, engineCursor, connectionName, databaseName,
                hints);
        }
        else
        {
            request = new EngineSqlWordListRequestBuilder(dialect).Build(
                text, offset, connectionName, databaseName);
        }

        var wordListItems = new List<SqlWordListItem>();
        var seen = new HashSet<string>(
            distinctEngineItems.Select(item => item.Label),
            StringComparer.OrdinalIgnoreCase);

        await foreach (var item in wordListProvider!.GetWordsListAsync(request, cancellationToken)
                           .WithCancellation(cancellationToken)
                           .ConfigureAwait(false))
        {
            if (seen.Add(item.Label))
                wordListItems.Add(item);
        }

        return new CompletionOrchestrationResult(distinctEngineItems, wordListItems);
    }

    private static IReadOnlyList<CompletionItem> DistinctEngineItems(
        IReadOnlyList<CompletionItem> items)
    {
        var result = new List<CompletionItem>(items.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (seen.Add(item.Label))
                result.Add(item);
        }

        return result;
    }
}
