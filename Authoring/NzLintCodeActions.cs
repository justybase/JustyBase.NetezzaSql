using System.Text.RegularExpressions;
using JustyBase.NetezzaSqlParser.Linter;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.NetezzaSqlParser.Authoring;

/// <summary>
/// Provides one-click quick fixes for SQL lint issues (Lite QUICK_FIX_MATRIX parity subset).
/// Each fix is a pure string transformation — no UI or editor dependencies.
/// </summary>
public static class NzLintCodeActions
{
    public static SqlQuickFixInfo? GetQuickFixInfo(LintIssue issue, string sql, ISchemaProvider? schema = null)
    {
        var action = GetQuickFix(issue, sql, schema);
        if (action is null) return null;
        var result = action.Value.Apply(sql);
        var start = 0;
        while (start < sql.Length && start < result.Length && sql[start] == result[start]) start++;
        var end = sql.Length;
        var resultEnd = result.Length;
        while (end > start && resultEnd > start && sql[end - 1] == result[resultEnd - 1])
        { end--; resultEnd--; }
        return new SqlQuickFixInfo(action.Value.Description, GetSafety(issue.RuleId),
            result == sql ? [] : [new SqlTextEdit(start, end, result[start..resultEnd])]);
    }

    // Single source of truth for quick-fix safety. A fix is Safe when it is
    // deterministic and cannot change query meaning.
    private static readonly HashSet<string> SafeQuickFixCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "PAR004", "PAR002", "SQL012", "SQL046", "NZ012", "NZL006", "SQL007", "NZP012", "SQL048",
        "NZ007", "NZ021", "PARSE001", "NZ024", "PAR101"
    };

    // Fix-all applies a subset of the Safe fixes. Safe fixes that apply a
    // suggestion choice (PAR004 typo, SQL048 qualification, NZL006) stay explicit-only.
    private static readonly HashSet<string> SafeFixAllCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "SQL007", "SQL012", "NZ007", "NZ012", "SQL046", "NZP012",
        "NZ021", "PAR002", "PARSE001", "NZ024", "PAR101"
    };

    /// <summary>Returns the safety classification of the quick fix for a rule.</summary>
    public static SqlQuickFixSafety GetSafety(string ruleId)
        => !string.IsNullOrWhiteSpace(ruleId) && SafeQuickFixCodes.Contains(ruleId)
            ? SqlQuickFixSafety.Safe
            : SqlQuickFixSafety.ReviewRequired;

    /// <summary>Returns whether the rule is eligible for automatic Fix-all (Safe and deterministic).</summary>
    public static bool IsSafeForFixAll(string ruleId)
        => !string.IsNullOrWhiteSpace(ruleId)
            && SafeFixAllCodes.Contains(ruleId)
            && GetSafety(ruleId) == SqlQuickFixSafety.Safe;

    /// <summary>
    /// Returns a quick-fix for the given lint issue, or <c>null</c> when none is available.
    /// </summary>
    public static (string Description, Func<string, string> Apply)? GetQuickFix(
        LintIssue issue, string fullSql)
        => GetQuickFix(issue, fullSql, schema: null);

    /// <summary>
    /// Overload that can expand <c>SELECT *</c> when schema metadata is available.
    /// </summary>
    public static (string Description, Func<string, string> Apply)? GetQuickFix(
        LintIssue issue, string fullSql, ISchemaProvider? schema)
    {
        // Prefer SuggestedFix from parser when present (e.g. PAR004 typos).
        if (!string.IsNullOrWhiteSpace(issue.SuggestedFix)
            && issue.StartOffset >= 0
            && issue.EndOffset > issue.StartOffset
            && issue.EndOffset <= fullSql.Length)
        {
            var suggested = issue.SuggestedFix!;
            return (issue.RuleId == "SQL048" ? $"Qualify as {suggested}" : issue.RuleId == "NZL006" ? $"Use {suggested} NULL" : $"Apply suggested fix: {suggested}", sql =>
            {
                if (issue.StartOffset >= sql.Length || issue.EndOffset > sql.Length) return sql;
                return sql[..issue.StartOffset] + suggested + sql[issue.EndOffset..];
            });
        }

        return issue.RuleId switch
        {
            "NZ001" => GetNz001Fix(issue, fullSql, schema),
            "NZ002" or "SQL043" => GetWhereClauseFix(issue, "WHERE"),
            "NZ003" or "SQL044" => GetWhereClauseFix(issue, "WHERE"),
            "NZ004" => GetNz004Fix(issue),
            "NZ006" => GetNz006Fix(issue),
            "NZ007" => GetNz007Fix(issue, fullSql),
            "NZ010" => GetNz010Fix(issue, fullSql),
            "NZ011" or "SQL045" => GetNz011Fix(issue),
            "NZ012" or "SQL046" => GetNz012Fix(issue),
            "NZ013" => GetNz013Fix(issue),
            "NZ021" => GetParse001Fix(issue, fullSql),
            "NZ023" => GetNz021Fix(issue, fullSql),
            "NZ024" => GetNz022Fix(issue, fullSql),
            "NZP012" => GetNzp012Fix(issue),
            "SQL008" or "SQL048" => GetQualifyFix(issue),
            "SQL012" => GetSql012Fix(issue, fullSql),
            "PAR002" => GetPar002Fix(issue),
            "PARSE001" => GetParse001Fix(issue, fullSql),
            "PAR101" => GetPar101Fix(issue),
            "SQL007" => GetSql007Fix(issue),
            _ => null
        };
    }

    /// <summary>
    /// Applies safe Fix-all eligible fixes in reverse offset order in a single pass.
    /// <paramref name="maxPasses"/> is retained for source compatibility; callers
    /// must re-lint before applying another pass because edits can invalidate offsets.
    /// </summary>
    public static string ApplyAllSafeFixes(
        string sql,
        IEnumerable<LintIssue> issues,
        ISchemaProvider? schema = null,
        int maxPasses = 1)
    {
        _ = maxPasses;
        var ordered = issues
            .Where(i => IsSafeForFixAll(i.RuleId))
            .OrderByDescending(i => i.StartOffset)
            .ThenByDescending(i => i.EndOffset)
            .ToList();

        // Issues are applied from the end of the document, so earlier offsets stay
        // valid. An issue that overlaps one already applied, or a second insertion
        // at the same offset, is skipped so the result does not depend on order.
        var current = sql;
        var appliedStart = int.MaxValue;
        var appliedInsertion = false;
        foreach (var issue in ordered)
        {
            var isInsertion = issue.StartOffset == issue.EndOffset;
            if (issue.EndOffset > appliedStart
                || (isInsertion && appliedInsertion && issue.StartOffset == appliedStart))
                continue;
            var fix = GetQuickFix(issue, current, schema);
            if (fix is null) continue;
            current = fix.Value.Apply(current);
            appliedStart = issue.StartOffset;
            appliedInsertion = isInsertion;
        }

        return current;
    }

    private static (string Description, Func<string, string> Apply)? GetNz001Fix(
        LintIssue issue, string fullSql, ISchemaProvider? schema)
    {
        if (schema is null || issue.StartOffset < 0) return null;

        // Heuristic: find FROM table after the star.
        var fromMatch = Regex.Match(
            fullSql[issue.StartOffset..],
            @"\bFROM\s+((?:""[^""]+""|\w+)(?:\.(?:""[^""]+""|\w+)){0,2})",
            RegexOptions.IgnoreCase);
        if (!fromMatch.Success) return null;

        var qualified = fromMatch.Groups[1].Value;
        var parts = qualified.Split('.');
        string? database = null, sch = null, table;
        if (parts.Length == 3) { database = parts[0].Trim('"'); sch = parts[1].Trim('"'); table = parts[2].Trim('"'); }
        else if (parts.Length == 2) { sch = parts[0].Trim('"'); table = parts[1].Trim('"'); }
        else { table = parts[0].Trim('"'); }

        var info = schema.GetTable(database, sch, table);
        if (info?.Columns is not { Count: > 0 }) return null;

        var list = string.Join(", ", info.Columns.Select(c => c.Name));
        return ("Expand SELECT * to columns", sql =>
        {
            if (issue.StartOffset >= sql.Length || issue.EndOffset > sql.Length) return sql;
            if (sql[issue.StartOffset] != '*') return sql;
            return sql[..issue.StartOffset] + list + sql[issue.EndOffset..];
        });
    }

    private static (string Description, Func<string, string> Apply)? GetNz004Fix(LintIssue issue)
    {
        if (issue.StartOffset < 0) return null;
        return ("Replace CROSS JOIN with INNER JOIN ... ON 1=1", sql =>
        {
            if (issue.StartOffset + 10 > sql.Length) return sql;
            var token = sql[issue.StartOffset..(issue.StartOffset + 10)];
            if (!string.Equals(token, "CROSS JOIN", StringComparison.OrdinalIgnoreCase)) return sql;

            var replaced = sql[..issue.StartOffset] + "INNER JOIN" + sql[(issue.StartOffset + 10)..];
            var joinSourceEnd = FindJoinSourceEnd(replaced, issue.StartOffset + "INNER JOIN".Length, out var hasExistingOn);
            return hasExistingOn ? replaced : replaced.Insert(joinSourceEnd, " ON 1=1");
        });
    }

    private static int FindJoinSourceEnd(string sql, int start, out bool hasExistingOn)
    {
        hasExistingOn = false;
        var parenDepth = 0;
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var inLineComment = false;
        var inBlockComment = false;

        for (var i = Math.Clamp(start, 0, sql.Length); i < sql.Length; i++)
        {
            var current = sql[i];
            var next = i + 1 < sql.Length ? sql[i + 1] : '\0';

            if (inSingleQuote)
            {
                if (current == '\'' && next == '\'') { i++; continue; }
                if (current == '\'') inSingleQuote = false;
                continue;
            }
            if (inDoubleQuote)
            {
                if (current == '"' && next == '"') { i++; continue; }
                if (current == '"') inDoubleQuote = false;
                continue;
            }
            if (inLineComment)
            {
                if (current == '\n') inLineComment = false;
                continue;
            }
            if (inBlockComment)
            {
                if (current == '*' && next == '/') { inBlockComment = false; i++; }
                continue;
            }

            if (current == '-' && next == '-') { inLineComment = true; i++; continue; }
            if (current == '/' && next == '*') { inBlockComment = true; i++; continue; }
            if (current == '\'') { inSingleQuote = true; continue; }
            if (current == '"') { inDoubleQuote = true; continue; }
            if (current == '(') { parenDepth++; continue; }
            if (current == ')') { parenDepth = Math.Max(0, parenDepth - 1); continue; }
            if (parenDepth != 0) continue;

            if (current is ';' or ',' || IsClauseBoundary(sql, i))
            {
                hasExistingOn = IsKeywordAt(sql, i, "ON");
                var end = i;
                while (end > start && char.IsWhiteSpace(sql[end - 1])) end--;
                return end;
            }
        }

        var finalEnd = sql.Length;
        while (finalEnd > start && char.IsWhiteSpace(sql[finalEnd - 1])) finalEnd--;
        return finalEnd;
    }

    private static bool IsKeywordAt(string sql, int index, string keyword)
    {
        if (index + keyword.Length > sql.Length
            || !sql.AsSpan(index, keyword.Length).Equals(keyword, StringComparison.OrdinalIgnoreCase))
            return false;
        var after = index + keyword.Length;
        return after == sql.Length || !(char.IsLetterOrDigit(sql[after]) || sql[after] == '_');
    }

    private static bool IsClauseBoundary(string sql, int index)
    {
        if (!(char.IsLetter(sql[index]) || sql[index] == '_')) return false;
        if (index > 0 && (char.IsLetterOrDigit(sql[index - 1]) || sql[index - 1] == '_')) return false;

        foreach (var keyword in new[]
                 {
                     "JOIN", "ON", "WHERE", "GROUP", "ORDER", "HAVING", "LIMIT", "FETCH",
                     "DISTRIBUTE", "ORGANIZE", "UNION", "EXCEPT", "INTERSECT", "QUALIFY"
                 })
        {
            if (index + keyword.Length > sql.Length) continue;
            if (!sql.AsSpan(index, keyword.Length).Equals(keyword, StringComparison.OrdinalIgnoreCase)) continue;
            var after = index + keyword.Length;
            if (after == sql.Length || !(char.IsLetterOrDigit(sql[after]) || sql[after] == '_')) return true;
        }
        return false;
    }

    private static (string Description, Func<string, string> Apply)? GetNz006Fix(LintIssue issue)
    {
        if (issue.StartOffset < 0) return null;
        return ("Add FETCH FIRST 100 ROWS ONLY", sql =>
        {
            var semi = sql.IndexOf(';', Math.Max(0, issue.StartOffset));
            var insertAt = semi >= 0 ? semi : sql.Length;
            if (sql.Contains("FETCH FIRST", StringComparison.OrdinalIgnoreCase)
                || sql.Contains("LIMIT ", StringComparison.OrdinalIgnoreCase))
                return sql;
            return sql.Insert(insertAt, " FETCH FIRST 100 ROWS ONLY");
        });
    }

    private static (string Description, Func<string, string> Apply)? GetNz007Fix(
        LintIssue issue, string fullSql)
    {
        if (issue.StartOffset < 0
            || issue.EndOffset > fullSql.Length
            || issue.StartOffset >= issue.EndOffset)
            return null;

        var originalWord = fullSql[issue.StartOffset..issue.EndOffset];
        bool toUpper = !issue.Message.Contains("lowercase", StringComparison.OrdinalIgnoreCase)
                    || issue.Message.Contains("UPPERCASE", StringComparison.OrdinalIgnoreCase);
        var targetWord = toUpper ? originalWord.ToUpperInvariant() : originalWord.ToLowerInvariant();
        if (originalWord == targetWord) return null;

        return ($"Make '{originalWord}' → '{targetWord}'", sql =>
        {
            if (issue.StartOffset >= sql.Length || issue.EndOffset > sql.Length) return sql;
            var current = sql[issue.StartOffset..issue.EndOffset];
            if (!string.Equals(current, originalWord, StringComparison.OrdinalIgnoreCase))
                return sql;
            return sql[..issue.StartOffset] + targetWord + sql[issue.EndOffset..];
        });
    }

    private static (string Description, Func<string, string> Apply)? GetNz010Fix(
        LintIssue issue, string fullSql)
    {
        if (issue.StartOffset < 0 || issue.EndOffset > fullSql.Length || issue.EndOffset <= issue.StartOffset)
            return null;

        var statementRange = FindStatementRange(fullSql, issue.StartOffset);
        if (statementRange.End <= statementRange.Start) return null;
        var statement = fullSql[statementRange.Start..statementRange.End];
        var statementIssueStart = issue.StartOffset - statementRange.Start;
        var issueEnd = Math.Min(issue.EndOffset, statementRange.End) - statementRange.Start;
        if (statementIssueStart < 0 || issueEnd > statement.Length || issueEnd <= statementIssueStart) return null;
        var region = statement[statementIssueStart..issueEnd];
        var tableMatch = Regex.Match(issue.Message, "'([^']+)'");
        if (!tableMatch.Success)
            return null;

        var tableName = tableMatch.Groups[1].Value;
        var tableIndex = region.IndexOf(tableName, StringComparison.OrdinalIgnoreCase);
        if (tableIndex < 0)
            return null;

        var alias = FindUnusedAlias(fullSql);
        return ($"Add alias {alias}", sql =>
        {
            if (statementRange.End > sql.Length) return sql;
            var statementText = sql[statementRange.Start..statementRange.End];
            var insertAt = statementIssueStart + tableIndex + tableName.Length;
            var withAlias = statementText[..insertAt] + " " + alias + statementText[insertAt..];
            var rewritten = QualifyTableReferences(
                withAlias,
                tableName,
                alias,
                declarationNameOffset: insertAt,
                declarationNameLength: tableName.Length);
            return sql[..statementRange.Start] + rewritten + sql[statementRange.End..];
        });
    }

    private static (int Start, int End) FindStatementRange(string sql, int offset)
    {
        var start = 0;
        var inSingle = false;
        var inDouble = false;
        var inLineComment = false;
        var inBlockComment = false;
        var depth = 0;
        for (var i = 0; i < sql.Length; i++)
        {
            var c = sql[i];
            var next = i + 1 < sql.Length ? sql[i + 1] : '\0';
            if (inSingle)
            {
                if (c == '\'' && next == '\'') { i++; continue; }
                if (c == '\'') inSingle = false;
                continue;
            }
            if (inDouble)
            {
                if (c == '"' && next == '"') { i++; continue; }
                if (c == '"') inDouble = false;
                continue;
            }
            if (inLineComment)
            {
                if (c == '\n') inLineComment = false;
                continue;
            }
            if (inBlockComment)
            {
                if (c == '*' && next == '/') { inBlockComment = false; i++; }
                continue;
            }
            if (c == '-' && next == '-') { inLineComment = true; i++; continue; }
            if (c == '/' && next == '*') { inBlockComment = true; i++; continue; }
            if (c == '\'') { inSingle = true; continue; }
            if (c == '"') { inDouble = true; continue; }
            if (c == '(') { depth++; continue; }
            if (c == ')') { depth = Math.Max(0, depth - 1); continue; }
            if (c == ';' && depth == 0)
            {
                if (offset <= i) return (start, i);
                start = i + 1;
            }
        }
        return offset >= start && offset <= sql.Length ? (start, sql.Length) : (0, sql.Length);
    }

    internal static string FindUnusedAlias(string sql)
    {
        for (var i = 1; ; i++)
        {
            var candidate = "t" + i;
            if (!Regex.IsMatch(sql, $@"\b{Regex.Escape(candidate)}\b", RegexOptions.IgnoreCase))
                return candidate;
        }
    }

    internal static string QualifyTableReferences(
        string sql, string tableName, string alias, int declarationNameOffset = -1, int declarationNameLength = 0)
    {
        var basename = tableName.Split('.').Last().Trim('"', '`', '[', ']');
        foreach (var qualifier in new[] { tableName, basename }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var pattern = $@"(?<![\w.""\]]){Regex.Escape(qualifier)}\s*\.";
            sql = Regex.Replace(sql, pattern, match =>
            {
                var inDeclaration = declarationNameOffset >= 0
                    && match.Index >= declarationNameOffset
                    && match.Index < declarationNameOffset + declarationNameLength;
                return inDeclaration || LintHelpers.IsInsideStringOrComment(sql, match.Index)
                    ? match.Value
                    : alias + ".";
            }, RegexOptions.IgnoreCase);
        }
        return sql;
    }

    private static (string Description, Func<string, string> Apply)? GetNz011Fix(LintIssue issue)
    {
        if (issue.StartOffset < 0) return null;

        return ("Add DISTRIBUTE ON RANDOM", sql =>
        {
            if (issue.StartOffset >= sql.Length) return sql;

            int stmtEnd = sql.Length;
            for (int i = issue.StartOffset; i < sql.Length; i++)
            {
                if (sql[i] == ';' && !LintHelpers.IsInsideStringOrComment(sql, i))
                {
                    stmtEnd = i;
                    break;
                }
            }

            int insertAt = stmtEnd;
            while (insertAt > issue.StartOffset && char.IsWhiteSpace(sql[insertAt - 1]))
                insertAt--;

            return sql[..insertAt] + " DISTRIBUTE ON RANDOM" + sql[insertAt..];
        });
    }

    private static (string Description, Func<string, string> Apply)? GetNz012Fix(LintIssue issue)
    {
        if (issue.StartOffset < 0) return null;

        return ("Remove AS keyword", sql =>
        {
            if (issue.StartOffset + 2 > sql.Length) return sql;
            var token = sql[issue.StartOffset..(issue.StartOffset + 2)];
            if (!string.Equals(token, "AS", StringComparison.OrdinalIgnoreCase)) return sql;

            int end = issue.StartOffset + 2;
            while (end < sql.Length && sql[end] == ' ')
                end++;

            return sql[..issue.StartOffset] + sql[end..];
        });
    }

    private static (string Description, Func<string, string> Apply)? GetNz013Fix(LintIssue issue)
    {
        if (issue.StartOffset < 0) return null;

        return ("Replace UNION with UNION ALL", sql =>
        {
            if (issue.StartOffset + 5 > sql.Length) return sql;
            var token = sql[issue.StartOffset..(issue.StartOffset + 5)];
            if (!string.Equals(token, "UNION", StringComparison.OrdinalIgnoreCase)) return sql;

            int after = issue.StartOffset + 5;
            int check = after;
            while (check < sql.Length && sql[check] == ' ') check++;
            if (check + 3 <= sql.Length
                && string.Equals(sql[check..(check + 3)], "ALL", StringComparison.OrdinalIgnoreCase))
                return sql;

            return sql[..issue.StartOffset] + token + " ALL" + sql[after..];
        });
    }

    private static (string Description, Func<string, string> Apply)? GetNzp012Fix(LintIssue issue)
    {
        if (issue.StartOffset < 0) return null;
        return ("Replace ELSEIF with ELSIF", sql =>
        {
            if (issue.StartOffset + 6 > sql.Length) return sql;
            var token = sql[issue.StartOffset..(issue.StartOffset + 6)];
            if (!string.Equals(token, "ELSEIF", StringComparison.OrdinalIgnoreCase)) return sql;
            return sql[..issue.StartOffset] + "ELSIF" + sql[(issue.StartOffset + 6)..];
        });
    }

    private static (string Description, Func<string, string> Apply)? GetQualifyFix(LintIssue issue)
    {
        if (string.IsNullOrWhiteSpace(issue.SuggestedFix)) return null;
        var suggested = issue.SuggestedFix!;
        return ($"Qualify as {suggested}", sql =>
        {
            if (issue.StartOffset < 0 || issue.EndOffset > sql.Length) return sql;
            return sql[..issue.StartOffset] + suggested + sql[issue.EndOffset..];
        });
    }

    private static (string Description, Func<string, string> Apply)? GetSql012Fix(
        LintIssue issue, string fullSql)
    {
        if (issue.StartOffset < 0 || issue.StartOffset >= fullSql.Length) return null;

        var statementRange = FindStatementRange(fullSql, issue.StartOffset);
        var statement = fullSql[statementRange.Start..statementRange.End];
        var candidates = Regex.Matches(statement, @"\bVARCHAR\b(?!\s*\()", RegexOptions.IgnoreCase)
            .Cast<Match>()
            .Where(match => !LintHelpers.IsInsideStringOrComment(fullSql, statementRange.Start + match.Index))
            .OrderBy(match => Math.Abs(statementRange.Start + match.Index - issue.StartOffset))
            .ToArray();
        if (candidates.Length == 0) return null;

        var startOffset = statementRange.Start + candidates[0].Index;
        var endOffset = startOffset + candidates[0].Length;

        return ("Use VARCHAR(100)", sql =>
        {
            if (endOffset > sql.Length || LintHelpers.IsInsideStringOrComment(sql, startOffset)) return sql;
            if (!sql.AsSpan(startOffset, endOffset - startOffset).Equals("VARCHAR", StringComparison.OrdinalIgnoreCase)) return sql;
            return sql[..startOffset] + "VARCHAR(100)" + sql[endOffset..];
        });
    }

    private static (string Description, Func<string, string> Apply)? GetPar101Fix(LintIssue issue)
    {
        if (issue.StartOffset < 0) return null;

        return ("Insert AS before subquery", sql =>
        {
            if (issue.StartOffset > sql.Length) return sql;
            return sql[..issue.StartOffset] + "AS " + sql[issue.StartOffset..];
        });
    }

    private static (string Description, Func<string, string> Apply)? GetSql007Fix(LintIssue issue)
    {
        if (issue.StartOffset < 0) return null;

        return ("Add second dot (DB..TABLE)", sql =>
        {
            if (issue.StartOffset >= sql.Length || issue.EndOffset > sql.Length) return sql;
            var segment = sql[issue.StartOffset..issue.EndOffset];
            int dotIdx = segment.IndexOf('.');
            if (dotIdx < 0) return sql;
            if (dotIdx + 1 < segment.Length && segment[dotIdx + 1] == '.') return sql;

            var fixedSegment = segment[..(dotIdx + 1)] + "." + segment[(dotIdx + 1)..];
            return sql[..issue.StartOffset] + fixedSegment + sql[issue.EndOffset..];
        });
    }

    private static (string Description, Func<string, string> Apply)? GetNz021Fix(
        LintIssue issue, string fullSql)
    {
        if (issue.StartOffset < 0 || issue.EndOffset > fullSql.Length) return null;

        var typo = fullSql[issue.StartOffset..issue.EndOffset];
        var correction = typo.ToUpperInvariant() switch
        {
            "SELEC" => "SELECT", "SELCT" => "SELECT", "SELCET" => "SELECT",
            "FORM" => "FROM", "FROME" => "FROM",
            "WEHERE" => "WHERE", "WEHRE" => "WHERE", "WEAR" => "WHERE",
            "INSET" => "INSERT", "INSTERT" => "INSERT",
            "UPDAT" => "UPDATE", "UPDTE" => "UPDATE",
            "DELET" => "DELETE", "DEELTE" => "DELETE",
            "GROP" => "GROUP", "GROPU" => "GROUP",
            "HAVIGN" => "HAVING", "HAVNG" => "HAVING",
            "ORDET" => "ORDER", "ODER" => "ORDER",
            "LMIT" => "LIMIT", "LIMT" => "LIMIT",
            "DISTINT" => "DISTINCT", "DISTNCT" => "DISTINCT",
            "BEWTEEN" => "BETWEEN", "BEETWEEN" => "BETWEEN", "BETWEE" => "BETWEEN",
            _ => null
        };

        if (correction is null) return null;

        return ($"Fix typo: {typo} → {correction}", sql =>
        {
            if (issue.StartOffset >= sql.Length || issue.EndOffset > sql.Length) return sql;
            var current = sql[issue.StartOffset..issue.EndOffset];
            if (!string.Equals(current, typo, StringComparison.OrdinalIgnoreCase)) return sql;
            return sql[..issue.StartOffset] + correction + sql[issue.EndOffset..];
        });
    }

    private static (string Description, Func<string, string> Apply)? GetNz022Fix(
        LintIssue issue, string fullSql)
    {
        if (issue.StartOffset < 0 || issue.StartOffset >= fullSql.Length) return null;

        return ("Remove trailing comma", sql =>
        {
            if (issue.StartOffset >= sql.Length) return sql;
            if (sql[issue.StartOffset] != ',') return sql;
            return sql[..issue.StartOffset] + sql[(issue.StartOffset + 1)..];
        });
    }

    private static (string Description, Func<string, string> Apply)? GetParse001Fix(
        LintIssue issue, string fullSql)
    {
        if (issue.StartOffset < 0 || issue.StartOffset >= fullSql.Length) return null;

        var tokenChar = fullSql[issue.StartOffset];
        if (tokenChar != ',') return null;

        return ("Remove unexpected comma", sql =>
        {
            if (issue.StartOffset >= sql.Length) return sql;
            if (sql[issue.StartOffset] != ',') return sql;
            return sql[..issue.StartOffset] + sql[(issue.StartOffset + 1)..];
        });
    }

    private static (string Description, Func<string, string> Apply)? GetWhereClauseFix(
        LintIssue issue, string clause)
    {
        if (issue.StartOffset < 0) return null;
        return ($"Add {clause} 1=0 guard", sql =>
        {
            var semi = sql.IndexOf(';', issue.StartOffset);
            var insertAt = semi >= 0 ? semi : sql.Length;
            return sql.Insert(insertAt, $" {clause} 1 = 0");
        });
    }

    private static (string Description, Func<string, string> Apply)? GetPar002Fix(LintIssue issue)
    {
        if (issue.StartOffset < 0) return null;
        return ("Remove duplicate comma", sql =>
        {
            if (issue.StartOffset >= sql.Length) return sql;
            if (issue.StartOffset > 0 && sql[issue.StartOffset] == ',' && sql[issue.StartOffset - 1] == ',')
                return sql[..issue.StartOffset] + sql[(issue.StartOffset + 1)..];
            if (issue.StartOffset + 1 < sql.Length && sql[issue.StartOffset] == ',' && sql[issue.StartOffset + 1] == ',')
                return sql[..(issue.StartOffset + 1)] + sql[(issue.StartOffset + 2)..];
            return sql;
        });
    }
}
