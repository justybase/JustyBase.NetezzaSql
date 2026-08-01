using JustyBase.NetezzaSqlLsp.Protocol;
using JustyBase.NetezzaSqlParser.Dialects;

namespace JustyBase.NetezzaSqlLsp.Services;

/// <summary>Provides LSP find-references for Netezza SQL symbols.</summary>
public static class ReferencesService
{
    /// <summary>Returns reference locations for the symbol at the given position.</summary>
    public static Location[] GetReferences(string text, int line, int character, string uri, bool includeDeclaration, SqlDialect dialect = SqlDialect.Netezza)
    {
        if (string.IsNullOrEmpty(text))
            return [];

        try
        {
            var absolute = LspTextUtilities.PositionToOffset(text, line, character);
            var index = SymbolCollector.Collect(text, dialect);
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
