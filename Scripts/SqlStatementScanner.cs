using JustyBase.NetezzaSqlParser.Dialects;

namespace JustyBase.NetezzaSqlParser.Scripts;

/// <summary>Half-open <c>[Start, Start + Length)</c> range of one SQL statement.</summary>
public readonly record struct SqlStatementSpan(int Start, int Length)
{
    public int End => Start + Length;

    public bool Contains(int position) => position >= Start && position <= End;

    public ReadOnlySpan<char> Slice(string source) => source.AsSpan(Start, Length);
}

/// <summary>Result of scanning a SQL script into statement spans.</summary>
public sealed class SqlScriptScan
{
    public static readonly SqlScriptScan Empty = new([], false);

    public SqlScriptScan(IReadOnlyList<SqlStatementSpan> statements, bool hasUnterminatedConstruct)
    {
        Statements = statements;
        HasUnterminatedConstruct = hasUnterminatedConstruct;
    }

    /// <summary>
    /// Statement ranges in source order. Dialects that preserve blank fragments
    /// (Netezza and the generic splitter) also report whitespace-only spans so
    /// editor offsets keep matching the historical <c>MySplitForSqlSplit</c>
    /// behaviour.
    /// </summary>
    public IReadOnlyList<SqlStatementSpan> Statements { get; }

    /// <summary>
    /// True when the scanner reached the end inside a string, comment, quote or
    /// SQL PL/PLSQL block. Callers may warn but should still execute what parsed.
    /// </summary>
    public bool HasUnterminatedConstruct { get; }
}

/// <summary>
/// Splits SQL scripts at semicolons that are outside strings, comments and
/// dialect-specific block constructs. Ported from the Rust/Tauri edition:
/// <c>sql-lang-db2::script::scan_db2_sql</c>,
/// <c>sql-lang-postgres::script::scan_postgresql_sql</c> and
/// <c>sql-lang-sqlite::script::scan_sqlite_sql</c>.
/// </summary>
public static class SqlStatementScanner
{
    public static SqlScriptScan Scan(string sql, SqlDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(sql);
        return dialect switch
        {
            SqlDialect.Db2 => ScanDb2(sql),
            SqlDialect.PostgreSql => ScanPostgreSql(sql),
            SqlDialect.Sqlite => ScanSqlite(sql),
            SqlDialect.Oracle => ScanOracle(sql),
            SqlDialect.MySql => ScanGeneric(sql, backticks: true, brackets: false, hashComments: true, nestedBlockComments: false),
            SqlDialect.Mssql => ScanGeneric(sql, backticks: false, brackets: true, hashComments: false, nestedBlockComments: true),
            _ => ScanGeneric(sql, backticks: false, brackets: false, hashComments: false, nestedBlockComments: false),
        };
    }

    /// <summary>
    /// Returns statement texts sliced from <paramref name="sql"/>. Whitespace-only
    /// fragments are preserved for the generic/Netezza splitter (historical
    /// behaviour) and skipped by the Db2/PostgreSQL/SQLite/Oracle scanners.
    /// </summary>
    public static List<string> Split(string sql, SqlDialect dialect)
    {
        var scan = Scan(sql, dialect);
        var result = new List<string>(scan.Statements.Count);
        foreach (var span in scan.Statements)
        {
            result.Add(sql.Substring(span.Start, span.Length));
        }

        return result;
    }

    /// <summary>
    /// Index of the statement that contains (or immediately precedes)
    /// <paramref name="position"/>, or -1 when the script has no spans.
    /// </summary>
    public static int FindStatementIndex(IReadOnlyList<SqlStatementSpan> statements, int position)
    {
        for (var i = 0; i < statements.Count; i++)
        {
            if (statements[i].End >= position)
            {
                return i;
            }
        }

        return statements.Count - 1;
    }

    private static SqlScriptScan ScanDb2(string source)
    {
        var statements = new List<SqlStatementSpan>();
        var start = 0;
        var index = 0;
        var hasCode = false;
        var beginDepth = 0;
        var caseDepth = 0;
        var unterminated = false;

        while (index < source.Length)
        {
            // Track SQL PL block structure. CASE ... END (expression or
            // searched-case statement) and compound ends (END IF, END LOOP,
            // END WHILE, END FOR, END REPEAT, END CASE) never close a BEGIN
            // block; only a bare END with no open CASE does. Matching stays
            // shallow on purpose: an unclosed BEGIN keeps the rest of the
            // script together instead of executing a half procedure body.
            if (IsWordStart(source, index))
            {
                var end = WordEnd(source, index);
                var word = source[index..end];
                if (word.Equals("begin", StringComparison.OrdinalIgnoreCase))
                {
                    beginDepth++;
                }
                else if (word.Equals("case", StringComparison.OrdinalIgnoreCase))
                {
                    caseDepth++;
                }
                else if (word.Equals("end", StringComparison.OrdinalIgnoreCase))
                {
                    var next = NextWord(source, end);
                    var isCompoundEnd = next is not null && IsCompoundEndKeyword(next);
                    if (next is not null && next.Equals("case", StringComparison.OrdinalIgnoreCase))
                    {
                        caseDepth = Math.Max(0, caseDepth - 1);
                    }
                    else if (!isCompoundEnd)
                    {
                        if (caseDepth > 0)
                        {
                            caseDepth--;
                        }
                        else
                        {
                            beginDepth = Math.Max(0, beginDepth - 1);
                        }
                    }
                }

                hasCode = true;
                index = end;
                continue;
            }

            var current = source[index];
            if (current == '-' && index + 1 < source.Length && source[index + 1] == '-')
            {
                index += 2;
                while (index < source.Length && source[index] is not ('\n' or '\r'))
                {
                    index++;
                }

                continue;
            }

            if (current == '/' && index + 1 < source.Length && source[index + 1] == '*')
            {
                index += 2;
                var closed = false;
                while (index + 1 < source.Length)
                {
                    if (source[index] == '*' && source[index + 1] == '/')
                    {
                        index += 2;
                        closed = true;
                        break;
                    }

                    index++;
                }

                if (!closed)
                {
                    unterminated = true;
                    hasCode = true;
                    index = source.Length;
                }

                continue;
            }

            if (current is '\'' or '"')
            {
                var end = SkipQuoted(source, index, current, current, allowDoubledClose: true);
                hasCode = true;
                if (end < 0)
                {
                    unterminated = true;
                    index = source.Length;
                }
                else
                {
                    index = end;
                }

                continue;
            }

            if (current == ';')
            {
                if (beginDepth == 0)
                {
                    PushStatement(statements, source, start, index);
                    start = index + 1;
                    hasCode = false;
                }
                else
                {
                    hasCode = true;
                }

                index++;
                continue;
            }

            if (!char.IsWhiteSpace(current))
            {
                hasCode = true;
            }

            index++;
        }

        if (hasCode)
        {
            PushStatement(statements, source, start, source.Length);
        }

        if (beginDepth > 0 || caseDepth > 0)
        {
            unterminated = true;
        }

        return new SqlScriptScan(statements, unterminated);
    }

    private static SqlScriptScan ScanPostgreSql(string source)
    {
        var statements = new List<SqlStatementSpan>();
        var statementStart = 0;
        var hasStatementCode = false;
        var index = 0;
        var unterminated = false;
        // BEGIN ATOMIC ... END (SQL-standard function body) is one statement even
        // though it contains semicolons. Track nesting so the body's terminators
        // do not split the script, and count CASE expressions inside it so their
        // END does not close the body.
        var atomicBodies = 0;
        var atomicCases = 0;

        while (index < source.Length)
        {
            var current = source[index];
            if (current == '-' && index + 1 < source.Length && source[index + 1] == '-')
            {
                index += 2;
                while (index < source.Length && source[index] != '\n')
                {
                    index++;
                }

                continue;
            }

            if (current == '/' && index + 1 < source.Length && source[index + 1] == '*')
            {
                index += 2;
                var depth = 1;
                while (index < source.Length && depth > 0)
                {
                    if (source[index] == '/' && index + 1 < source.Length && source[index + 1] == '*')
                    {
                        depth++;
                        index += 2;
                    }
                    else if (source[index] == '*' && index + 1 < source.Length && source[index + 1] == '/')
                    {
                        depth--;
                        index += 2;
                    }
                    else
                    {
                        index++;
                    }
                }

                if (depth > 0)
                {
                    unterminated = true;
                    index = source.Length;
                }

                continue;
            }

            if (current == '\'')
            {
                hasStatementCode = true;
                var escapeString = index > 0
                    && (source[index - 1] is 'e' or 'E')
                    && (index < 2 || !IsIdentifierChar(source[index - 2]));
                var end = SkipQuoted(source, index, '\'', '\'', allowDoubledClose: true, escapeCharacter: escapeString ? '\\' : null);
                if (end < 0)
                {
                    unterminated = true;
                    index = source.Length;
                }
                else
                {
                    index = end;
                }

                continue;
            }

            if (current == '"')
            {
                hasStatementCode = true;
                var end = SkipQuoted(source, index, '"', '"', allowDoubledClose: true);
                if (end < 0)
                {
                    unterminated = true;
                    index = source.Length;
                }
                else
                {
                    index = end;
                }

                continue;
            }

            if (current == '$')
            {
                var delimiterEnd = DollarDelimiterEnd(source, index);
                if (delimiterEnd > 0 && (index == 0 || !IsIdentifierChar(source[index - 1])))
                {
                    hasStatementCode = true;
                    var delimiter = source[index..delimiterEnd];
                    var bodyStart = delimiterEnd;
                    var relativeEnd = source.AsSpan(bodyStart).IndexOf(delimiter, StringComparison.Ordinal);
                    if (relativeEnd >= 0)
                    {
                        index = bodyStart + relativeEnd + delimiter.Length;
                    }
                    else
                    {
                        unterminated = true;
                        index = source.Length;
                    }

                    continue;
                }

                hasStatementCode = true;
                index++;
                continue;
            }

            if (current == ';')
            {
                if (hasStatementCode && atomicBodies == 0)
                {
                    statements.Add(new SqlStatementSpan(statementStart, index - statementStart));
                }

                if (atomicBodies == 0)
                {
                    statementStart = index + 1;
                    hasStatementCode = false;
                }

                index++;
                continue;
            }

            if (char.IsAsciiLetter(current) || current == '_')
            {
                var wordStart = index;
                while (index < source.Length && IsIdentifierChar(source[index]))
                {
                    index++;
                }

                var word = source[wordStart..index];
                if (word.Equals("begin", StringComparison.OrdinalIgnoreCase) && NextWordIs(source, index, "atomic"))
                {
                    atomicBodies++;
                }
                else if (word.Equals("case", StringComparison.OrdinalIgnoreCase) && atomicBodies > 0)
                {
                    atomicCases++;
                }
                else if (word.Equals("end", StringComparison.OrdinalIgnoreCase) && atomicBodies > 0)
                {
                    if (atomicCases > 0)
                    {
                        atomicCases--;
                    }
                    else
                    {
                        atomicBodies--;
                    }
                }

                hasStatementCode = true;
                continue;
            }

            if (!char.IsWhiteSpace(current))
            {
                hasStatementCode = true;
            }

            index++;
        }

        if (hasStatementCode)
        {
            statements.Add(new SqlStatementSpan(statementStart, source.Length - statementStart));
        }

        if (atomicBodies > 0 || atomicCases > 0)
        {
            unterminated = true;
        }

        return new SqlScriptScan(statements, unterminated);
    }

    private static SqlScriptScan ScanSqlite(string source)
    {
        var statements = new List<SqlStatementSpan>();
        var start = 0;
        var index = 0;
        var hasCode = false;
        var words = new List<string>();
        var isTrigger = false;
        var triggerBegun = false;
        var triggerComplete = false;
        var caseDepth = 0;
        var unterminated = false;

        while (index < source.Length)
        {
            var current = source[index];
            if (current == '-' && index + 1 < source.Length && source[index + 1] == '-')
            {
                index += 2;
                while (index < source.Length && source[index] is not ('\n' or '\r'))
                {
                    index++;
                }

                continue;
            }

            if (current == '/' && index + 1 < source.Length && source[index + 1] == '*')
            {
                index += 2;
                var depth = 1;
                while (index < source.Length && depth > 0)
                {
                    if (source[index] == '/' && index + 1 < source.Length && source[index + 1] == '*')
                    {
                        depth++;
                        index += 2;
                    }
                    else if (source[index] == '*' && index + 1 < source.Length && source[index + 1] == '/')
                    {
                        depth--;
                        index += 2;
                    }
                    else
                    {
                        index++;
                    }
                }

                if (depth > 0)
                {
                    unterminated = true;
                    hasCode = true;
                    index = source.Length;
                }

                continue;
            }

            if (current is '\'' or '"' or '`')
            {
                var end = SkipQuoted(source, index, current, current, allowDoubledClose: true);
                hasCode = true;
                if (end < 0)
                {
                    unterminated = true;
                    index = source.Length;
                }
                else
                {
                    index = end;
                }

                continue;
            }

            if (current == '[')
            {
                var end = SkipQuoted(source, index, '[', ']', allowDoubledClose: false);
                hasCode = true;
                if (end < 0)
                {
                    unterminated = true;
                    index = source.Length;
                }
                else
                {
                    index = end;
                }

                continue;
            }

            if (current == ';')
            {
                hasCode = true;
                if (isTrigger && triggerBegun && !triggerComplete)
                {
                    // The semicolon belongs to the trigger body; only the final
                    // END closes the CREATE TRIGGER statement.
                }
                else if (!isTrigger || triggerComplete || !triggerBegun)
                {
                    PushStatement(statements, source, start, index);
                    start = index + 1;
                    hasCode = false;
                    words.Clear();
                    isTrigger = false;
                    triggerBegun = false;
                    triggerComplete = false;
                    caseDepth = 0;
                }

                index++;
                continue;
            }

            if (IsWordStart(source, index))
            {
                var wordStart = index;
                index++;
                while (index < source.Length && IsWordContinue(source[index]))
                {
                    index++;
                }

                var word = source[wordStart..index];
                words.Add(word.ToUpperInvariant());
                hasCode = true;
                if (!isTrigger && IsCreateTrigger(words))
                {
                    isTrigger = true;
                }

                if (isTrigger)
                {
                    if (word.Equals("begin", StringComparison.OrdinalIgnoreCase) && !triggerBegun)
                    {
                        triggerBegun = true;
                    }
                    else if (word.Equals("case", StringComparison.OrdinalIgnoreCase) && triggerBegun)
                    {
                        caseDepth++;
                    }
                    else if (word.Equals("end", StringComparison.OrdinalIgnoreCase) && triggerBegun && caseDepth > 0)
                    {
                        caseDepth--;
                    }
                    else if (word.Equals("end", StringComparison.OrdinalIgnoreCase) && triggerBegun)
                    {
                        triggerComplete = true;
                    }
                }

                continue;
            }

            if (!char.IsWhiteSpace(current))
            {
                hasCode = true;
            }

            index++;
        }

        if (hasCode)
        {
            PushStatement(statements, source, start, source.Length);
        }

        return new SqlScriptScan(statements, unterminated);
    }

    private static SqlScriptScan ScanOracle(string source)
    {
        // SQL*Plus scripts end PL/SQL blocks with a slash on its own line. The
        // generic semicolon rules apply to ordinary SQL; inside a PL/SQL block
        // BEGIN/END (with CASE nesting) keeps the body intact.
        var statements = new List<SqlStatementSpan>();
        var start = 0;
        var index = 0;
        var hasCode = false;
        var beginDepth = 0;
        var caseDepth = 0;
        var unterminated = false;

        while (index < source.Length)
        {
            if (IsSlashLine(source, index))
            {
                if (beginDepth == 0 && hasCode)
                {
                    PushStatement(statements, source, start, index);
                }

                while (index < source.Length && source[index] is not ('\n' or '\r'))
                {
                    index++;
                }

                start = index;
                hasCode = false;
                beginDepth = 0;
                caseDepth = 0;
                continue;
            }

            var current = source[index];
            if (current == '-' && index + 1 < source.Length && source[index + 1] == '-')
            {
                index += 2;
                while (index < source.Length && source[index] is not ('\n' or '\r'))
                {
                    index++;
                }

                continue;
            }

            if (current == '/' && index + 1 < source.Length && source[index + 1] == '*')
            {
                index += 2;
                var depth = 1;
                while (index < source.Length && depth > 0)
                {
                    if (source[index] == '/' && index + 1 < source.Length && source[index + 1] == '*')
                    {
                        depth++;
                        index += 2;
                    }
                    else if (source[index] == '*' && index + 1 < source.Length && source[index + 1] == '/')
                    {
                        depth--;
                        index += 2;
                    }
                    else
                    {
                        index++;
                    }
                }

                if (depth > 0)
                {
                    unterminated = true;
                    index = source.Length;
                }

                continue;
            }

            if ((current is 'q' or 'Q') && index + 1 < source.Length && source[index + 1] == '\'')
            {
                hasCode = true;
                var end = SkipOracleQQuote(source, index);
                if (end < 0)
                {
                    unterminated = true;
                    index = source.Length;
                }
                else
                {
                    index = end;
                }

                continue;
            }

            if (current is '\'' or '"')
            {
                hasCode = true;
                var end = SkipQuoted(source, index, current, current, allowDoubledClose: true);
                if (end < 0)
                {
                    unterminated = true;
                    index = source.Length;
                }
                else
                {
                    index = end;
                }

                continue;
            }

            if (IsWordStart(source, index))
            {
                var wordStart = index;
                index = WordEnd(source, index);
                var word = source[wordStart..index];
                if (word.Equals("begin", StringComparison.OrdinalIgnoreCase))
                {
                    beginDepth++;
                }
                else if (word.Equals("case", StringComparison.OrdinalIgnoreCase))
                {
                    caseDepth++;
                }
                else if (word.Equals("end", StringComparison.OrdinalIgnoreCase))
                {
                    var next = NextWord(source, index);
                    var isCompoundEnd = next is not null && IsCompoundEndKeyword(next);
                    if (next is not null && next.Equals("case", StringComparison.OrdinalIgnoreCase))
                    {
                        caseDepth = Math.Max(0, caseDepth - 1);
                    }
                    else if (!isCompoundEnd)
                    {
                        if (caseDepth > 0)
                        {
                            caseDepth--;
                        }
                        else
                        {
                            beginDepth = Math.Max(0, beginDepth - 1);
                        }
                    }
                }

                hasCode = true;
                continue;
            }

            if (current == ';')
            {
                hasCode = true;
                if (beginDepth == 0)
                {
                    PushStatement(statements, source, start, index);
                    start = index + 1;
                    hasCode = false;
                    caseDepth = 0;
                }

                index++;
                continue;
            }

            if (!char.IsWhiteSpace(current))
            {
                hasCode = true;
            }

            index++;
        }

        if (hasCode)
        {
            PushStatement(statements, source, start, source.Length);
        }

        if (beginDepth > 0 || caseDepth > 0)
        {
            unterminated = true;
        }

        return new SqlScriptScan(statements, unterminated);
    }

    private static SqlScriptScan ScanGeneric(
        string source,
        bool backticks,
        bool brackets,
        bool hashComments,
        bool nestedBlockComments)
    {
        var statements = new List<SqlStatementSpan>();
        var start = 0;
        var index = 0;
        var parenDepth = 0;
        var unterminated = false;

        while (index < source.Length)
        {
            var current = source[index];
            if (current == '-' && index + 1 < source.Length && source[index + 1] == '-')
            {
                index += 2;
                while (index < source.Length && source[index] is not ('\n' or '\r'))
                {
                    index++;
                }

                continue;
            }

            if (hashComments && current == '#')
            {
                index++;
                while (index < source.Length && source[index] is not ('\n' or '\r'))
                {
                    index++;
                }

                continue;
            }

            if (current == '/' && index + 1 < source.Length && source[index + 1] == '*')
            {
                index += 2;
                var depth = 1;
                while (index < source.Length && depth > 0)
                {
                    if (nestedBlockComments && source[index] == '/' && index + 1 < source.Length && source[index + 1] == '*')
                    {
                        depth++;
                        index += 2;
                    }
                    else if (source[index] == '*' && index + 1 < source.Length && source[index + 1] == '/')
                    {
                        depth--;
                        index += 2;
                    }
                    else
                    {
                        index++;
                    }
                }

                if (depth > 0)
                {
                    unterminated = true;
                    index = source.Length;
                }

                continue;
            }

            if (current is '\'' or '"')
            {
                var end = SkipQuoted(source, index, current, current, allowDoubledClose: true);
                if (end < 0)
                {
                    unterminated = true;
                    index = source.Length;
                }
                else
                {
                    index = end;
                }

                continue;
            }

            if (backticks && current == '`')
            {
                var end = SkipQuoted(source, index, '`', '`', allowDoubledClose: true);
                if (end < 0)
                {
                    unterminated = true;
                    index = source.Length;
                }
                else
                {
                    index = end;
                }

                continue;
            }

            if (brackets && current == '[')
            {
                var end = SkipQuoted(source, index, '[', ']', allowDoubledClose: false);
                if (end < 0)
                {
                    unterminated = true;
                    index = source.Length;
                }
                else
                {
                    index = end;
                }

                continue;
            }

            if (current == '(')
            {
                parenDepth++;
                index++;
                continue;
            }

            if (current == ')')
            {
                parenDepth = Math.Max(0, parenDepth - 1);
                index++;
                continue;
            }

            if (current == ';' && parenDepth == 0)
            {
                statements.Add(new SqlStatementSpan(start, index - start));
                start = index + 1;
            }

            index++;
        }

        statements.Add(new SqlStatementSpan(start, source.Length - start));
        return new SqlScriptScan(statements, unterminated);
    }

    private static void PushStatement(List<SqlStatementSpan> statements, string source, int start, int end)
    {
        if (end <= start)
        {
            return;
        }

        if (source.AsSpan(start, end - start).IsWhiteSpace())
        {
            return;
        }

        statements.Add(new SqlStatementSpan(start, end - start));
    }

    private static bool IsCreateTrigger(List<string> words)
        => words.Count > 0
            && words[0] == "CREATE"
            && words.Take(5).Any(static word => word == "TRIGGER");

    private static bool IsSlashLine(string source, int index)
    {
        if (index > 0 && source[index - 1] is not ('\n' or '\r'))
        {
            return false;
        }

        if (index >= source.Length || source[index] != '/')
        {
            return false;
        }

        var cursor = index + 1;
        while (cursor < source.Length && char.IsWhiteSpace(source[cursor]))
        {
            cursor++;
        }

        return cursor >= source.Length || source[cursor] is '\n' or '\r';
    }

    private static int SkipOracleQQuote(string source, int index)
    {
        // q'[...]', q'{...}', q'(...)', q'<...>' or q'X...X' with a custom delimiter.
        var cursor = index + 2;
        if (cursor >= source.Length)
        {
            return -1;
        }

        var open = source[cursor];
        var close = open switch
        {
            '[' => ']',
            '{' => '}',
            '(' => ')',
            '<' => '>',
            _ => open,
        };
        cursor++;
        while (cursor < source.Length)
        {
            if (source[cursor] == close && cursor + 1 < source.Length && source[cursor + 1] == '\'')
            {
                return cursor + 2;
            }

            cursor++;
        }

        return -1;
    }

    private static int DollarDelimiterEnd(string source, int start)
    {
        var index = start + 1;
        if (index < source.Length && source[index] == '$')
        {
            return index + 1;
        }

        if (index >= source.Length
            || !(char.IsAsciiLetter(source[index]) || source[index] == '_' || source[index] >= 0x80))
        {
            return -1;
        }

        index++;
        while (index < source.Length
               && (char.IsAsciiLetterOrDigit(source[index]) || source[index] == '_' || source[index] >= 0x80))
        {
            index++;
        }

        return index < source.Length && source[index] == '$' ? index + 1 : -1;
    }

    private static int SkipQuoted(
        string source,
        int start,
        char open,
        char close,
        bool allowDoubledClose,
        char? escapeCharacter = null)
    {
        var index = start + 1;
        while (index < source.Length)
        {
            if (escapeCharacter is { } escape && source[index] == escape)
            {
                index += 2;
                continue;
            }

            if (source[index] == close)
            {
                if (allowDoubledClose && index + 1 < source.Length && source[index + 1] == close)
                {
                    index += 2;
                }
                else
                {
                    return index + 1;
                }
            }
            else
            {
                index++;
            }
        }

        return -1;
    }

    private static bool NextWordIs(string source, int index, string expected)
    {
        while (true)
        {
            while (index < source.Length && char.IsWhiteSpace(source[index]))
            {
                index++;
            }

            if (index + 1 < source.Length && source[index] == '-' && source[index + 1] == '-')
            {
                index += 2;
                while (index < source.Length && source[index] != '\n')
                {
                    index++;
                }

                continue;
            }

            if (index + 1 < source.Length && source[index] == '/' && source[index + 1] == '*')
            {
                index += 2;
                var depth = 1;
                while (index < source.Length && depth > 0)
                {
                    if (source[index] == '/' && index + 1 < source.Length && source[index + 1] == '*')
                    {
                        depth++;
                        index += 2;
                    }
                    else if (source[index] == '*' && index + 1 < source.Length && source[index + 1] == '/')
                    {
                        depth--;
                        index += 2;
                    }
                    else
                    {
                        index++;
                    }
                }

                continue;
            }

            break;
        }

        var end = index + expected.Length;
        return end <= source.Length
            && source.AsSpan(index, expected.Length).Equals(expected, StringComparison.OrdinalIgnoreCase)
            && (end >= source.Length || !IsIdentifierChar(source[end]));
    }

    private static string? NextWord(string source, int index)
    {
        while (index < source.Length && char.IsWhiteSpace(source[index]))
        {
            index++;
        }

        if (index >= source.Length || !IsWordStart(source, index))
        {
            return null;
        }

        return source[index..WordEnd(source, index)];
    }

    private static bool IsCompoundEndKeyword(string word)
        => word.Equals("if", StringComparison.OrdinalIgnoreCase)
            || word.Equals("loop", StringComparison.OrdinalIgnoreCase)
            || word.Equals("while", StringComparison.OrdinalIgnoreCase)
            || word.Equals("for", StringComparison.OrdinalIgnoreCase)
            || word.Equals("repeat", StringComparison.OrdinalIgnoreCase)
            || word.Equals("case", StringComparison.OrdinalIgnoreCase);

    private static bool IsWordStart(string source, int index)
    {
        var current = source[index];
        if (!(char.IsAsciiLetter(current) || current == '_'))
        {
            return false;
        }

        if (index == 0)
        {
            return true;
        }

        var previous = source[index - 1];
        return !(char.IsAsciiLetterOrDigit(previous) || previous is '_' or '$');
    }

    private static int WordEnd(string source, int start)
    {
        var index = start;
        while (index < source.Length && IsWordContinue(source[index]))
        {
            index++;
        }

        return index;
    }

    private static bool IsWordContinue(char value)
        => char.IsAsciiLetterOrDigit(value) || value is '_' or '$';

    private static bool IsIdentifierChar(char value)
        => char.IsAsciiLetterOrDigit(value) || value == '_' || value >= 0x80;
}
