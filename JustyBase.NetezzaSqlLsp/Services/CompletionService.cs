using JustyBase.NetezzaSqlParser.Completion;
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

    /// <summary>Returns LSP completions at the given position.</summary>
    public static Protocol.CompletionList GetCompletions(string text, int line, int character, ISchemaProvider? schema)
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

        var engine = new NzCompletionEngine(schema);
        var items = engine.GetCompletions(text, offset);

        var mapped = new List<Protocol.CompletionItem>(items.Count);
        foreach (var item in items)
        {
            mapped.Add(new Protocol.CompletionItem(
                Label: item.Label,
                Kind: MapKind(item.Kind),
                Detail: item.Detail,
                InsertText: null
            ));
        }

        return new Protocol.CompletionList(false, mapped.ToArray());
    }
}
