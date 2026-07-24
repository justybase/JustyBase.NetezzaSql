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
    private static readonly HashSet<string> SafeFixAllCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "SQL007", "SQL012", "NZ007", "NZ012", "SQL046", "NZP012",
        "NZ021", "PAR002", "PARSE001", "NZ024", "PAR101"
    };

    /// <summary>Returns whether the rule is eligible for automatic Fix-all (safe, deterministic).</summary>
    public static bool IsSafeForFixAll(string ruleId)
        => !string.IsNullOrWhiteSpace(ruleId) && SafeFixAllCodes.Contains(ruleId);

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
            return ($"Apply suggested fix: {suggested}", sql =>
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
    /// Applies all safe Fix-all eligible fixes in reverse offset order (stable single pass).
    /// Re-lint and call again if additional passes are needed after structural changes.
    /// </summary>
    public static string ApplyAllSafeFixes(
        string sql,
        IEnumerable<LintIssue> issues,
        ISchemaProvider? schema = null,
        int maxPasses = 1)
    {
        var current = sql;
        var ordered = issues
            .Where(i => IsSafeForFixAll(i.RuleId))
            .OrderByDescending(i => i.StartOffset)
            .ThenByDescending(i => i.EndOffset)
            .ToList();

        for (var pass = 0; pass < Math.Max(1, maxPasses); pass++)
        {
            var before = current;
            foreach (var issue in ordered)
            {
                var fix = GetQuickFix(issue, current, schema);
                if (fix is null) continue;
                current = fix.Value.Apply(current);
            }

            if (current == before)
                break;
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
        return ("Replace CROSS JOIN with INNER JOIN", sql =>
        {
            if (issue.StartOffset + 10 > sql.Length) return sql;
            var token = sql[issue.StartOffset..(issue.StartOffset + 10)];
            if (!string.Equals(token, "CROSS JOIN", StringComparison.OrdinalIgnoreCase)) return sql;
            return sql[..issue.StartOffset] + "INNER JOIN" + sql[(issue.StartOffset + 10)..];
        });
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
        if (issue.StartOffset < 0 || issue.EndOffset > fullSql.Length) return null;
        var tableToken = fullSql[issue.StartOffset..issue.EndOffset].Trim();
        if (string.IsNullOrEmpty(tableToken)) return null;
        var alias = "t1";
        return ($"Add alias {alias}", sql =>
        {
            if (issue.EndOffset > sql.Length) return sql;
            return sql[..issue.EndOffset] + " " + alias + sql[issue.EndOffset..];
        });
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

            return sql[..insertAt] + "\nDISTRIBUTE ON RANDOM" + sql[insertAt..];
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
        if (issue.StartOffset < 0 || issue.EndOffset > fullSql.Length) return null;
        return ("Use VARCHAR(100)", sql =>
        {
            if (issue.StartOffset >= sql.Length || issue.EndOffset > sql.Length) return sql;
            var segment = sql[issue.StartOffset..issue.EndOffset];
            if (!segment.Contains("VARCHAR", StringComparison.OrdinalIgnoreCase)) return sql;
            return sql[..issue.StartOffset] + "VARCHAR(100)" + sql[issue.EndOffset..];
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
            return sql.Insert(insertAt, $" {clause} 1=0");
        });
    }

    private static (string Description, Func<string, string> Apply)? GetPar002Fix(LintIssue issue)
    {
        if (issue.StartOffset < 0) return null;
        return ("Remove duplicate comma", sql =>
        {
            if (issue.StartOffset + 1 >= sql.Length) return sql;
            if (sql[issue.StartOffset] == ',' && sql[issue.StartOffset + 1] == ',')
                return sql[..(issue.StartOffset + 1)] + sql[(issue.StartOffset + 2)..];
            return sql;
        });
    }
}
