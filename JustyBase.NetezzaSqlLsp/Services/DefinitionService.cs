using JustyBase.NetezzaSqlLsp.Protocol;

namespace JustyBase.NetezzaSqlLsp.Services;

/// <summary>Provides LSP go-to-definition for Netezza SQL symbols.</summary>
public static class DefinitionService
{
    /// <summary>Returns definition locations at the given position, or <see langword="null"/>.</summary>
    public static Location[]? GetDefinitions(string text, int line, int character, string uri)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        try
        {
            var absolute = LspTextUtilities.PositionToOffset(text, line, character);
            var index = SymbolCollector.Collect(text);
            var occurrence = index.FindOccurrenceAt(absolute);
            if (occurrence is null)
                return null;

            var target = occurrence.IsDefinition
                ? occurrence
                : occurrence.DefinitionId is not null
                    ? index.FindDefinition(occurrence.DefinitionId.Value)
                    : null;

            if (target is null)
                return null;

            return [new Location(uri, target.Range)];
        }
        catch
        {
            return null;
        }
    }
}
