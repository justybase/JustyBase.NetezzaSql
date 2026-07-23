using JustyBase.NetezzaSqlParser.Lexer;
using Superpower.Model;

namespace JustyBase.NetezzaSqlParser.Authoring;

public static class NzRenameService
{
    public static SqlRenameInfo? GetRenameInfo(string text, int offset)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        offset = Math.Clamp(offset, 0, Math.Max(0, text.Length - 1));

        try
        {
            var tokens = NzLexer.Tokenize(text).ToArray();
            if (tokens.Length == 0)
                return null;

            Token<NzToken>? cursorToken = null;
            foreach (var token in tokens)
            {
                int tokenStart = token.Span.Position.Absolute;
                int tokenEnd = tokenStart + token.Span.Length;
                if (offset >= tokenStart && offset <= tokenEnd)
                {
                    cursorToken = token;
                    break;
                }
            }

            if (cursorToken is null)
                return null;

            if (cursorToken.Value.Kind != NzToken.Identifier && cursorToken.Value.Kind != NzToken.QuotedIdentifier)
                return null;

            var symbolName = StripQuotes(cursorToken.Value.ToStringValue());
            var index = NzSymbolCollector.Collect(text);
            var occurrence = index.FindOccurrenceAt(offset);
            if (occurrence is null)
                return null;

            var definitionId = occurrence.IsDefinition
                ? occurrence.Id
                : occurrence.DefinitionId;

            if (definitionId is null)
                return null;

            var references = index.FindReferences(definitionId.Value, includeDeclaration: true);
            if (references.Count == 0)
                return null;

            return new SqlRenameInfo(symbolName, occurrence.Kind, references);
        }
        catch
        {
            return null;
        }
    }

    public static string ApplyRename(string text, SqlRenameInfo renameInfo, string newName)
    {
        if (string.IsNullOrEmpty(text) || renameInfo.Occurrences.Count == 0)
            return text;

        // A rename must always remain a valid SQL identifier. Quoted names
        // are accepted as document syntax; plain names use identifier rules.
        if (!IsValidIdentifier(newName))
            return text;

        var replacements = renameInfo.Occurrences
            .OrderByDescending(o => o.StartAbsolute)
            .ToList();

        var result = text;
        foreach (var occ in replacements)
        {
            if (occ.StartAbsolute < 0 || occ.EndAbsolute > result.Length)
                continue;

            var originalText = result[occ.StartAbsolute..occ.EndAbsolute];
            var replacement = PreserveCasing(originalText, newName);
            result = result[..occ.StartAbsolute] + replacement + result[occ.EndAbsolute..];
        }

        return result;
    }

    public static bool IsValidIdentifier(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        if (name[0] == '"')
        {
            if (name.Length < 2 || name[^1] != '"')
                return false;

            for (int i = 1; i < name.Length - 1; i++)
            {
                if (name[i] == '"')
                    return false;
            }

            return true;
        }

        // Try plain identifier rules first
        if (!char.IsLetter(name[0]) && name[0] != '_')
            return !name.Contains('"'); // can be made valid via quoting

        for (int i = 1; i < name.Length; i++)
        {
            if (!char.IsLetterOrDigit(name[i]) && name[i] != '_')
            {
                // Not a valid plain identifier, but can be made valid via quoting
                // as long as it has no internal quote characters
                return !name.Contains('"');
            }
        }

        return true;
    }

    private static string PreserveCasing(string original, string newName)
    {
        if (original.Length > 0 && original[0] == '"')
        {
            return newName.Contains('"') ? newName : $"\"{newName}\"";
        }

        if (original == original.ToUpperInvariant())
            return newName.ToUpperInvariant();

        if (original == original.ToLowerInvariant())
            return newName.ToLowerInvariant();

        if (original.Length > 0 && char.IsUpper(original[0]))
            return char.ToUpperInvariant(newName[0]) + newName[1..];

        return newName;
    }

    private static string StripQuotes(string value)
    {
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            return value[1..^1];
        return value;
    }
}
