using System.Text.RegularExpressions;

namespace JustyBase.NetezzaSqlParser.Linter;

internal readonly record struct ProcedureBlockBalance(
    int Begins,
    int Ends,
    int UnmatchedCases);

/// <summary>
/// Counts procedural <c>BEGIN … END</c> blocks against SQL <c>CASE … END</c>
/// expressions.
///
/// Netezza has no <c>CASE</c> statement, so every <c>CASE</c> token opens an
/// expression that a bare <c>END</c> closes. A bare <c>END</c> therefore closes
/// the innermost open CASE before it can close a <c>BEGIN</c> block. Counting
/// keywords with a lookahead on the next token misfires for <c>END WHERE</c>,
/// <c>END + 1</c>, <c>END::INT</c>, <c>END IS NULL</c> and similar valid
/// expression continuations.
/// </summary>
internal static class ProcedureBlockScanner
{
    private static readonly Regex TokenPattern = new(
        @"\bEND\s+(?:IF|LOOP|CASE|TRANSACTION|WORK)\b|\bBEGIN\s+(?:TRANSACTION|WORK)\b|\bBEGIN\b|\bEND\b|\bCASE\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static ProcedureBlockBalance Measure(string body)
    {
        int begins = 0;
        int ends = 0;
        int unmatchedCases = 0;

        // A CASE expression cannot span a statement terminator. Splitting the
        // sanitized text keeps a missing CASE END from consuming the next
        // block's END and hiding the imbalance.
        foreach (var statement in Sanitize(body).Split(';'))
        {
            int openCases = 0;
            foreach (Match match in TokenPattern.Matches(statement))
            {
                var token = Regex.Replace(match.Value.ToUpperInvariant(), @"\s+", " ");
                if (token == "CASE")
                {
                    openCases++;
                    continue;
                }
                if (token == "BEGIN")
                {
                    begins++;
                    continue;
                }
                if (token.StartsWith("END ", StringComparison.Ordinal))
                {
                    if (token == "END CASE" && openCases > 0)
                    {
                        openCases--;
                    }
                    continue;
                }
                if (openCases > 0)
                {
                    openCases--;
                    continue;
                }
                ends++;
            }
            unmatchedCases += openCases;
        }

        return new ProcedureBlockBalance(begins, ends, unmatchedCases);
    }

    /// <summary>
    /// Replace comments and string/identifier literals with spaces while
    /// preserving length. Semicolons inside them must not split statements.
    /// </summary>
    private static string Sanitize(string sql)
    {
        var buffer = sql.ToCharArray();
        bool inSingle = false, inDouble = false, inLine = false, inBlock = false;

        for (int i = 0; i < sql.Length; i++)
        {
            char c = sql[i];
            char next = i + 1 < sql.Length ? sql[i + 1] : '\0';

            if (inLine)
            {
                if (c == '\n')
                {
                    inLine = false;
                    continue;
                }
                buffer[i] = ' ';
                continue;
            }
            if (inBlock)
            {
                buffer[i] = ' ';
                if (c == '*' && next == '/')
                {
                    buffer[i + 1] = ' ';
                    inBlock = false;
                    i++;
                }
                continue;
            }
            if (inSingle)
            {
                buffer[i] = ' ';
                if (c == '\'' && next == '\'')
                {
                    buffer[i + 1] = ' ';
                    i++;
                }
                else if (c == '\'')
                {
                    inSingle = false;
                }
                continue;
            }
            if (inDouble)
            {
                buffer[i] = ' ';
                if (c == '"' && next == '"')
                {
                    buffer[i + 1] = ' ';
                    i++;
                }
                else if (c == '"')
                {
                    inDouble = false;
                }
                continue;
            }

            if (c == '-' && next == '-')
            {
                buffer[i] = ' ';
                inLine = true;
            }
            else if (c == '/' && next == '*')
            {
                buffer[i] = ' ';
                inBlock = true;
            }
            else if (c == '\'')
            {
                buffer[i] = ' ';
                inSingle = true;
            }
            else if (c == '"')
            {
                buffer[i] = ' ';
                inDouble = true;
            }
        }

        return new string(buffer);
    }
}
