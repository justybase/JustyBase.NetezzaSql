namespace JustyBase.NetezzaSqlParser.Authoring;

/// <summary>
/// Resolves CTE and table-alias symbols in a single SQL document.
/// The service deliberately exposes only text offsets so it can be shared by UI hosts.
/// </summary>
public static class NzSymbolService
{
    /// <summary>Gets the symbol under <paramref name="offset"/>, its declaration and all occurrences.</summary>
    public static SqlRenameInfo? GetSymbol(string text, int offset)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        offset = Math.Clamp(offset, 0, Math.Max(0, text.Length - 1));

        try
        {
            var index = NzSymbolCollector.Collect(text);
            var occurrence = index.FindOccurrenceAt(offset);
            if (occurrence is null)
                return null;

            int? definitionId = occurrence.IsDefinition ? occurrence.Id : occurrence.DefinitionId;
            if (definitionId is null)
                return null;

            var occurrences = index.FindReferences(definitionId.Value, includeDeclaration: true);
            if (occurrences.Count == 0)
                return null;

            var definition = index.FindDefinition(definitionId.Value);
            return new SqlRenameInfo(definition?.Name ?? occurrence.Name, occurrence.Kind, occurrences);
        }
        catch
        {
            // Authoring must remain available for incomplete SQL while it is being typed.
            return null;
        }
    }

    /// <summary>Gets the declaration for the CTE or alias under <paramref name="offset"/>.</summary>
    public static SymbolOccurrence? GetDefinition(string text, int offset)
    {
        var symbol = GetSymbol(text, offset);
        return symbol?.Occurrences.FirstOrDefault(occurrence => occurrence.IsDefinition);
    }

    /// <summary>Gets the declaration and references for the CTE or alias under <paramref name="offset"/>.</summary>
    public static IReadOnlyList<SymbolOccurrence> GetReferences(string text, int offset)
        => GetSymbol(text, offset)?.Occurrences ?? Array.Empty<SymbolOccurrence>();
}
