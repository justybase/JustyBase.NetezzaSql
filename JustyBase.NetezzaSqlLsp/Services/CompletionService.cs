using JustyBase.Core.Database;
using JustyBase.NetezzaSqlParser.Completion;
using JustyBase.NetezzaSqlParser.Caching;
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
    /// Returns LSP completions at the given position through the shared
    /// headless completion orchestrator. The merge remains unconditional by
    /// default because LSP has no host-specific legacy merge policy.
    /// </summary>
    public static async Task<Protocol.CompletionList> GetCompletions(
        string text,
        int line,
        int character,
        ISchemaProvider? schema,
        SqlDialect dialect = SqlDialect.Netezza,
        ISqlDbWordListProvider? wordListProvider = null,
        CancellationToken cancellationToken = default,
        DocumentParsingCoordinator? coordinator = null,
        string? documentUri = null,
        string? connectionName = null,
        string? databaseName = null,
        Func<IReadOnlyList<JustyBase.NetezzaSqlParser.Completion.CompletionItem>, string, bool>? mergePolicy = null)
    {
        int offset = GetOffset(text, line, character);

        // Parity with the Avalonia editor: whitespace never opens the completion list.
        if (offset > 0 && CompletionGate.ShouldSuppressTrigger(text[offset - 1]))
            return new Protocol.CompletionList(false, Array.Empty<Protocol.CompletionItem>());

        var result = await CompletionOrchestrator.GetCompletions(
            text,
            offset,
            schema,
            dialect,
            wordListProvider,
            mergePolicy,
            coordinator,
            documentUri,
            connectionName,
            databaseName,
            new CompletionOrchestrationOptions { ForcedAutocomplete = true },
            cancellationToken);

        var mapped = new List<Protocol.CompletionItem>(
            result.EngineItems.Count + result.WordListItems.Count);
        foreach (var item in result.EngineItems)
        {
            mapped.Add(new Protocol.CompletionItem(
                Label: item.Label,
                Kind: MapKind(item.Kind),
                Detail: item.Detail,
                InsertText: null
            ));
        }

        foreach (var wordItem in result.WordListItems)
        {
            mapped.Add(new Protocol.CompletionItem(
                Label: wordItem.Label,
                Kind: MapWordListKind(wordItem.Kind),
                Detail: wordItem.Detail,
                InsertText: wordItem.Label
            ));
        }

        return new Protocol.CompletionList(false, mapped.ToArray());
    }

    private static int GetOffset(string text, int line, int character)
    {
        if (line < 0 || character < 0)
            return 0;

        int currentLine = 0;
        int lineStart = 0;
        for (int i = 0; i <= text.Length; i++)
        {
            if (currentLine == line)
            {
                return Math.Min(lineStart + character, text.Length);
            }

            if (i < text.Length && text[i] == '\n')
            {
                currentLine++;
                lineStart = i + 1;
            }
        }

        return text.Length;
    }
}
