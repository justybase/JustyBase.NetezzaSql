using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Lexer;

namespace JustyBase.NetezzaSqlParser.Authoring;

public static class NzRenameService
{
    public static SqlRenameInfo? GetRenameInfo(string text, int offset, SqlDialect dialect = SqlDialect.Netezza)
        => NzSymbolService.GetSymbol(text, offset, dialect);

    public static IReadOnlyList<SqlTextEdit>? GetRenameEdits(string text, int offset, string newName, SqlDialect dialect = SqlDialect.Netezza)
    {
        try { return GetRenameEditsCore(text, offset, newName, dialect); }
        catch { return null; }
    }

    private static IReadOnlyList<SqlTextEdit>? GetRenameEditsCore(string text, int offset, string newName, SqlDialect dialect)
    {
        var index = NzSymbolCollector.Collect(text, dialect);
        var symbol = NzSymbolService.GetSymbol(text, offset, index);
        if (symbol is null || FormatReplacement("a", newName, dialect) is null) return null;
        var own = symbol.Occurrences.Where(o => o.IsDefinition).Select(o => o.StartAbsolute).ToHashSet();
        var name = DecodeName(newName.Trim());
        // Conservative capture prevention, including unrelated query scopes.
        if (index.Occurrences.Any(o => o.IsDefinition
            && !own.Contains(o.StartAbsolute) && string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase))) return null;
        // An unaliased physical relation exposes its table name as a qualifier;
        // renaming an alias or CTE to that name would capture its references.
        if (name is not null && index.ExposedRelationNames.Contains(name)) return null;
        return symbol.Occurrences.OrderBy(o => o.StartAbsolute).Select(o => new SqlTextEdit(
            o.StartAbsolute, o.EndAbsolute, FormatReplacement(text[o.StartAbsolute..o.EndAbsolute], newName, dialect)!)).ToArray();
    }

    public static string ApplyRename(string text, SqlRenameInfo renameInfo, string newName)
    {
        if (renameInfo.Occurrences.Count == 0 || FormatReplacement("a", newName) is null) return text;
        var replacements = renameInfo.Occurrences.OrderBy(o => o.StartAbsolute).ToArray();
        if (replacements.Any(o => o.StartAbsolute < 0 || o.EndAbsolute > text.Length || o.StartAbsolute >= o.EndAbsolute)
            || replacements.Zip(replacements.Skip(1)).Any(pair => pair.First.EndAbsolute > pair.Second.StartAbsolute)) return text;
        // Revalidate the supplied snapshot against production identity. Legacy
        // callers can supply ranges, but stale or foreign ranges must not edit SQL.
        var original = text[replacements[0].StartAbsolute..replacements[0].EndAbsolute];
        var dialect = original.StartsWith('`') ? SqlDialect.MySql
            : original.StartsWith('[') ? SqlDialect.Access : SqlDialect.Netezza;
        var edits = GetRenameEdits(text, replacements[0].StartAbsolute, newName, dialect);
        if (edits is null || !edits.Select(edit => (edit.StartOffset, edit.EndOffset))
            .SequenceEqual(replacements.Select(occurrence => (occurrence.StartAbsolute, occurrence.EndAbsolute)))) return text;
        foreach (var edit in edits.Reverse())
        {
            text = text[..edit.StartOffset] + edit.NewText + text[edit.EndOffset..];
        }
        return text;
    }

    public static string? FormatReplacement(string original, string newName, SqlDialect dialect = SqlDialect.Netezza)
    {
        var trimmed = newName.Trim();
        var name = DecodeName(trimmed);
        if (name is null) return null;
        if (original.StartsWith('[')) return "[" + name.Replace("]", "]]", StringComparison.Ordinal) + "]";
        if (original.StartsWith('`')) return "`" + name.Replace("`", "``", StringComparison.Ordinal) + "`";
        bool plain = false;
        try
        {
            var tokens = DialectRuntime.Tokenize(name, dialect).ToArray();
            plain = tokens.Length == 1 && tokens[0].Kind == NzToken.Identifier && tokens[0].Span.Length == name.Length;
        }
        catch { }
        return original.StartsWith('"') || trimmed.StartsWith('"') || !plain
            ? "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : name;
    }

    private static string? DecodeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Any(char.IsControl)) return null;
        if (name.StartsWith('"'))
        {
            if (name.Length < 2 || !name.EndsWith('"')) return null;
            var body = name[1..^1];
            if (body.Replace("\"\"", "", StringComparison.Ordinal).Contains('"')) return null;
            name = body.Replace("\"\"", "\"", StringComparison.Ordinal);
        }
        return name.Length == 0 ? null : name;
    }

    public static bool IsValidIdentifier(string name)
        => !string.IsNullOrWhiteSpace(name) && (name.StartsWith('"')
            ? DecodeName(name) is not null
            : (char.IsLetter(name[0]) || name[0] == '_') && name.All(c => char.IsLetterOrDigit(c) || c == '_'));
}
