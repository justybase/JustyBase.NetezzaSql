using System.Text.RegularExpressions;

namespace JustyBase.Netezza;

/// <summary>
/// Locates the SQL token to highlight from a Netezza / OLEDB error message.
/// Shared by Legacy FCTB highlighter and Avalonia plugin exception handling.
/// </summary>
public static class NetezzaErrorLocator
{
    /// <param name="Word">Token to search for in the SQL statement.</param>
    /// <param name="UseRegexWordSearch">When true, hosts may use a looser word search.</param>
    /// <param name="CharIndexInSlice">
    /// Optional start index within <c>sqlSlice</c> (e.g. parser "at char N" errors).
    /// Absolute editor offset = statementSelectionStart + this value.
    /// </param>
    public readonly record struct Location(string Word, bool UseRegexWordSearch = false, int? CharIndexInSlice = null);

    private static Regex? _attributeNotFound;
    private static Regex? _exceptAtChar;
    private static Regex? _incorrectType;
    private static Regex? _transformColumnType;
    private static Regex? _groomError;
    private static Regex? _repeatedError;
    private static Regex? _alreadyExistsError;
    private static Regex? _notExistsError;
    private static Regex? _functionError;
    private static Regex? _groupError1;
    private static Regex? _groupError2;
    private static Regex? _wrongOption;
    private static Regex? _wrongSet;
    private static Regex? _manySameAliases;
    private static Regex? _ambiguousError;
    private static Regex? _couldNotAcquire;
    private static Regex? _objectAlreadyExists;
    private static Regex? _schemaDoesNotExist;

    public static bool TryLocate(
        string message,
        bool fromOleDb,
        ReadOnlySpan<char> sqlSlice,
        out Location location)
    {
        EnsureRegexInitialized();
        location = default;
        if (string.IsNullOrEmpty(message))
            return false;

        // Prefer the at-char parser (sets CharIndexInSlice) before the crude "^ found" fallback.
        if (TryExceptAtChar(message, sqlSlice, out location)) return true;

        if ((!fromOleDb && message.StartsWith("ERROR [42000] ERROR:", StringComparison.Ordinal) && message.Contains(" ^ found \"", StringComparison.Ordinal))
            || (fromOleDb && message.Contains(" ^ found \"", StringComparison.Ordinal)))
        {
            if (TryLocateFoundAtCharFallback(message, sqlSlice, out location))
                return true;
        }

        if (TryGroup(_wrongSet!, message, "found", out location)) return true;
        if (TryGroup(_attributeNotFound!, message, "name", out location)) return true;
        if (TryGroup(_incorrectType!, message, "found", out location)) return true;
        if (TryGroup(_transformColumnType!, message, "found", out location)) return true;
        if (TryGroup(_groomError!, message, "found", out location)) return true;
        if (TryGroup(_repeatedError!, message, "found", out location)) return true;
        if (TryGroup(_alreadyExistsError!, message, "found", out location)) return true;
        if (TryGroup(_notExistsError!, message, "found", out location)) return true;
        if (TryGroup(_functionError!, message, "found", out location)) return true;

        if (_groupError1!.IsMatch(message))
        {
            var m = _groupError1.Match(message);
            string found = m.Groups["found"].Value;
            if (!string.IsNullOrEmpty(found) &&
                sqlSlice.Contains(found.AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                location = new Location(found);
                return true;
            }

            if (_groupError2!.IsMatch(message))
            {
                m = _groupError2.Match(message);
                location = new Location(m.Groups["found"].Value);
                return true;
            }
        }

        if (TryGroup(_wrongOption!, message, "found", out location)) return true;
        if (TryGroup(_manySameAliases!, message, "found", out location)) return true;

        if (_ambiguousError!.IsMatch(message))
        {
            var m = _ambiguousError.Match(message);
            location = new Location(m.Groups["found"].Value, UseRegexWordSearch: true);
            return true;
        }

        if (TryGroup(_couldNotAcquire!, message, "found", out location)) return true;

        if (!fromOleDb && message.StartsWith("ERROR [HY000] ERROR:  Permission denied on ", StringComparison.Ordinal))
        {
            int m1 = message.IndexOf('"');
            int m2 = message.LastIndexOf('"');
            if (m1 != -1 && m2 > m1)
            {
                location = new Location(message[(m1 + 1)..m2]);
                return true;
            }
        }

        if (!fromOleDb && message.StartsWith("ERROR [HY000] ERROR: ", StringComparison.Ordinal)
            && _objectAlreadyExists!.IsMatch(message))
        {
            location = new Location(_objectAlreadyExists.Match(message).Groups["objectname"].Value);
            return true;
        }

        if (!fromOleDb && message.StartsWith("ERROR [HY000] ERROR: ", StringComparison.Ordinal)
            && _schemaDoesNotExist!.IsMatch(message))
        {
            location = new Location(_schemaDoesNotExist.Match(message).Groups["objectname"].Value);
            return true;
        }

        if (!fromOleDb && message.StartsWith("ERROR [42S02] ERROR:", StringComparison.Ordinal))
        {
            int m1 = message.LastIndexOf('.');
            if (m1 != -1 && m1 + 1 < message.Length)
            {
                location = new Location(message[(m1 + 1)..]);
                return true;
            }
        }

        if (!fromOleDb && message.StartsWith("ERROR [42S22] ERROR:", StringComparison.Ordinal))
        {
            const string version1 = "ERROR [42S22] ERROR:  Attribute '";
            if (message.StartsWith(version1, StringComparison.Ordinal))
            {
                int a1 = message.IndexOf('\'', version1.Length);
                if (a1 > version1.Length)
                {
                    location = new Location(message[version1.Length..a1]);
                    return true;
                }
            }
        }

        if (!fromOleDb && message.StartsWith("ERROR [HY000] ERROR:  GROOM VERSIONS must be run on ", StringComparison.Ordinal))
        {
            const string prefix = "ERROR [HY000] ERROR:  GROOM VERSIONS must be run on ";
            int i1 = message.IndexOf(" before", StringComparison.Ordinal);
            if (i1 > prefix.Length)
            {
                location = new Location(message[prefix.Length..i1]);
                return true;
            }
        }

        if (!fromOleDb && message.StartsWith("ERROR [HY000] ERROR:  Attribute ", StringComparison.Ordinal)
            && message.Contains(" is repeated", StringComparison.Ordinal))
        {
            const string prefix = "ERROR [HY000] ERROR:  Attribute ";
            int i1 = message.IndexOf(" is repeated", StringComparison.Ordinal);
            if (i1 > prefix.Length + 1)
            {
                location = new Location(message[(prefix.Length + 1)..(i1 - 1)]);
                return true;
            }
        }

        if (!fromOleDb && message.StartsWith("ERROR [HY000] ERROR:  Attribute ", StringComparison.Ordinal)
            && message.Contains(" must be GROUPed", StringComparison.Ordinal))
        {
            const string prefix = "ERROR [HY000] ERROR:  Attribute ";
            int i1 = message.IndexOf(" must be GROUPed", StringComparison.Ordinal);
            if (i1 > prefix.Length)
            {
                location = new Location(message[prefix.Length..i1]);
                return true;
            }
        }

        if (!fromOleDb && message.StartsWith("ERROR [HY000] ERROR:  ", StringComparison.Ordinal)
            && message.Contains(" is not a valid option name", StringComparison.Ordinal))
        {
            const string prefix = "ERROR [HY000] ERROR:  ";
            int i1 = message.IndexOf(" is not a valid option name", StringComparison.Ordinal);
            if (i1 > prefix.Length + 1)
            {
                location = new Location(message[(prefix.Length + 1)..(i1 - 1)]);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Resolves a 0-based char span in <paramref name="sql"/> for host highlighters that need (offset, length).
    /// </summary>
    public static (int Offset, int Length) LocateInSql(string message, ReadOnlySpan<char> sql, bool fromOleDb = false)
    {
        if (!TryLocate(message, fromOleDb, sql, out var location) || string.IsNullOrEmpty(location.Word))
            return (-1, -1);

        int start = location.CharIndexInSlice ?? 0;
        if (start < 0 || start > sql.Length)
            return (-1, -1);

        int relative = location.UseRegexWordSearch
            ? IndexOfUnqualifiedWord(sql[start..], location.Word)
            : sql[start..].IndexOf(location.Word.AsSpan(), StringComparison.OrdinalIgnoreCase);
        if (relative < 0)
            return (-1, -1);

        return (start + relative, location.Word.Length);
    }

    /// <summary>
    /// Finds <paramref name="word"/> as a standalone identifier, skipping qualified refs like <c>alias.word</c>
    /// (Legacy FCTB <c>UseRegex2</c> / ambiguous-column behavior).
    /// </summary>
    private static int IndexOfUnqualifiedWord(ReadOnlySpan<char> sql, string word)
    {
        ReadOnlySpan<char> needle = word.AsSpan();
        if (needle.IsEmpty || needle.Length > sql.Length)
            return -1;

        int searchFrom = 0;
        while (searchFrom <= sql.Length - needle.Length)
        {
            int idx = sql[searchFrom..].IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                return -1;

            int abs = searchFrom + idx;
            bool leftBoundary = abs == 0 || !IsIdentifierChar(sql[abs - 1]);
            bool rightBoundary = abs + needle.Length >= sql.Length || !IsIdentifierChar(sql[abs + needle.Length]);
            bool notQualified = abs == 0 || sql[abs - 1] != '.';

            if (leftBoundary && rightBoundary && notQualified)
                return abs;

            searchFrom = abs + 1;
        }

        return -1;
    }

    private static bool IsIdentifierChar(char c)
        => char.IsLetterOrDigit(c) || c == '_';

    private static bool TryExceptAtChar(string message, ReadOnlySpan<char> sqlSlice, out Location location)
    {
        location = default;
        if (!_exceptAtChar!.IsMatch(message))
            return false;

        var m = _exceptAtChar.Match(message);
        location = new Location(
            m.Groups["found"].Value,
            CharIndexInSlice: ParseAtCharSliceOffset(m.Groups["charNum"].Value, sqlSlice));
        return true;
    }

    /// <summary>
    /// Fallback when the message contains <c>^ found</c> / <c>at char</c> but does not match <see cref="_exceptAtChar"/>.
    /// Still parses the character index when present so hosts do not search from offset 0.
    /// </summary>
    private static bool TryLocateFoundAtCharFallback(
        string message,
        ReadOnlySpan<char> sqlSlice,
        out Location location)
    {
        location = default;
        int m = message.IndexOf("^ found", StringComparison.Ordinal);
        int m1 = message.IndexOf("at char ", m, StringComparison.Ordinal);
        if (m < 0 || m1 <= m)
            return false;

        string wrongText = message[(m + 9)..(m1 - 3)];
        int charStart = m1 + "at char ".Length;
        int charEnd = charStart;
        while (charEnd < message.Length && char.IsDigit(message[charEnd]))
            charEnd++;

        if (charEnd > charStart)
        {
            location = new Location(
                wrongText,
                CharIndexInSlice: ParseAtCharSliceOffset(message[charStart..charEnd], sqlSlice));
            return true;
        }

        location = new Location(wrongText);
        return true;
    }

    private static int ParseAtCharSliceOffset(string charNumText, ReadOnlySpan<char> sqlSlice)
    {
        int number = int.Parse(charNumText) - 1;
        int leadingWhite = 0;
        while (leadingWhite < sqlSlice.Length)
        {
            char c = sqlSlice[leadingWhite];
            if (c is not ('\r' or '\n' or ' '))
                break;
            leadingWhite++;
        }

        return number + leadingWhite;
    }

    private static bool TryGroup(Regex regex, string message, string groupName, out Location location)
    {
        location = default;
        if (!regex.IsMatch(message))
            return false;

        var m = regex.Match(message);
        location = new Location(m.Groups[groupName].Value);
        return true;
    }

    private static void EnsureRegexInitialized()
    {
        if (_attributeNotFound is not null)
            return;

        _attributeNotFound = new Regex(@"ERROR: Attribute '(?<name>.*)' not found", RegexOptions.CultureInvariant);
        _exceptAtChar = new Regex(@"\^ found ""(?<found>.*)"" \(at char (?<charNum>[0-9]+)\) expecting", RegexOptions.CultureInvariant);
        _incorrectType = new Regex(@"^ERROR: DROP (TABLE|VIEW): object ""(?<found>.*)"", incorrect type\.$", RegexOptions.CultureInvariant);
        _transformColumnType = new Regex(@"^ERROR: transformColumnType: error reading type '(?<found>.*)'$", RegexOptions.CultureInvariant);
        _groomError = new Regex(@"^ERROR: GROOM VERSIONS must be run on (?<found>.*) before any other GROOM operation$", RegexOptions.CultureInvariant);
        _repeatedError = new Regex(@"^ERROR: Attribute '(?<found>.*)' is repeated. Must have an appropriate alias\.$", RegexOptions.CultureInvariant);
        _alreadyExistsError = new Regex(@"^ERROR: CREATE TABLE: object ""(?<found>.*)"" already exists\.$", RegexOptions.CultureInvariant);
        _notExistsError = new Regex(@"^ERROR: relation does not exist (?<db>[^.]*)\.?(?<schema>[^.]*)\.?(?<found>.*)$", RegexOptions.CultureInvariant);
        _functionError = new Regex(@"^ERROR: Function '(?<found>.*)\(.*\)' does not exist", RegexOptions.CultureInvariant);
        _groupError1 = new Regex(@"^ERROR: Attribute (?<found>.*) must be GROUPed or used in an aggregate function$", RegexOptions.CultureInvariant);
        _groupError2 = new Regex(@"^ERROR: Attribute (?<table>[^\.]*)\.(?<found>.*) must be GROUPed or used in an aggregate function$", RegexOptions.CultureInvariant);
        _wrongOption = new Regex(@"^ERROR: Option '(?<found>.*)' is not recognized$", RegexOptions.CultureInvariant);
        _wrongSet = new Regex(@"^ERROR: 'SET (?<found>.*)'", RegexOptions.CultureInvariant);
        _manySameAliases = new Regex(@"^ERROR: Table name ""(?<found>.*)"" specified more than once$", RegexOptions.CultureInvariant);
        _ambiguousError = new Regex(@"^ERROR: Column reference ""(?<found>.*)"" is ambiguous$", RegexOptions.CultureInvariant);
        _couldNotAcquire = new Regex(@"^ERROR: DROP DATABASE: could not acquire lock for ""(?<found>.*)""$", RegexOptions.CultureInvariant);
        _objectAlreadyExists = new Regex(@"object ""(?<objectname>[a-z0-9_\.""]+)"" already exists", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        _schemaDoesNotExist = new Regex(@"Schema '(?<objectname>[a-z0-9_\.""]+)' does not exist", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
