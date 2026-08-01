using JustyBase.NetezzaSqlLsp.Protocol;
using JustyBase.NetezzaSqlParser.Dialects;

namespace JustyBase.NetezzaSqlLsp.Services;

/// <summary>Provides LSP document symbols (outline) for Netezza SQL.</summary>
public static class DocumentSymbolService
{
    /// <summary>Returns symbol information for all statements and named objects in the document.</summary>
    public static SymbolInformation[] GetDocumentSymbols(string text, SqlDialect dialect = SqlDialect.Netezza)
    {
        if (string.IsNullOrEmpty(text))
            return [];

        try
        {
            var index = SymbolCollector.Collect(text, dialect);
            return index.Occurrences
                .Where(o => o.IsStatement || o.Kind is SymbolKind.Class or SymbolKind.Struct or SymbolKind.Function)
                .OrderBy(o => o.Range.Start.Line)
                .ThenBy(o => o.Range.Start.Character)
                .Select(o => new SymbolInformation(o.Name, o.Kind, o.Range, o.ContainerName))
                .ToArray();
        }
        catch
        {
            return [];
        }
    }
}
