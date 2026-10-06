using System.Globalization;
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
        Func<IReadOnlyList<JustyBase.NetezzaSqlParser.Completion.CompletionItem>, string, bool>? mergePolicy = null,
        int? triggerKind = null)
    {
        int offset = GetOffset(text, line, character);

        // Parity with the Avalonia editor: whitespace never opens the completion list.
        var explicitRequest = triggerKind == (int)Protocol.CompletionTriggerKind.Invoked;
        if (!explicitRequest && offset > 0 && CompletionGate.ShouldSuppressTrigger(text[offset - 1]))
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
                InsertText: item.InsertText,
                SortText: GetSortText(item.Kind, item.Priority, item.Label)
            ));
        }

        foreach (var wordItem in result.WordListItems)
        {
            mapped.Add(new Protocol.CompletionItem(
                Label: wordItem.Label,
                Kind: MapWordListKind(wordItem.Kind),
                Detail: wordItem.Detail,
                InsertText: wordItem.Label,
                SortText: GetSortText(wordItem.Kind, wordItem.Label)
            ));
        }

        return new Protocol.CompletionList(false, mapped.ToArray());
    }

    /// <summary>
    /// Builds the wire sort key using the reference host's tier order:
    /// variables, local definitions, scoped columns, metadata, functions, keywords.
    /// </summary>
    private static string GetSortText(CompletionKind kind, int priority, string label) =>
        TierPrefix(kind) + priority.ToString("D2", CultureInfo.InvariantCulture) + "_" + label;

    private static string GetSortText(SqlWordListKind kind, string label) =>
        TierPrefix(kind) + "00_" + label;

    private static string TierPrefix(CompletionKind kind) => kind switch
    {
        CompletionKind.Variable => "0_",
        CompletionKind.Alias or CompletionKind.Cte => "1_",
        CompletionKind.Column => "2_",
        CompletionKind.Table or CompletionKind.View or CompletionKind.ExternalTable
            or CompletionKind.Schema or CompletionKind.Database => "3_",
        CompletionKind.Function or CompletionKind.Snippet or CompletionKind.DataType => "4_",
        CompletionKind.Keyword => "5_",
        _ => "4_"
    };

    private static string TierPrefix(SqlWordListKind kind) => kind switch
    {
        SqlWordListKind.Variable => "0_",
        SqlWordListKind.Alias or SqlWordListKind.With or SqlWordListKind.Subquery => "1_",
        SqlWordListKind.Column => "2_",
        SqlWordListKind.Database or SqlWordListKind.Schema or SqlWordListKind.Table
            or SqlWordListKind.View or SqlWordListKind.ExternalTable
            or SqlWordListKind.TempTable => "3_",
        SqlWordListKind.Procedure or SqlWordListKind.Function or SqlWordListKind.Snippet
            or SqlWordListKind.DataType => "4_",
        SqlWordListKind.Keyword => "5_",
        _ => "4_"
    };

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
