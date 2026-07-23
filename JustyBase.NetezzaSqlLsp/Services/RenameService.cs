using JustyBase.NetezzaSqlLsp.Protocol;
using LspRange = JustyBase.NetezzaSqlLsp.Protocol.Range;

namespace JustyBase.NetezzaSqlLsp.Services;

internal static class RenameService
{
    public static PrepareRenameResult? PrepareRename(string text, int line, int character)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        try
        {
            var absolute = LspTextUtilities.PositionToOffset(text, line, character);
            var index = SymbolCollector.Collect(text);
            var occurrence = index.FindOccurrenceAt(absolute);
            if (occurrence is null || occurrence.IsStatement)
                return null;

            return new PrepareRenameResult(false, occurrence.Range);
        }
        catch
        {
            return null;
        }
    }

    public static WorkspaceEdit? Rename(string text, int line, int character, string newName, string uri)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        try
        {
            var absolute = LspTextUtilities.PositionToOffset(text, line, character);
            var lineStarts = LspTextUtilities.ComputeLineStarts(text);
            var index = SymbolCollector.Collect(text);
            var occurrence = index.FindOccurrenceAt(absolute);
            if (occurrence is null || occurrence.IsStatement)
                return null;

            var definitionId = occurrence.IsDefinition
                ? occurrence.Id
                : occurrence.DefinitionId;

            if (definitionId is null)
                return null;

            var references = index.FindReferences(definitionId.Value, includeDeclaration: true);
            if (references.Count == 0)
                return null;

            var textEdits = new List<TextEdit>();
            foreach (var refOcc in references.OrderByDescending(r => r.StartAbsolute))
            {
                var start = LspTextUtilities.OffsetToPosition(refOcc.StartAbsolute, lineStarts);
                var end = LspTextUtilities.OffsetToPosition(refOcc.EndAbsolute, lineStarts);
                var range = new LspRange(start, end);
                textEdits.Add(new TextEdit(range, newName));
            }

            var changes = new Dictionary<string, TextEdit[]> { [uri] = textEdits.ToArray() };
            return new WorkspaceEdit(changes);
        }
        catch
        {
            return null;
        }
    }
}
