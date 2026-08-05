using System.Collections.Frozen;
using System.Globalization;
using System.Text.RegularExpressions;

namespace JustyBase.ImportExport.Import;

/// <summary>
/// Identifier helpers shared by the import pipeline (port of the Avalonia
/// <c>StringExtension</c> name helpers): database-safe column-name normalization,
/// case-insensitive de-duplication and random suffixes.
/// </summary>
public static partial class ImportNameHelper
{
    private static readonly FrozenSet<string> NotAllowedWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "ABORT", "DECIMAL", "INTERVAL", "PRESERVE",
        "ALL", "DECODE", "INTO", "PRIMARY",
        "ALLOCATE", "DEFAULT", "LEADING", "RESET",
        "ANALYSE", "DESC", "LEFT", "REUSE",
        "ANALYZE", "DISTINCT", "LIKE", "RIGHT",
        "AND", "DISTRIBUTE", "LIMIT", "ROWS",
        "ANY", "DO", "LOAD", "SELECT",
        "AS", "ELSE", "LOCAL", "SESSION_USER",
        "ASC", "END", "LOCK", "SETOF",
        "BETWEEN", "EXCEPT", "MINUS", "SHOW",
        "BINARY", "EXCLUDE", "MOVE", "SOME",
        "BIT", "EXISTS", "NATURAL", "TABLE",
        "BOTH", "EXPLAIN", "NCHAR", "THEN",
        "CASE", "EXPRESS", "NEW", "TIES",
        "CAST", "EXTEND", "NOT", "TIME",
        "CHAR", "EXTERNAL", "NOTNULL", "TIMESTAMP",
        "CHARACTER", "EXTRACT", "NULL", "TO",
        "CHECK", "FALSE", "NULLS", "TRAILING",
        "CLUSTER", "FIRST", "NUMERIC", "TRANSACTION",
        "COALESCE", "FLOAT", "NVL", "TRIGGER",
        "COLLATE", "FOLLOWING", "NVL2", "TRIM",
        "COLLATION", "FOR", "OFF", "TRUE",
        "COLUMN", "FOREIGN", "OFFSET", "UNBOUNDED",
        "CONSTRAINT", "FROM", "OLD", "UNION",
        "COPY", "FULL", "ON", "UNIQUE",
        "CROSS", "FUNCTION", "ONLINE", "USER",
        "CURRENT", "GENSTATS", "ONLY", "USING",
        "CURRENT_CATALOG", "GLOBAL", "OR", "VACUUM",
        "CURRENT_DATE", "GROUP", "ORDER", "VARCHAR",
        "CURRENT_DB", "HAVING", "OTHERS", "VERBOSE",
        "CURRENT_SCHEMA", "IDENTIFIER_CASE", "OUT", "VERSION",
        "CURRENT_SID", "ILIKE", "OUTER", "VIEW",
        "CURRENT_TIME", "IN", "OVER", "WHEN",
        "CURRENT_TIMESTAMP", "INDEX", "OVERLAPS", "WHERE",
        "CURRENT_USER", "INITIALLY", "PARTITION", "WITH",
        "CURRENT_USERID", "INNER", "POSITION", "WRITE",
        "CURRENT_USEROID", "INOUT", "PRECEDING", "RESET",
        "DEALLOCATE", "INTERSECT", "PRECISION", "REUSE",
        "DEC"
    }.ToFrozenSet();

    public static string RandomSuffix(string startName = "export_", int len = 10, bool withDate = true)
    {
        const string letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

        if (string.IsNullOrEmpty(startName))
        {
            startName = "ABCDE_";
        }

#pragma warning disable CA5394 // Import names are not security tokens; uniqueness is the only requirement.
        return startName + (withDate ? DateTime.Now.ToString("yyMMdd_HHmm", CultureInfo.InvariantCulture) : "")
                         + new string(Enumerable.Repeat(letters, len).Select(s => s[Random.Shared.Next(s.Length)]).ToArray());
#pragma warning restore CA5394
    }

    /// <summary>Case-insensitive de-duplication with <c>_1</c>/<c>_2</c> suffixes (mutates the array).</summary>
    public static void DeDuplicate(string[] list)
    {
        var dict = new Dictionary<string, (int, int)>(list.Length, StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < list.Length; i++)
        {
            if (dict.TryGetValue(list[i], out var value))
            {
                dict[list[i]] = (value.Item1 + 1, value.Item2);
            }
            else
            {
                dict[list[i]] = (1, 0);
            }
        }

        for (int i = 0; i < list.Length; i++)
        {
            if (dict[list[i]].Item1 > 1)
            {
                dict[list[i]] = (dict[list[i]].Item1, dict[list[i]].Item2 + 1);
                list[i] = list[i] + "_" + dict[list[i]].Item2;
            }
        }
    }

    /// <summary>Uppercase, ASCII-only, database-safe identifier (max 126 chars, leading digit guarded).</summary>
    public static string NormalizeDbColumnName(string arg)
    {
        if (string.IsNullOrWhiteSpace(arg))
        {
            return RandomSuffix("EMPTY_COLNAME_", 3);
        }

        string res = LeadingUnderscoresRegex().Replace(InvalidTokenRegex().Replace(
            arg.Trim().ToUpper(CultureInfo.InvariantCulture)
                .Replace('Ą', 'A')
                .Replace('Ć', 'C')
                .Replace('Ę', 'E')
                .Replace('Ł', 'L')
                .Replace('Ń', 'N')
                .Replace('Ó', 'O')
                .Replace('Ś', 'S')
                .Replace('Ż', 'Z')
                .Replace('Ź', 'Z')
            , "_"), "");

        if (res.Length >= 129)
        {
            res = res[..126];
        }

        if (LeadingNonLetterRegex().IsMatch(res))
        {
            res = $"K{res}";
        }

        if (NotAllowedWords.Contains(res))
        {
            res += RandomSuffix("_", 2, false);
        }

        return res.Trim();
    }

    [GeneratedRegex(@"[^a-zA-Z0-9_]", RegexOptions.Compiled | RegexOptions.Singleline)]
    private static partial Regex InvalidTokenRegex();

    [GeneratedRegex(@"^_*", RegexOptions.Compiled | RegexOptions.Singleline)]
    private static partial Regex LeadingUnderscoresRegex();

    [GeneratedRegex(@"^[^a-zA-Z]", RegexOptions.Compiled)]
    private static partial Regex LeadingNonLetterRegex();
}
