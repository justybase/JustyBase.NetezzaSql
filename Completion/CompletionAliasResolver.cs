using JustyBase.NetezzaSqlParser.Lexer;
using Superpower.Model;

namespace JustyBase.NetezzaSqlParser.Completion;

/// <summary>
/// Shared alias and qualified table-name resolution for completion and wildcard expansion.
/// </summary>
internal static class CompletionAliasResolver
{
    public readonly record struct TablePath(string Name, string? Schema, string? Database);

    public static string? ResolveAlias(Token<NzToken>[] tokens, string alias)
    {
        bool inFromOrJoin = false;
        bool afterUpdate = false;
        string? previousIdentifier = null;

        for (int i = 0; i < tokens.Length; i++)
        {
            var k = tokens[i].Kind;

            if (k == NzToken.Update)
            {
                afterUpdate = true;
                inFromOrJoin = false;
                previousIdentifier = null;
                continue;
            }
            if (k == NzToken.Set && afterUpdate)
            {
                afterUpdate = false;
                previousIdentifier = null;
                continue;
            }
            if (IsFrom(k) || IsJoin(k))
            {
                inFromOrJoin = true;
                afterUpdate = false;
                previousIdentifier = null;
                continue;
            }
            if (IsWhere(k) || IsGroupBy(k) || IsOrderBy(k) || IsHaving(k) || k == NzToken.On)
            {
                inFromOrJoin = false;
                afterUpdate = false;
                continue;
            }
            if (k == NzToken.As)
                continue;
            if (k == NzToken.Comma)
            {
                previousIdentifier = null;
                continue;
            }
            if (k == NzToken.Dot)
                continue;

            if (k is NzToken.Identifier or NzToken.QuotedIdentifier)
            {
                var name = tokens[i].ToStringValue();
                if (previousIdentifier is not null &&
                    string.Equals(name, alias, StringComparison.OrdinalIgnoreCase))
                {
                    return previousIdentifier;
                }
                if (inFromOrJoin || afterUpdate)
                    previousIdentifier = name;
            }
            else
            {
                previousIdentifier = null;
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves alias to underlying table path (database, schema, unqualified name).
    /// </summary>
    public static TablePath? ResolveTablePath(Token<NzToken>[] tokens, string aliasOrName)
    {
        var resolvedAlias = ResolveAlias(tokens, aliasOrName);
        var lookup = resolvedAlias ?? aliasOrName;

        for (int i = 0; i < tokens.Length; i++)
        {
            if (!IsFrom(tokens[i].Kind) && !IsJoin(tokens[i].Kind) && tokens[i].Kind != NzToken.Update)
                continue;

            var (path, alias, consumed) = ParseTablePathAt(tokens, i + 1);
            if (consumed == 0) continue;

            var effectiveAlias = alias ?? path.Name;
            if (string.Equals(effectiveAlias, aliasOrName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(path.Name, lookup, StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }

            i += consumed - 1;
        }

        return null;
    }

    public static TablePath ParseQualifierPath(string qualifierText)
    {
        var parts = qualifierText.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return new TablePath(qualifierText, null, null);
        if (parts.Length == 1)
            return new TablePath(parts[0].Trim('"', '[', ']'), null, null);
        if (parts.Length == 2)
            return new TablePath(parts[1].Trim('"', '[', ']'), parts[0].Trim('"', '[', ']'), null);
        return new TablePath(parts[^1].Trim('"', '[', ']'), parts[^2].Trim('"', '[', ']'), parts[0].Trim('"', '[', ']'));
    }

    public static (TablePath Path, string? Alias, int Consumed) ParseTablePathAt(Token<NzToken>[] tokens, int start)
    {
        int i = start;
        string? firstIdent = null;
        string? secondIdent = null;
        string? thirdIdent = null;
        bool afterDoubleDot = false;

        // Consume up to 3 dot-separated path parts: table, schema.table, db.schema.table, db..table
        while (i < tokens.Length && tokens[i].Kind is NzToken.Identifier or NzToken.QuotedIdentifier)
        {
            if (firstIdent is null)
                firstIdent = tokens[i].ToStringValue();
            else if (secondIdent is null)
                secondIdent = tokens[i].ToStringValue();
            else
                thirdIdent = tokens[i].ToStringValue();
            i++;

            bool consumed = false;

            // Single dot: schema.table or db.schema.table part separator
            if (i < tokens.Length && tokens[i].Kind == NzToken.Dot &&
                i + 1 < tokens.Length && tokens[i + 1].Kind is NzToken.Identifier or NzToken.QuotedIdentifier)
            {
                i++;
                consumed = true;
                continue;
            }

            // Double dot (db..table)
            if (i < tokens.Length && tokens[i].Kind == NzToken.Dot &&
                i + 1 < tokens.Length && tokens[i + 1].Kind == NzToken.Dot &&
                i + 2 < tokens.Length && tokens[i + 2].Kind is NzToken.Identifier or NzToken.QuotedIdentifier)
            {
                afterDoubleDot = true;
                secondIdent = null;
                thirdIdent = null;
                i += 2;
                consumed = true;
                continue;
            }

            if (!consumed) break;
        }

        if (firstIdent is null)
            return (new TablePath(string.Empty, null, null), null, 0);

        string? tableName;
        string? schema = null;
        string? database = null;

        if (afterDoubleDot && secondIdent is not null)
        {
            database = firstIdent;
            tableName = secondIdent;
        }
        else if (thirdIdent is not null)
        {
            database = firstIdent;
            schema = secondIdent;
            tableName = thirdIdent;
        }
        else if (secondIdent is not null)
        {
            schema = firstIdent;
            tableName = secondIdent;
        }
        else
        {
            tableName = firstIdent;
        }

        string? alias = null;
        if (i < tokens.Length && tokens[i].Kind == NzToken.As)
            i++;

        if (i < tokens.Length && tokens[i].Kind is NzToken.Identifier or NzToken.QuotedIdentifier)
        {
            var candidate = tokens[i].ToStringValue();
            if (!IsClauseKeyword(candidate))
            {
                alias = candidate;
                i++;
            }
        }

        return (new TablePath(tableName, schema, database), alias, i - start);
    }

    private static bool IsClauseKeyword(string word) =>
        word.Equals("WHERE", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("ON", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("SET", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("GROUP", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("ORDER", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("HAVING", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("LIMIT", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("FETCH", StringComparison.OrdinalIgnoreCase);

    private static bool IsFrom(NzToken t) => t == NzToken.From;
    private static bool IsJoin(NzToken t) =>
        t is NzToken.Join or NzToken.Inner or NzToken.Left or NzToken.Right
            or NzToken.Full or NzToken.Cross;
    private static bool IsWhere(NzToken t) => t == NzToken.Where;
    private static bool IsGroupBy(NzToken t) => t == NzToken.GroupBy;
    private static bool IsOrderBy(NzToken t) => t == NzToken.OrderBy;
    private static bool IsHaving(NzToken t) => t == NzToken.Having;
}
