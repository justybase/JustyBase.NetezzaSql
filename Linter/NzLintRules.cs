using System.Text.RegularExpressions;

namespace JustyBase.NetezzaSqlParser.Linter;

// ====== NZ001: SELECT * ======
public class RuleNZ001_SelectStar : LintRule
{
    public override string Id => "NZ001";
    public override string Name => "Select Star";
    public override string Description => "Avoid using SELECT * - specify explicit column names";
    public override LintSeverity DefaultSeverity => LintSeverity.Warning;
    public override RuleCost Cost => RuleCost.Cheap;
    public override int Priority => 80; // Common code smell, explicit columns are best practice

    public override IEnumerable<LintIssue> Check(string sql)
    {
        var matches = Regex.Matches(sql, @"\bSELECT\s+\*", RegexOptions.IgnoreCase);
        foreach (Match m in matches)
        {
            if (LintHelpers.IsInsideStringOrComment(sql, m.Index)) continue;
            var starPos = m.Value.IndexOf('*');
            yield return new LintIssue(Id, $"{Id}: {Description}", DefaultSeverity,
                m.Index + starPos, m.Index + starPos + 1);
        }
    }
}

// ====== NZ002: DELETE without WHERE ======
public class RuleNZ002_DeleteWithoutWhere : LintRule
{
    public override string Id => "NZ002";
    public override string Name => "Delete Without Where";
    public override string Description => "DELETE statement without WHERE clause will delete all rows";
    public override LintSeverity DefaultSeverity => LintSeverity.Error;
    public override RuleCost Cost => RuleCost.Cheap;
    public override int Priority => 100; // Critical — data loss prevention

    public override IEnumerable<LintIssue> Check(string sql)
    {
        foreach (Match m in Regex.Matches(sql, @"\bDELETE\s+FROM\s+[\w.""\[\]]+", RegexOptions.IgnoreCase))
        {
            if (LintHelpers.IsInsideStringOrComment(sql, m.Index)) continue;
            var after = sql[(m.Index + m.Length)..];
            var semi = after.IndexOf(';');
            var check = semi >= 0 ? after[..semi] : after;
            if (!Regex.IsMatch(check, @"\bWHERE\b", RegexOptions.IgnoreCase))
                yield return new LintIssue(Id, $"{Id}: {Description}", DefaultSeverity, m.Index, m.Index + 6);
        }
    }
}

// ====== NZ003: UPDATE without WHERE ======
public class RuleNZ003_UpdateWithoutWhere : LintRule
{
    public override string Id => "NZ003";
    public override string Name => "Update Without Where";
    public override string Description => "UPDATE statement without WHERE clause will update all rows";
    public override LintSeverity DefaultSeverity => LintSeverity.Error;
    public override RuleCost Cost => RuleCost.Cheap;
    public override int Priority => 100; // Critical — data loss prevention

    public override IEnumerable<LintIssue> Check(string sql)
    {
        foreach (Match m in Regex.Matches(sql, @"\bUPDATE\s+[\w.""\[\]]+\s+SET\b", RegexOptions.IgnoreCase))
        {
            if (LintHelpers.IsInsideStringOrComment(sql, m.Index)) continue;
            var after = sql[(m.Index + m.Length)..];
            var semi = after.IndexOf(';');
            var check = semi >= 0 ? after[..semi] : after;
            if (!Regex.IsMatch(check, @"\bWHERE\b", RegexOptions.IgnoreCase))
                yield return new LintIssue(Id, $"{Id}: {Description}", DefaultSeverity, m.Index, m.Index + 6);
        }
    }
}

// ====== NZ004: CROSS JOIN ======
public class RuleNZ004_CrossJoin : LintRule
{
    public override string Id => "NZ004";
    public override string Name => "Cross Join";
    public override string Description => "CROSS JOIN produces a Cartesian product - verify intentional";
    public override LintSeverity DefaultSeverity => LintSeverity.Warning;
    public override RuleCost Cost => RuleCost.Cheap;
    public override int Priority => 80; // Performance — Cartesian product risk

    public override IEnumerable<LintIssue> Check(string sql)
    {
        foreach (Match m in Regex.Matches(sql, @"\bCROSS\s+JOIN\b", RegexOptions.IgnoreCase))
        {
            if (LintHelpers.IsInsideStringOrComment(sql, m.Index)) continue;
            yield return new LintIssue(Id, $"{Id}: {Description}", DefaultSeverity, m.Index, m.Index + m.Length);
        }
    }
}

// ====== NZ005: Leading wildcard LIKE ======
public class RuleNZ005_LeadingWildcardLike : LintRule
{
    public override string Id => "NZ005";
    public override string Name => "Leading Wildcard Like";
    public override string Description => "LIKE pattern with leading wildcard prevents Zone Map pruning";
    public override LintSeverity DefaultSeverity => LintSeverity.Hint;
    public override RuleCost Cost => RuleCost.Cheap;
    public override int Priority => 60; // Performance — Zone Map pruning, elevated from Hint default 30

    public override IEnumerable<LintIssue> Check(string sql)
    {
        foreach (Match m in Regex.Matches(sql, @"\bLIKE\s+'%", RegexOptions.IgnoreCase))
        {
            if (LintHelpers.IsInsideStringOrComment(sql, m.Index)) continue;
            yield return new LintIssue(Id, $"{Id}: {Description}", DefaultSeverity, m.Index, m.Index + m.Length);
        }
    }
}

// ====== NZ006: ORDER BY without LIMIT ======
public class RuleNZ006_OrderByWithoutLimit : LintRule
{
    public override string Id => "NZ006";
    public override string Name => "Order By Without Limit";
    public override string Description => "ORDER BY without LIMIT/FETCH may cause performance issues";
    public override LintSeverity DefaultSeverity => LintSeverity.Information;
    public override RuleCost Cost => RuleCost.Cheap;
    public override int Priority => 60; // Performance — elevated from Info default 50

    public override IEnumerable<LintIssue> Check(string sql)
    {
        foreach (Match m in Regex.Matches(sql, @"\bORDER\s+BY\b", RegexOptions.IgnoreCase))
        {
            if (LintHelpers.IsInsideStringOrComment(sql, m.Index)) continue;
            if (!LintHelpers.IsTopLevelSqlPosition(sql, m.Index)) continue;
            var after = sql[(m.Index + m.Length)..];
            var semi = after.IndexOf(';');
            var check = semi >= 0 ? after[..semi] : after;
            if (!Regex.IsMatch(check, @"\b(LIMIT|FETCH|TOP)\b", RegexOptions.IgnoreCase))
            {
                yield return new LintIssue(Id, $"{Id}: {Description}", DefaultSeverity, m.Index, m.Index + m.Length);
            }
        }
    }
}

// ====== NZ007: Inconsistent keyword casing ======
public class RuleNZ007_InconsistentCase : LintRule
{
    public override string Id => "NZ007";
    public override string Name => "Inconsistent Keyword Case";
    public override string Description => "SQL keywords have inconsistent casing";
    public override LintSeverity DefaultSeverity => LintSeverity.Warning;

    private static readonly string[] Keywords = {
        "SELECT","FROM","WHERE","JOIN","LEFT","RIGHT","INNER","OUTER",
        "ON","AND","OR","INSERT","INTO","UPDATE","DELETE","CREATE","DROP","ALTER",
        "TABLE","VIEW","ORDER","BY","GROUP","HAVING","UNION","ALL","DISTINCT",
        "AS","SET","VALUES","NULL","NOT","IN","BETWEEN","LIKE","IS","EXISTS",
        "CASE","WHEN","THEN","ELSE","END","LIMIT","OFFSET"
    };

    public override RuleCost Cost => RuleCost.Cheap;

    public override IEnumerable<LintIssue> Check(string sql)
    {
        int upper = 0, lower = 0;
        var found = new List<(string text, int idx, string type)>();

        foreach (var kw in Keywords)
        {
            foreach (Match m in Regex.Matches(sql, $@"\b{kw}\b", RegexOptions.IgnoreCase))
            {
                if (LintHelpers.IsInsideStringOrComment(sql, m.Index)) continue;
                var text = m.Value;
                string type;
                if (text == text.ToUpperInvariant()) { upper++; type = "UPPER"; }
                else if (text == text.ToLowerInvariant()) { lower++; type = "lower"; }
                else type = "Mixed";
                found.Add((text, m.Index, type));
            }
        }

        var dominantUpper = upper >= lower;
        foreach (var item in found)
        {
            if (item.type == "Mixed")
            {
                yield return new LintIssue(Id,
                    $"{Id}: Keyword '{item.text}' has mixed casing", DefaultSeverity,
                    item.idx, item.idx + item.text.Length);
            }
            else if (item.type != (dominantUpper ? "UPPER" : "lower"))
            {
                yield return new LintIssue(Id,
                    $"{Id}: Keyword '{item.text}' should be {(dominantUpper ? "UPPERCASE" : "lowercase")}",
                    DefaultSeverity, item.idx, item.idx + item.text.Length);
            }
        }
    }
}

// ====== NZ008: TRUNCATE ======
public class RuleNZ008_Truncate : LintRule
{
    public override string Id => "NZ008";
    public override string Name => "Truncate Table";
    public override string Description => "TRUNCATE removes all data and cannot be rolled back";
    public override LintSeverity DefaultSeverity => LintSeverity.Warning;

    public override RuleCost Cost => RuleCost.Cheap;

    public override IEnumerable<LintIssue> Check(string sql)
    {
        foreach (Match m in Regex.Matches(sql, @"\bTRUNCATE\b", RegexOptions.IgnoreCase))
        {
            if (LintHelpers.IsInsideStringOrComment(sql, m.Index)) continue;
            yield return new LintIssue(Id, $"{Id}: {Description}", DefaultSeverity, m.Index, m.Index + 8);
        }
    }
}

// ====== NZ009: OR in WHERE ======
public class RuleNZ009_OrInWhere : LintRule
{
    public override string Id => "NZ009";
    public override string Name => "Or In Where Clause";
    public override string Description => "Multiple OR conditions may prevent Zone Map pruning";
    public override LintSeverity DefaultSeverity => LintSeverity.Hint;
    public override RuleCost Cost => RuleCost.Cheap;
    public override int Priority => 60; // Performance — Zone Map pruning, elevated from Hint default 30

    public override IEnumerable<LintIssue> Check(string sql)
    {
        foreach (Match whereM in Regex.Matches(sql, @"\bWHERE\b", RegexOptions.IgnoreCase))
        {
            if (LintHelpers.IsInsideStringOrComment(sql, whereM.Index)) continue;
            var after = sql[whereM.Index..];
            var end = Regex.Match(after, @"\b(GROUP\s+BY|ORDER\s+BY|HAVING|LIMIT|UNION|;)", RegexOptions.IgnoreCase);
            var clause = end.Success ? after[..end.Index] : after;
            var ors = Regex.Matches(clause, @"\bOR\b", RegexOptions.IgnoreCase);
            if (ors.Count >= 2)
            {
                yield return new LintIssue(Id,
                    $"{Id}: {Description} ({ors.Count} OR conditions found)",
                    DefaultSeverity, whereM.Index + ors[0].Index, whereM.Index + ors[0].Index + 2);
            }
        }
    }
}

// ====== NZ010: Missing table alias ======
public class RuleNZ010_MissingAlias : LintRule
{
    public override string Id => "NZ010";
    public override string Name => "Missing Table Alias";
    public override string Description => "Consider using table aliases in JOINs";
    public override LintSeverity DefaultSeverity => LintSeverity.Information;
    public override RuleCost Cost => RuleCost.Cheap;
    public override int Priority => 55; // Readability — slightly elevated above default Info 50

    public override IEnumerable<LintIssue> Check(string sql)
    {
        foreach (Match m in Regex.Matches(sql, @"\bJOIN\s+([\w.""\[\]]+)\s+ON\b", RegexOptions.IgnoreCase))
        {
            if (LintHelpers.IsInsideStringOrComment(sql, m.Index)) continue;
            yield return new LintIssue(Id,
                $"{Id}: Table '{m.Groups[1].Value}' in JOIN has no alias", DefaultSeverity,
                m.Index, m.Index + m.Length);
        }
    }
}

// ====== NZ011: CTAS missing DISTRIBUTE ON ======
public class RuleNZ011_CtasMissingDistribution : LintRule
{
    public override string Id => "NZ011";
    public override string Name => "CTAS Missing Distribution";
    public override string Description => "CREATE TABLE AS SELECT should specify explicit data distribution";
    public override LintSeverity DefaultSeverity => LintSeverity.Warning;

    public override RuleCost Cost => RuleCost.Cheap;

    public override IEnumerable<LintIssue> Check(string sql)
    {
        foreach (Match m in Regex.Matches(sql,
            @"\bCREATE\s+TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?[\w.""\[\]]+\s+AS\s+(?:\(\s*)?SELECT\b", RegexOptions.IgnoreCase))
        {
            if (LintHelpers.IsInsideStringOrComment(sql, m.Index)) continue;
            var endIdx = sql.Length;
            for (int i = m.Index; i < sql.Length; i++)
            {
                if (sql[i] == ';' && !LintHelpers.IsInsideStringOrComment(sql, i)) { endIdx = i; break; }
            }
            var stmt = sql[m.Index..endIdx];
            if (!Regex.IsMatch(stmt, @"\bDISTRIBUTE\s+ON\b", RegexOptions.IgnoreCase))
                yield return new LintIssue(Id,
                    $"{Id}: {Description} - Add DISTRIBUTE ON (...)", DefaultSeverity,
                    m.Index, m.Index + m.Length);
        }
    }
}

// ====== NZ012: UPDATE ... AS ======
public class RuleNZ012_UpdateWithAs : LintRule
{
    public override string Id => "NZ012";
    public override string Name => "Update Alias With AS";
    public override string Description => "Netezza UPDATE does not support AS for table aliases";
    public override LintSeverity DefaultSeverity => LintSeverity.Error;

    public override RuleCost Cost => RuleCost.Cheap;

    public override IEnumerable<LintIssue> Check(string sql)
    {
        foreach (Match m in Regex.Matches(sql, @"\bUPDATE\s+[\w.""\[\]]+\s+AS\s+\w+", RegexOptions.IgnoreCase))
        {
            if (LintHelpers.IsInsideStringOrComment(sql, m.Index)) continue;
            var asMatch = Regex.Match(m.Value, @"\bAS\b", RegexOptions.IgnoreCase);
            yield return new LintIssue(Id, $"{Id}: {Description}", DefaultSeverity,
                m.Index + asMatch.Index, m.Index + asMatch.Index + 2);
        }
    }
}

// ====== NZ013: Prefer UNION ALL ======
public class RuleNZ013_PreferUnionAll : LintRule
{
    public override string Id => "NZ013";
    public override string Name => "Prefer Union All";
    public override string Description => "UNION does a distinct sort; use UNION ALL if duplicates are OK";
    public override LintSeverity DefaultSeverity => LintSeverity.Information;

    public override RuleCost Cost => RuleCost.Cheap;

    public override IEnumerable<LintIssue> Check(string sql)
    {
        foreach (Match m in Regex.Matches(sql, @"\bUNION\b(?!\s+ALL\b)", RegexOptions.IgnoreCase))
        {
            if (LintHelpers.IsInsideStringOrComment(sql, m.Index)) continue;
            yield return new LintIssue(Id, $"{Id}: {Description}", DefaultSeverity, m.Index, m.Index + 5);
        }
    }
}

// ====== NZ014: OR in JOIN condition ======
public class RuleNZ014_OrInJoin : LintRule
{
    public override string Id => "NZ014";
    public override string Name => "Or In Join Condition";
    public override string Description => "OR in JOIN condition can cause Cartesian product";
    public override LintSeverity DefaultSeverity => LintSeverity.Error;

    public override RuleCost Cost => RuleCost.Cheap;

    public override IEnumerable<LintIssue> Check(string sql)
    {
        foreach (Match m in Regex.Matches(sql,
            @"\bJOIN\s+[\w.""]+(?:\s+(?:AS\s+)?\w+)?\s+ON\b(?:(?!\b(?:WHERE|JOIN|GROUP\s+BY|ORDER\s+BY|HAVING|LIMIT|UNION|INTERSECT|EXCEPT|;)\b).)*?\bOR\b",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            if (LintHelpers.IsInsideStringOrComment(sql, m.Index)) continue;
            var orMatch = Regex.Match(m.Value, @"\bOR\b", RegexOptions.IgnoreCase);
            yield return new LintIssue(Id, $"{Id}: {Description}", DefaultSeverity,
                m.Index + orMatch.Index, m.Index + orMatch.Index + 2);
        }
    }
}

// ====== NZ015: Functions in WHERE ======
public class RuleNZ015_FunctionsInWhere : LintRule
{
    public override string Id => "NZ015";
    public override string Name => "Function in Where Clause";
    public override string Description => "Using functions in WHERE prevents Zone Map pruning";
    public override LintSeverity DefaultSeverity => LintSeverity.Warning;
    public override RuleCost Cost => RuleCost.Cheap;
    public override int Priority => 80; // Performance — Zone Map pruning

    public override IEnumerable<LintIssue> Check(string sql)
    {
        foreach (Match whereM in Regex.Matches(sql, @"\bWHERE\b", RegexOptions.IgnoreCase))
        {
            if (LintHelpers.IsInsideStringOrComment(sql, whereM.Index)) continue;
            var after = sql[whereM.Index..];
            var end = Regex.Match(after, @"\b(GROUP\s+BY|ORDER\s+BY|HAVING|LIMIT|UNION|INTERSECT|EXCEPT|;)\b",
                RegexOptions.IgnoreCase);
            var clause = end.Success ? after[..end.Index] : after;
            foreach (Match m in Regex.Matches(clause,
                @"\b([A-Z_][A-Z0-9_]*)\s*\(\s*([A-Z_][A-Z0-9_]*(?:\.[A-Z_][A-Z0-9_]*)?)\s*(?:,|\))",
                RegexOptions.IgnoreCase))
            {
                yield return new LintIssue(Id, $"{Id}: {Description}", DefaultSeverity,
                    whereM.Index + m.Index, whereM.Index + m.Index + m.Length);
            }
        }
    }
}

// ====== NZ016: Implicit casting in JOIN ======
public class RuleNZ016_ImplicitCastInJoin : LintRule
{
    public override string Id => "NZ016";
    public override string Name => "Implicit Casting in Join";
    public override string Description => "Avoid joining columns with different data types";
    public override LintSeverity DefaultSeverity => LintSeverity.Warning;

    public override RuleCost Cost => RuleCost.Cheap;

    public override IEnumerable<LintIssue> Check(string sql)
    {
        foreach (Match m in Regex.Matches(sql, @"ON\s+.*?::.*?=", RegexOptions.IgnoreCase))
        {
            if (LintHelpers.IsInsideStringOrComment(sql, m.Index)) continue;
            yield return new LintIssue(Id, $"{Id}: {Description}", DefaultSeverity, m.Index, m.Index + m.Length);
        }
    }
}

// ====== NZ017: Double-quoted identifiers ======
public class RuleNZ017_DoubleQuotedIdentifiers : LintRule
{
    public override string Id => "NZ017";
    public override string Name => "Double Quoted Identifiers";
    public override string Description => "Double-quoted identifiers are case-sensitive in Netezza";
    public override LintSeverity DefaultSeverity => LintSeverity.Information;

    public override RuleCost Cost => RuleCost.Cheap;

    public override IEnumerable<LintIssue> Check(string sql)
    {
        foreach (Match m in Regex.Matches(sql, @"""[^""]*"""))
        {
            if (LintHelpers.IsInsideStringOrComment(sql, m.Index)) continue;
            yield return new LintIssue(Id, $"{Id}: {Description}", DefaultSeverity, m.Index, m.Index + m.Length);
        }
    }
}

// ====== NZ018: Self-referential join ======
public class RuleNZ018_SelfReferentialJoin : LintRule
{
    public override string Id => "NZ018";
    public override string Name => "Self Referential Join";
    public override string Description => "JOIN condition compares same column to itself";
    public override LintSeverity DefaultSeverity => LintSeverity.Warning;
    public override RuleCost Cost => RuleCost.Cheap;
    public override int Priority => 75; // Logic correctness — likely a bug

    public override IEnumerable<LintIssue> Check(string sql)
    {
        foreach (Match m in Regex.Matches(sql,
            @"\b(?:ON|WHERE|AND|OR)\b[^=!<>]*?\b([\w.]+)\s*=\s*\b\1\b", RegexOptions.IgnoreCase))
        {
            if (LintHelpers.IsInsideStringOrComment(sql, m.Index)) continue;
            var idStart = m.Value.IndexOf(m.Groups[1].Value);
            yield return new LintIssue(Id,
                $"{Id}: {Description} (found '{m.Groups[1].Value}')", DefaultSeverity,
                m.Index + idStart, m.Index + idStart + m.Groups[1].Value.Length);
        }
    }
}

// ====== NZ019: CASE without END ======
public class RuleNZ019_CaseWithoutEnd : LintRule
{
    public override string Id => "NZ019";
    public override string Name => "Case Without End";
    public override string Description => "CASE expression must end with END keyword";
    public override LintSeverity DefaultSeverity => LintSeverity.Error;

    public override RuleCost Cost => RuleCost.Cheap;

    public override IEnumerable<LintIssue> Check(string sql)
    {
        int pos = 0;
        while (pos < sql.Length)
        {
            var caseMatch = Regex.Match(sql[pos..], @"\bCASE\b", RegexOptions.IgnoreCase);
            if (!caseMatch.Success) break;
            var caseIdx = pos + caseMatch.Index;
            if (LintHelpers.IsInsideStringOrComment(sql, caseIdx))
            {
                pos = caseIdx + 4;
                continue;
            }

            // Find the next END after this CASE, skipping nested CASE...END pairs
            var searchFrom = caseIdx + 4;
            int depth = 1;
            while (searchFrom < sql.Length && depth > 0)
            {
                if (LintHelpers.IsInsideStringOrComment(sql, searchFrom))
                {
                    searchFrom++;
                    continue;
                }
                var remaining = sql[searchFrom..];
                var nextCase = Regex.Match(remaining, @"\bCASE\b", RegexOptions.IgnoreCase);
                var nextEnd = Regex.Match(remaining, @"\bEND\b", RegexOptions.IgnoreCase);

                if (nextEnd.Success && (!nextCase.Success || nextEnd.Index < nextCase.Index))
                {
                    depth--;
                    searchFrom += nextEnd.Index + 3;
                }
                else if (nextCase.Success)
                {
                    depth++;
                    searchFrom += nextCase.Index + 4;
                }
                else if (nextEnd.Success)
                {
                    depth--;
                    searchFrom += nextEnd.Index + 3;
                }
                else
                {
                    break;
                }
            }

            if (depth > 0)
            {
                // No matching END found
                var endOfStmt = sql.IndexOf(';', caseIdx);
                var endPos = endOfStmt >= 0 ? endOfStmt : sql.Length;
                yield return new LintIssue(Id, $"{Id}: {Description}", DefaultSeverity, caseIdx, endPos);
            }

            pos = caseIdx + 4;
        }
    }
}

// ====== NZ020: IN (SELECT ...) subquery efficiency ======
public class RuleNZ020_InSubqueryEfficiency : LintRule
{
    public override string Id => "NZ020";
    public override string Name => "Subquery Efficiency";
    public override string Description => "Consider EXISTS or JOIN instead of IN (SELECT ...)";
    public override LintSeverity DefaultSeverity => LintSeverity.Information;

    public override RuleCost Cost => RuleCost.Cheap;

    public override IEnumerable<LintIssue> Check(string sql)
    {
        foreach (Match m in Regex.Matches(sql, @"\bIN\s*\(\s*SELECT\b", RegexOptions.IgnoreCase))
        {
            if (LintHelpers.IsInsideStringOrComment(sql, m.Index)) continue;
            yield return new LintIssue(Id, $"{Id}: {Description}", DefaultSeverity, m.Index, m.Index + m.Length);
        }
    }
}

// ====== NZ021: duplicate comma ======
public class RuleNZ021_DoubleComma : LintRule
{
    public override string Id => "NZ021";
    public override string Name => "Double Comma";
    public override string Description => "Duplicate comma in SQL expression or list";
    public override LintSeverity DefaultSeverity => LintSeverity.Error;
    public override int Priority => 95;

    public override RuleCost Cost => RuleCost.Cheap;
    public override IEnumerable<LintIssue> Check(string sql)
    {
        foreach (Match m in Regex.Matches(sql, @",\s*,", RegexOptions.IgnoreCase))
        {
            if (LintHelpers.IsInsideStringOrComment(sql, m.Index)) continue;
            yield return new LintIssue(Id, $"{Id}: {Description}", DefaultSeverity, m.Index, m.Index + m.Length);
        }
    }
}

// ====== NZ022: WHERE without FROM ======
public class RuleNZ022_WhereWithoutFrom : LintRule
{
    public override string Id => "NZ022";
    public override string Name => "WHERE Without FROM";
    public override string Description => "WHERE clause appears without a preceding FROM clause";
    public override LintSeverity DefaultSeverity => LintSeverity.Error;
    public override RuleCost Cost => RuleCost.Cheap;

    public override IEnumerable<LintIssue> Check(string sql)
    {
        foreach (Match select in Regex.Matches(sql, @"\bSELECT\b", RegexOptions.IgnoreCase))
        {
            if (LintHelpers.IsInsideStringOrComment(sql, select.Index)) continue;
            var end = sql.IndexOf(';', select.Index);
            if (end < 0) end = sql.Length;
            var statement = sql[select.Index..end];
            var where = Regex.Match(statement, @"\bWHERE\b", RegexOptions.IgnoreCase);
            if (!where.Success || Regex.IsMatch(statement[..where.Index], @"\bFROM\b", RegexOptions.IgnoreCase)) continue;
            yield return new LintIssue(Id, $"{Id}: {Description}", DefaultSeverity,
                select.Index + where.Index, select.Index + where.Index + where.Length);
        }
    }
}

// ====== NZ023: Common SQL keyword typos ======
public class RuleNZ023_KeywordTypo : LintRule
{
    public override string Id => "NZ023";
    public override string Name => "Keyword Typo";
    public override string Description => "Common SQL keyword typo detected";
    public override LintSeverity DefaultSeverity => LintSeverity.Error;
    public override int Priority => 95;

    private static readonly (string Typo, string Correction)[] TypoMap =
    [
        ("SELEC", "SELECT"), ("SELCT", "SELECT"), ("SELCET", "SELECT"),
        ("FORM", "FROM"), ("FROME", "FROM"),
        ("WEHERE", "WHERE"), ("WEHRE", "WHERE"), ("WEAR", "WHERE"),
        ("INSET", "INSERT"), ("INSTERT", "INSERT"),
        ("UPDAT", "UPDATE"), ("UPDTE", "UPDATE"),
        ("DELET", "DELETE"), ("DEELTE", "DELETE"),
        ("GROP", "GROUP"), ("GROPU", "GROUP"),
        ("HAVIGN", "HAVING"), ("HAVNG", "HAVING"),
        ("ORDET", "ORDER"), ("ODER", "ORDER"),
        ("LMIT", "LIMIT"), ("LIMT", "LIMIT"),
        ("DISTINT", "DISTINCT"), ("DISTNCT", "DISTINCT"),
        ("BEWTEEN", "BETWEEN"), ("BEETWEEN", "BETWEEN"), ("BETWEE", "BETWEEN"),
    ];

    public override RuleCost Cost => RuleCost.Cheap;

    public override IEnumerable<LintIssue> Check(string sql)
    {
        foreach (var (typo, correction) in TypoMap)
        {
            foreach (Match m in Regex.Matches(sql, $@"\b{typo}\b", RegexOptions.IgnoreCase))
            {
                if (LintHelpers.IsInsideStringOrComment(sql, m.Index)) continue;
                yield return new LintIssue(Id, $"{Id}: Typo '{typo}' → '{correction}'", DefaultSeverity, m.Index, m.Index + m.Length);
            }
        }
    }
}

// ====== NZ024: Trailing comma before FROM/semicolon ======
public class RuleNZ024_TrailingComma : LintRule
{
    public override string Id => "NZ024";
    public override string Name => "Trailing Comma";
    public override string Description => "Trailing comma before FROM or semicolon";
    public override LintSeverity DefaultSeverity => LintSeverity.Error;

    public override RuleCost Cost => RuleCost.Cheap;

    public override IEnumerable<LintIssue> Check(string sql)
    {
        foreach (Match m in Regex.Matches(sql, @",(\s*)(FROM\b|;)", RegexOptions.IgnoreCase))
        {
            if (LintHelpers.IsInsideStringOrComment(sql, m.Index)) continue;
            yield return new LintIssue(Id, $"{Id}: {Description}", DefaultSeverity, m.Index, m.Index + 1);
        }
    }
}

// ====== All rules registry ======
public static class NzLintRules
{
    public static readonly IReadOnlyList<LintRule> AllRules = new LintRule[]
    {
        new RuleNZ001_SelectStar(),
        new RuleNZ002_DeleteWithoutWhere(),
        new RuleNZ003_UpdateWithoutWhere(),
        new RuleNZ004_CrossJoin(),
        new RuleNZ005_LeadingWildcardLike(),
        new RuleNZ006_OrderByWithoutLimit(),
        new RuleNZ007_InconsistentCase(),
        new RuleNZ008_Truncate(),
        new RuleNZ009_OrInWhere(),
        new RuleNZ010_MissingAlias(),
        new RuleNZ011_CtasMissingDistribution(),
        new RuleNZ012_UpdateWithAs(),
        new RuleNZ013_PreferUnionAll(),
        new RuleNZ014_OrInJoin(),
        new RuleNZ015_FunctionsInWhere(),
        new RuleNZ016_ImplicitCastInJoin(),
        new RuleNZ017_DoubleQuotedIdentifiers(),
        new RuleNZ018_SelfReferentialJoin(),
        new RuleNZ019_CaseWithoutEnd(),
        new RuleNZ020_InSubqueryEfficiency(),
        new RuleNZ021_DoubleComma(),
        new RuleNZ022_WhereWithoutFrom(),
        new RuleNZ023_KeywordTypo(),
        new RuleNZ024_TrailingComma()
    };

    public static LintSeverity MapSeverity(RuleSeverityConfig config) => config switch
    {
        RuleSeverityConfig.Error => LintSeverity.Error,
        RuleSeverityConfig.Warning => LintSeverity.Warning,
        RuleSeverityConfig.Information => LintSeverity.Information,
        RuleSeverityConfig.Hint => LintSeverity.Hint,
        _ => LintSeverity.Warning
    };
}
