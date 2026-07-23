using JustyBase.NetezzaSqlLsp.Protocol;

namespace JustyBase.NetezzaSqlLsp.Services;

/// <summary>Provides LSP find-references for Netezza SQL symbols.</summary>
public static class ReferencesService
{
    /// <summary>Returns reference locations for the symbol at the given position.</summary>
    public static Location[] GetReferences(string text, int line, int character, string uri, bool includeDeclaration)
    {
        if (string.IsNullOrEmpty(text))
            return [];

        try
        {
            var absolute = LspTextUtilities.PositionToOffset(text, line, character);
            var index = SymbolCollector.Collect(text);
            var occurrence = index.FindOccurrenceAt(absolute);
            if (occurrence is null)
                return [];

            var targetDefinitionId = occurrence.IsDefinition
                ? occurrence.Id
                : occurrence.DefinitionId;

            if (targetDefinitionId is null)
                return [];

            return index.FindReferences(targetDefinitionId.Value, includeDeclaration)
                .Select(o => new Location(uri, o.Range))
                .ToArray();
        }
        catch
        {
            return [];
        }
    }
}
