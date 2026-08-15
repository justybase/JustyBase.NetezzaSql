using System.Text.RegularExpressions;

namespace JustyBase.Sqlite.Diagnostics;

/// <summary>
/// Locates the SQL token to highlight from a SQLite error message
/// (for example <c>near "X": syntax error</c> or <c>no such table: t</c>).
/// </summary>
public static class SqliteErrorLocator
{
    /// <param name="Word">Token to search for in the SQL statement.</param>
    /// <param name="UseRegexWordSearch">When true, hosts may use a looser word search.</param>
    public readonly record struct Location(string Word, bool UseRegexWordSearch = false);

    private static readonly Regex NearRegex = new(
        @"near ""(?<word>[^""]*)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex NoSuchRegex = new(
        @"no such (?:table|view|column|index|function|pragma|trigger|constraint):\s*(?<word>[^\s;]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex AlreadyExistsRegex = new(
        @"(?<word>[A-Za-z_][A-Za-z0-9_.]*)\s+already exists",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex ColumnRedefinedRegex = new(
        @"duplicate column name:\s*(?<word>[^\s;]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex ConstraintFailedRegex = new(
        @"UNIQUE constraint failed:\s*(?<word>[^\s;]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool TryLocate(string message, out Location location)
    {
        location = default;
        if (string.IsNullOrEmpty(message))
            return false;

        var m = NearRegex.Match(message);
        if (m.Success)
        {
            location = new Location(m.Groups["word"].Value);
            return true;
        }

        m = NoSuchRegex.Match(message);
        if (m.Success)
        {
            location = new Location(m.Groups["word"].Value);
            return true;
        }

        m = AlreadyExistsRegex.Match(message);
        if (m.Success)
        {
            location = new Location(m.Groups["word"].Value, UseRegexWordSearch: true);
            return true;
        }

        m = ColumnRedefinedRegex.Match(message);
        if (m.Success)
        {
            location = new Location(m.Groups["word"].Value);
            return true;
        }

        m = ConstraintFailedRegex.Match(message);
        if (m.Success)
        {
            location = new Location(m.Groups["word"].Value);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Resolves a 0-based char span in <paramref name="sql"/> for host highlighters that need (offset, length).
    /// </summary>
    public static (int Offset, int Length) LocateInSql(string message, string sql)
    {
        if (!TryLocate(message, out var location) || string.IsNullOrEmpty(location.Word))
            return (-1, -1);

        int relative = location.UseRegexWordSearch
            ? IndexOfUnqualifiedWord(sql, location.Word)
            : sql.IndexOf(location.Word, StringComparison.OrdinalIgnoreCase);
        if (relative < 0)
            return (-1, -1);

        return (relative, location.Word.Length);
    }

    /// <summary>Finds <paramref name="word"/> as a standalone identifier, skipping qualified refs like <c>alias.word</c>.</summary>
    private static int IndexOfUnqualifiedWord(string sql, string word)
    {
        int searchFrom = 0;
        while (searchFrom <= sql.Length - word.Length)
        {
            int idx = sql.IndexOf(word, searchFrom, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                return -1;

            bool leftBoundary = idx == 0 || !IsIdentifierChar(sql[idx - 1]);
            bool rightBoundary = idx + word.Length >= sql.Length || !IsIdentifierChar(sql[idx + word.Length]);
            bool notQualified = idx == 0 || sql[idx - 1] != '.';

            if (leftBoundary && rightBoundary && notQualified)
                return idx;

            searchFrom = idx + 1;
        }

        return -1;
    }

    private static bool IsIdentifierChar(char c)
        => char.IsLetterOrDigit(c) || c == '_';
}
