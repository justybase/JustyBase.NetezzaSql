using JustyBase.Core.Database;
using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.NetezzaSqlParser.Completion;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Visitor;
using JustyBase.NetezzaSqlLsp.Protocol;

namespace JustyBase.NetezzaSqlLsp.Services;

/// <summary>Provides LSP completion items for Netezza SQL.</summary>
public static class CompletionService
{
    private static Protocol.CompletionItemKind MapKind(CompletionKind kind) => kind switch
    {
        CompletionKind.Keyword => Protocol.CompletionItemKind.Keyword,
        CompletionKind.Table => Protocol.CompletionItemKind.Struct,
        CompletionKind.View => Protocol.CompletionItemKind.Class,
        CompletionKind.ExternalTable => Protocol.CompletionItemKind.Struct,
        CompletionKind.Column => Protocol.CompletionItemKind.Field,
        CompletionKind.Function => Protocol.CompletionItemKind.Function,
        CompletionKind.Schema => Protocol.CompletionItemKind.Module,
        CompletionKind.Database => Protocol.CompletionItemKind.Folder,
        CompletionKind.Alias => Protocol.CompletionItemKind.Variable,
        CompletionKind.Cte => Protocol.CompletionItemKind.Class,
        CompletionKind.DataType => Protocol.CompletionItemKind.TypeParameter,
        CompletionKind.Snippet => Protocol.CompletionItemKind.Snippet,
        CompletionKind.Variable => Protocol.CompletionItemKind.Variable,
        _ => Protocol.CompletionItemKind.Text
    };

    /// <summary>Maps the shared word-list contract kinds onto LSP item kinds.</summary>
    private static Protocol.CompletionItemKind MapWordListKind(SqlWordListKind kind) => kind switch
    {
        SqlWordListKind.Database => Protocol.CompletionItemKind.Folder,
        SqlWordListKind.Schema => Protocol.CompletionItemKind.Module,
        SqlWordListKind.Table => Protocol.CompletionItemKind.Struct,
        SqlWordListKind.View => Protocol.CompletionItemKind.Class,
        SqlWordListKind.ExternalTable => Protocol.CompletionItemKind.Struct,
        SqlWordListKind.Procedure => Protocol.CompletionItemKind.Function,
        SqlWordListKind.Function => Protocol.CompletionItemKind.Function,
        SqlWordListKind.Column => Protocol.CompletionItemKind.Field,
        SqlWordListKind.Alias => Protocol.CompletionItemKind.Variable,
        SqlWordListKind.With => Protocol.CompletionItemKind.Class,
        SqlWordListKind.TempTable => Protocol.CompletionItemKind.Struct,
        SqlWordListKind.Subquery => Protocol.CompletionItemKind.Class,
        SqlWordListKind.Keyword => Protocol.CompletionItemKind.Keyword,
        SqlWordListKind.DataType => Protocol.CompletionItemKind.TypeParameter,
        SqlWordListKind.Variable => Protocol.CompletionItemKind.Variable,
        SqlWordListKind.Snippet => Protocol.CompletionItemKind.Snippet,
        _ => Protocol.CompletionItemKind.Text
    };

    /// <summary>
    /// Returns LSP completions at the given position. When a live-database
    /// word-list provider is supplied, its neutral items are merged after the
    /// engine items (deduplicated by label) through the shared headless
    /// <see cref="SqlWordListService"/> — the <c>ISqlDbWordListProvider</c> seam.
    /// The merge is unconditional (no host <c>SqlCompletionMergePolicy</c>
    /// gate): a provider always contributes items alongside the engine result,
    /// which is intentional for a headless seam. Hosts that want the
    /// "skip DB fallback when the engine already found useful results" policy
    /// must apply it before registering a provider.
    /// </summary>
    public static async Task<Protocol.CompletionList> GetCompletions(
        string text,
        int line,
        int character,
        ISchemaProvider? schema,
        SqlDialect dialect = SqlDialect.Netezza,
        ISqlDbWordListProvider? wordListProvider = null,
        CancellationToken cancellationToken = default)
    {
        // Convert line/character to offset
        int offset = 0;
        int currentLine = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (currentLine == line)
            {
                offset = Math.Min(i + character, text.Length);
                break;
            }
            if (text[i] == '\n')
                currentLine++;
        }

        var catalog = DialectRuntime.AuthoringCatalogOrNull(dialect);
        var engine = new NzCompletionEngine(schema, catalog: catalog, dialect: dialect);
        var items = engine.GetCompletions(text, offset);

        var mapped = new List<Protocol.CompletionItem>(items.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            seen.Add(item.Label);
            mapped.Add(new Protocol.CompletionItem(
                Label: item.Label,
                Kind: MapKind(item.Kind),
                Detail: item.Detail,
                InsertText: null
            ));
        }

        if (wordListProvider is not null)
        {
            var builder = new EngineSqlWordListRequestBuilder(dialect);
            var service = new SqlWordListService(wordListProvider, builder.Build);
            await foreach (var wordItem in service.GetWordsListAsync(
                               text, offset, cancellationToken: cancellationToken))
            {
                if (!seen.Add(wordItem.Label))
                    continue;
                mapped.Add(new Protocol.CompletionItem(
                    Label: wordItem.Label,
                    Kind: MapWordListKind(wordItem.Kind),
                    Detail: wordItem.Detail,
                    InsertText: wordItem.Label
                ));
            }
        }

        return new Protocol.CompletionList(false, mapped.ToArray());
    }
}
