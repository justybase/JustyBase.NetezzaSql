using System.Text.RegularExpressions;

namespace JustyBase.NetezzaSqlParser.Linter;

/// <summary>
/// Oracle dialect lint rules. Port of extensions/oracle/src/sql/qualityRules.ts
/// from the reference TypeScript project. Registered into a QualityRuleRegistry
/// via AddRules when the active document dialect is Oracle.
/// </summary>
public static class OracleLintRules
{
    public static IReadOnlyList<LintRule> AllRules { get; } =
    [
        new RuleORA001_SelectStar(),
        new RuleORA002_DeleteWithoutWhere(),
        new RuleORA003_UpdateWithoutWhere(),
        new RuleORA004_RownumWithOrderBy(),
    ];
}

// ====== ORA001: SELECT * ======
public class RuleORA001_SelectStar : LintRule
{
    public override string Id => "ORA001";
    public override string Name => "Select Star";
    public override string Description => "Avoid SELECT * in production Oracle queries when a stable projection is possible.";
    public override LintSeverity DefaultSeverity => LintSeverity.Warning;
    public override RuleCost Cost => RuleCost.Cheap;

    public override IEnumerable<LintIssue> Check(string sql)
    {
        foreach (Match m in Regex.Matches(sql, @"\bSELECT\s+\*", RegexOptions.IgnoreCase))
        {
            if (LintHelpers.IsInsideStringOrComment(sql, m.Index)) continue;
            var starPos = m.Value.LastIndexOf('*');
            yield return new LintIssue(Id, $"{Id}: {Description}", DefaultSeverity,
                m.Index + starPos, m.Index + starPos + 1);
        }
    }
}

// ====== ORA002: DELETE without WHERE ======
public class RuleORA002_DeleteWithoutWhere : LintRule
{
    public override string Id => "ORA002";
    public override string Name => "Delete Without Where";
    public override string Description => "DELETE without WHERE removes every row in the target table.";
    public override LintSeverity DefaultSeverity => LintSeverity.Error;
    public override RuleCost Cost => RuleCost.Cheap;

    public override IEnumerable<LintIssue> Check(string sql)
    {
        foreach (Match m in Regex.Matches(sql, @"\bDELETE\s+FROM\s+(?:""[^""]+""|[A-Za-z_][\w$#]*)(?:\s*\.\s*(?:""[^""]+""|[A-Za-z_][\w$#]*)){0,2}", RegexOptions.IgnoreCase))
        {
            if (LintHelpers.IsInsideStringOrComment(sql, m.Index)) continue;
            var end = OracleLintHelpers.StatementEnd(sql, m.Index);
            var tail = sql[(m.Index + m.Length)..end];
            if (!Regex.IsMatch(tail, @"\bWHERE\b", RegexOptions.IgnoreCase))
                yield return new LintIssue(Id, $"{Id}: {Description}", DefaultSeverity, m.Index, m.Index + 6);
        }
    }
}

// ====== ORA003: UPDATE without WHERE ======
public class RuleORA003_UpdateWithoutWhere : LintRule
{
    public override string Id => "ORA003";
    public override string Name => "Update Without Where";
    public override string Description => "UPDATE without WHERE changes every row in the target table.";
    public override LintSeverity DefaultSeverity => LintSeverity.Error;
    public override RuleCost Cost => RuleCost.Cheap;

    public override IEnumerable<LintIssue> Check(string sql)
    {
        foreach (Match m in Regex.Matches(sql, @"\bUPDATE\s+(?:""[^""]+""|[A-Za-z_][\w$#]*)(?:\s*\.\s*(?:""[^""]+""|[A-Za-z_][\w$#]*)){0,2}\s+SET\b", RegexOptions.IgnoreCase))
        {
            if (LintHelpers.IsInsideStringOrComment(sql, m.Index)) continue;
            var end = OracleLintHelpers.StatementEnd(sql, m.Index);
            var tail = sql[(m.Index + m.Length)..end];
            if (!Regex.IsMatch(tail, @"\bWHERE\b", RegexOptions.IgnoreCase))
                yield return new LintIssue(Id, $"{Id}: {Description}", DefaultSeverity, m.Index, m.Index + 6);
        }
    }
}

// ====== ORA004: ROWNUM with ORDER BY ======
public class RuleORA004_RownumWithOrderBy : LintRule
{
    public override string Id => "ORA004";
    public override string Name => "Rownum With Order By";
    public override string Description => "ROWNUM is evaluated before a same-level ORDER BY; use an ordered subquery or FETCH FIRST for deterministic top-N results.";
    public override LintSeverity DefaultSeverity => LintSeverity.Warning;
    public override RuleCost Cost => RuleCost.Cheap;

    public override IEnumerable<LintIssue> Check(string sql)
    {
        foreach (Match m in Regex.Matches(sql, @"\bROWNUM\b", RegexOptions.IgnoreCase))
        {
            if (LintHelpers.IsInsideStringOrComment(sql, m.Index)) continue;
            var end = OracleLintHelpers.StatementEnd(sql, m.Index);
            var after = sql[(m.Index + m.Length)..end];
            if (Regex.IsMatch(after, @"\bORDER\s+BY\b", RegexOptions.IgnoreCase) &&
                !Regex.IsMatch(after, @"\bFETCH\s+(?:FIRST|NEXT)\b", RegexOptions.IgnoreCase))
                yield return new LintIssue(Id, $"{Id}: {Description}", DefaultSeverity,
                    m.Index, m.Index + m.Length);
        }
    }
}

internal static class OracleLintHelpers
{
    /// <summary>
    /// Port of statementEnd() from qualityRules.ts: finds the end of the current
    /// statement (index of ';' or end of input) while respecting single/double
    /// quotes (with doubled-quote escapes), q-quoted strings, line comments and
    /// block comments.
    /// </summary>
    public static int StatementEnd(string sql, int start)
    {
        char? quote = null;
        char? qQuoteDelim = null;
        var lineComment = false;
        var blockComment = false;

        static bool IsOpeningBracket(char c) => c is '[' or '{' or '<' or '(';
        static char MatchingBracket(char c) => c switch
        {
            '[' => ']',
            '{' => '}',
            '<' => '>',
            '(' => ')',
            _ => c
        };

        for (var index = start; index < sql.Length; index++)
        {
            var c = sql[index];
            var next = index + 1 < sql.Length ? sql[index + 1] : '\0';

            if (lineComment)
            {
                if (c is '\n' or '\r') lineComment = false;
                continue;
            }

            if (blockComment)
            {
                if (c == '*' && next == '/')
                {
                    blockComment = false;
                    index++;
                }
                continue;
            }

            if (qQuoteDelim is not null)
            {
                if (IsOpeningBracket(qQuoteDelim.Value))
                {
                    if (c == MatchingBracket(qQuoteDelim.Value) && next == '\'')
                    {
                        qQuoteDelim = null;
                        index++;
                    }
                }
                else if (c == qQuoteDelim.Value && next == '\'')
                {
                    qQuoteDelim = null;
                    index++;
                }
                continue;
            }

            if (quote is not null)
            {
                if (c == quote)
                {
                    if (next == quote) index++;
                    else quote = null;
                }
                continue;
            }

            if (c == '-' && next == '-')
            {
                lineComment = true;
                index++;
                continue;
            }
            if (c == '/' && next == '*')
            {
                blockComment = true;
                index++;
                continue;
            }
            if (c == 'q' && next == '\'')
            {
                var delimStart = index + 2;
                if (delimStart < sql.Length)
                {
                    qQuoteDelim = sql[delimStart];
                    index = delimStart;
                    continue;
                }
            }
            if (c is '\'' or '"')
            {
                quote = c;
                continue;
            }
            if (c == ';')
                return index;
        }

        return sql.Length;
    }
}
