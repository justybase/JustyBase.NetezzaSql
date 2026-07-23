using JustyBase.NetezzaSqlParser.Linter;

namespace JustyBase.NetezzaSqlParser.Authoring;

/// <summary>
/// Provides one-click quick fixes for SQL lint issues detected by the Netezza linter.
/// Each fix is a pure string transformation — no UI or editor dependencies.
/// </summary>
public static class NzLintCodeActions
{
    /// <summary>
    /// Returns a quick-fix (description + applicator) for the given lint issue, or <c>null</c> when
    /// no automated fix is available for the rule.
    /// </summary>
    /// <param name="issue">The lint issue to fix.</param>
    /// <param name="fullSql">
    /// The SQL string at the time the issue was detected — used only for context when building
    /// the fix description. The returned <c>Apply</c> delegate accepts the <em>live</em> SQL
    /// (potentially edited since the lint ran) and returns the corrected string.
    /// </param>
    public static (string Description, Func<string, string> Apply)? GetQuickFix(
        LintIssue issue, string fullSql)
    {
        return issue.RuleId switch
        {
            "NZ007" => GetNz007Fix(issue, fullSql),
            "NZ011" => GetNz011Fix(issue),
            "NZ012" => GetNz012Fix(issue),
            "NZ013" => GetNz013Fix(issue),
            "NZ021" => GetParse001Fix(issue, fullSql),
            "NZ023" => GetNz021Fix(issue, fullSql),
            "NZ024" => GetNz022Fix(issue, fullSql),
            "SQL043" => GetWhereClauseFix(issue, fullSql, "WHERE"),
            "SQL044" => GetWhereClauseFix(issue, fullSql, "WHERE"),
            "PAR002" => GetPar002Fix(issue, fullSql),
            "PARSE001" => GetParse001Fix(issue, fullSql),
            "PAR101" => GetPar101Fix(issue),
            "SQL007" => GetSql007Fix(issue),
            _ => null
        };
    }

    // -----------------------------------------------------------------
    // NZ007 — Inconsistent keyword casing  →  normalise to UPPERCASE
    // The issue range [StartOffset, EndOffset) covers exactly the keyword.
    // -----------------------------------------------------------------
    private static (string Description, Func<string, string> Apply)? GetNz007Fix(
        LintIssue issue, string fullSql)
    {
        if (issue.StartOffset < 0
            || issue.EndOffset > fullSql.Length
            || issue.StartOffset >= issue.EndOffset)
            return null;

        var originalWord = fullSql[issue.StartOffset..issue.EndOffset];

        // Derive the target case from the diagnostic message text; default to UPPERCASE.
        bool toUpper = !issue.Message.Contains("lowercase", StringComparison.OrdinalIgnoreCase)
                    || issue.Message.Contains("UPPERCASE", StringComparison.OrdinalIgnoreCase);

        var targetWord = toUpper ? originalWord.ToUpperInvariant() : originalWord.ToLowerInvariant();
        if (originalWord == targetWord) return null;

        return ($"Make '{originalWord}' → '{targetWord}'", sql =>
        {
            if (issue.StartOffset >= sql.Length || issue.EndOffset > sql.Length) return sql;
            var current = sql[issue.StartOffset..issue.EndOffset];
            // Only apply when the word at this position still matches (case-insensitive).
            if (!string.Equals(current, originalWord, StringComparison.OrdinalIgnoreCase))
                return sql;
            return sql[..issue.StartOffset] + targetWord + sql[issue.EndOffset..];
        }
        );
    }

    // -----------------------------------------------------------------
    // NZ011 — CTAS missing DISTRIBUTE ON  →  append DISTRIBUTE ON RANDOM
    // Insert before the trailing semicolon (or at end of statement).
    // -----------------------------------------------------------------
    private static (string Description, Func<string, string> Apply)? GetNz011Fix(LintIssue issue)
    {
        if (issue.StartOffset < 0) return null;

        return ("Add DISTRIBUTE ON RANDOM", sql =>
        {
            if (issue.StartOffset >= sql.Length) return sql;

            // Find the semicolon that ends this CTAS statement.
            int stmtEnd = sql.Length;
            for (int i = issue.StartOffset; i < sql.Length; i++)
            {
                if (sql[i] == ';' && !LintHelpers.IsInsideStringOrComment(sql, i))
                {
                    stmtEnd = i;
                    break;
                }
            }

            // Walk back past trailing whitespace to find a clean insert point.
            int insertAt = stmtEnd;
            while (insertAt > issue.StartOffset && char.IsWhiteSpace(sql[insertAt - 1]))
                insertAt--;

            return sql[..insertAt] + "\nDISTRIBUTE ON RANDOM" + sql[insertAt..];
        }
        );
    }

    // -----------------------------------------------------------------
    // NZ012 — UPDATE ... AS alias  →  remove the AS keyword
    // The issue range starts at "AS".
    // -----------------------------------------------------------------
    private static (string Description, Func<string, string> Apply)? GetNz012Fix(LintIssue issue)
    {
        if (issue.StartOffset < 0) return null;

        return ("Remove AS keyword", sql =>
        {
            if (issue.StartOffset + 2 > sql.Length) return sql;
            var token = sql[issue.StartOffset..(issue.StartOffset + 2)];
            if (!string.Equals(token, "AS", StringComparison.OrdinalIgnoreCase)) return sql;

            // Remove "AS" plus any immediately following spaces.
            int end = issue.StartOffset + 2;
            while (end < sql.Length && sql[end] == ' ')
                end++;

            return sql[..issue.StartOffset] + sql[end..];
        }
        );
    }

    // -----------------------------------------------------------------
    // NZ013 — UNION (without ALL)  →  UNION ALL
    // The issue range covers "UNION" (5 chars).
    // -----------------------------------------------------------------
    private static (string Description, Func<string, string> Apply)? GetNz013Fix(LintIssue issue)
    {
        if (issue.StartOffset < 0) return null;

        return ("Replace UNION with UNION ALL", sql =>
        {
            if (issue.StartOffset + 5 > sql.Length) return sql;
            var token = sql[issue.StartOffset..(issue.StartOffset + 5)];
            if (!string.Equals(token, "UNION", StringComparison.OrdinalIgnoreCase)) return sql;

            // Safety: already UNION ALL?
            int after = issue.StartOffset + 5;
            int check = after;
            while (check < sql.Length && sql[check] == ' ') check++;
            if (check + 3 <= sql.Length
                && string.Equals(sql[check..(check + 3)], "ALL", StringComparison.OrdinalIgnoreCase))
                return sql;

            // Preserve the original casing of "UNION" and add " ALL".
            return sql[..issue.StartOffset] + token + " ALL" + sql[after..];
        }
        );
    }

    // -----------------------------------------------------------------
    // PAR101 — CTE missing AS  →  insert "AS " before the subquery opener
    // The issue position points to the token immediately after the CTE name
    // (typically the opening parenthesis of the subquery body).
    // -----------------------------------------------------------------
    private static (string Description, Func<string, string> Apply)? GetPar101Fix(LintIssue issue)
    {
        if (issue.StartOffset < 0) return null;

        return ("Insert AS before subquery", sql =>
        {
            if (issue.StartOffset > sql.Length) return sql;
            return sql[..issue.StartOffset] + "AS " + sql[issue.StartOffset..];
        }
        );
    }

    // -----------------------------------------------------------------
    // SQL007 — DB.TABLE (invalid single-dot form)  →  DB..TABLE
    // The issue range [StartOffset, EndOffset) covers the qualified name.
    // -----------------------------------------------------------------
    private static (string Description, Func<string, string> Apply)? GetSql007Fix(LintIssue issue)
    {
        if (issue.StartOffset < 0) return null;

        return ("Add second dot (DB..TABLE)", sql =>
        {
            if (issue.StartOffset >= sql.Length || issue.EndOffset > sql.Length) return sql;
            var segment = sql[issue.StartOffset..issue.EndOffset];
            int dotIdx = segment.IndexOf('.');
            if (dotIdx < 0) return sql;

            // Already double-dot?
            if (dotIdx + 1 < segment.Length && segment[dotIdx + 1] == '.') return sql;

            var fixedSegment = segment[..(dotIdx + 1)] + "." + segment[(dotIdx + 1)..];
            return sql[..issue.StartOffset] + fixedSegment + sql[issue.EndOffset..];
        }
        );
    }

    // -----------------------------------------------------------------
    // NZ021 — Keyword typo  →  correct keyword
    // The issue range [StartOffset, EndOffset) covers the typo word.
    // -----------------------------------------------------------------
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
        }
        );
    }

    // -----------------------------------------------------------------
    // NZ022 — Trailing comma before FROM/semicolon  →  remove comma
    // The issue range covers the comma character.
    // -----------------------------------------------------------------
    private static (string Description, Func<string, string> Apply)? GetNz022Fix(
        LintIssue issue, string fullSql)
    {
        if (issue.StartOffset < 0 || issue.StartOffset >= fullSql.Length) return null;

        return ("Remove trailing comma", sql =>
        {
            if (issue.StartOffset >= sql.Length) return sql;
            if (sql[issue.StartOffset] != ',') return sql;
            return sql[..issue.StartOffset] + sql[(issue.StartOffset + 1)..];
        }
        );
    }

    // -----------------------------------------------------------------
    // PARSE001 — Unexpected token in expression
    // If the unexpected token is a comma, remove it (duplicate comma fix).
    // -----------------------------------------------------------------
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
        }
        );
    }

    private static (string Description, Func<string, string> Apply)? GetWhereClauseFix(
        LintIssue issue, string fullSql, string clause)
    {
        if (issue.StartOffset < 0) return null;
        return ($"Add {clause} 1=0 guard", sql =>
        {
            var semi = sql.IndexOf(';', issue.StartOffset);
            var insertAt = semi >= 0 ? semi : sql.Length;
            return sql.Insert(insertAt, $" {clause} 1=0");
        });
    }

    private static (string Description, Func<string, string> Apply)? GetPar002Fix(
        LintIssue issue, string fullSql)
    {
        if (issue.StartOffset < 0 || issue.StartOffset >= fullSql.Length) return null;
        return ("Remove duplicate comma", sql =>
        {
            if (issue.StartOffset + 1 >= sql.Length) return sql;
            if (sql[issue.StartOffset] == ',' && sql[issue.StartOffset + 1] == ',')
                return sql[..(issue.StartOffset + 1)] + sql[(issue.StartOffset + 2)..];
            return sql;
        });
    }
}
