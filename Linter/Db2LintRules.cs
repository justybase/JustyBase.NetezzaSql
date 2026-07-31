using System.Text.RegularExpressions;

namespace JustyBase.NetezzaSqlParser.Linter;

/// <summary>
/// Db2 LUW dialect lint rules. Port of extensions/db2/src/sql/qualityRules.ts
/// (DB2001–DB2008). Registry for Db2 documents contains only these rules.
/// </summary>
public static class Db2LintRules
{
    public static IReadOnlyList<LintRule> AllRules { get; } =
    [
        new RuleDB2001_SelectStar(),
        new RuleDB2002_DeleteWithoutWhere(),
        new RuleDB2003_UpdateWithoutWhere(),
        new RuleDB2004_NetezzaGroom(),
        new RuleDB2005_NetezzaDistributeOn(),
        new RuleDB2006_TopNWithoutOrderBy(),
        new RuleDB2007_NetezzaLimit(),
        new RuleDB2008_NetezzaDoubleDot(),
    ];
}

public class RuleDB2001_SelectStar : LintRule
{
    public override string Id => "DB2001";
    public override string Name => "Select Star";
    public override string Description => "Avoid SELECT * in production Db2 queries when a stable projection is possible.";
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

public class RuleDB2002_DeleteWithoutWhere : LintRule
{
    public override string Id => "DB2002";
    public override string Name => "Delete Without Where";
    public override string Description => "DELETE without WHERE removes every row in the target table.";
    public override LintSeverity DefaultSeverity => LintSeverity.Error;
    public override RuleCost Cost => RuleCost.Cheap;

    public override IEnumerable<LintIssue> Check(string sql)
    {
        // Multipart quoted identifiers: (?:\s*\.\s*...){0,2} after first segment —
        // same precedence fix as ORA002 (avoid matching only the last segment).
        foreach (Match m in Regex.Matches(sql,
            @"\bDELETE\s+FROM\s+(?:""[^""]+""|[A-Za-z_][\w$#]*)(?:\s*\.\s*(?:""[^""]+""|[A-Za-z_][\w$#]*)){0,2}",
            RegexOptions.IgnoreCase))
        {
            if (LintHelpers.IsInsideStringOrComment(sql, m.Index)) continue;
            var end = Db2LintHelpers.StatementEnd(sql, m.Index);
            var tail = sql[(m.Index + m.Length)..end];
            if (!Regex.IsMatch(tail, @"\bWHERE\b", RegexOptions.IgnoreCase))
                yield return new LintIssue(Id, $"{Id}: {Description}", DefaultSeverity, m.Index, m.Index + 6);
        }
    }
}

public class RuleDB2003_UpdateWithoutWhere : LintRule
{
    public override string Id => "DB2003";
    public override string Name => "Update Without Where";
    public override string Description => "UPDATE without WHERE changes every row in the target table.";
    public override LintSeverity DefaultSeverity => LintSeverity.Error;
    public override RuleCost Cost => RuleCost.Cheap;

    public override IEnumerable<LintIssue> Check(string sql)
    {
        foreach (Match m in Regex.Matches(sql,
            @"\bUPDATE\s+(?:""[^""]+""|[A-Za-z_][\w$#]*)(?:\s*\.\s*(?:""[^""]+""|[A-Za-z_][\w$#]*)){0,2}\s+SET\b",
            RegexOptions.IgnoreCase))
        {
            if (LintHelpers.IsInsideStringOrComment(sql, m.Index)) continue;
            var end = Db2LintHelpers.StatementEnd(sql, m.Index);
            var tail = sql[(m.Index + m.Length)..end];
            if (!Regex.IsMatch(tail, @"\bWHERE\b", RegexOptions.IgnoreCase))
                yield return new LintIssue(Id, $"{Id}: {Description}", DefaultSeverity, m.Index, m.Index + 6);
        }
    }
}

public class RuleDB2004_NetezzaGroom : LintRule
{
    public override string Id => "DB2004";
    public override string Name => "Netezza Groom";
    public override string Description => "GROOM is Netezza-only; use RUNSTATS / REORG on Db2 LUW instead.";
    public override LintSeverity DefaultSeverity => LintSeverity.Error;
    public override RuleCost Cost => RuleCost.Cheap;

    public override IEnumerable<LintIssue> Check(string sql)
    {
        foreach (Match m in Regex.Matches(sql, @"\bGROOM\b", RegexOptions.IgnoreCase))
        {
            if (LintHelpers.IsInsideStringOrComment(sql, m.Index)) continue;
            yield return new LintIssue(Id, $"{Id}: {Description}", DefaultSeverity, m.Index, m.Index + m.Length);
        }
    }
}

public class RuleDB2005_NetezzaDistributeOn : LintRule
{
    public override string Id => "DB2005";
    public override string Name => "Netezza Distribute On";
    public override string Description => "DISTRIBUTE ON is Netezza-only; use DISTRIBUTE BY HASH / ORGANIZE BY on Db2 LUW.";
    public override LintSeverity DefaultSeverity => LintSeverity.Error;
    public override RuleCost Cost => RuleCost.Cheap;

    public override IEnumerable<LintIssue> Check(string sql)
    {
        foreach (Match m in Regex.Matches(sql, @"\bDISTRIBUTE\s+ON\b", RegexOptions.IgnoreCase))
        {
            if (LintHelpers.IsInsideStringOrComment(sql, m.Index)) continue;
            yield return new LintIssue(Id, $"{Id}: {Description}", DefaultSeverity, m.Index, m.Index + m.Length);
        }
    }
}

public class RuleDB2006_TopNWithoutOrderBy : LintRule
{
    public override string Id => "DB2006";
    public override string Name => "Top-N Without Order By";
    public override string Description =>
        "FETCH FIRST / OPTIMIZE FOR without ORDER BY in the same SELECT can return non-deterministic rows; add ORDER BY for stable top-N.";
    public override LintSeverity DefaultSeverity => LintSeverity.Warning;
    public override RuleCost Cost => RuleCost.Cheap;

    public override IEnumerable<LintIssue> Check(string sql)
    {
        foreach (Match m in Regex.Matches(sql, @"\b(?:FETCH\s+(?:FIRST|NEXT)|OPTIMIZE\s+FOR)\b", RegexOptions.IgnoreCase))
        {
            if (LintHelpers.IsInsideStringOrComment(sql, m.Index)) continue;
            var start = sql.LastIndexOf(';', m.Index - 1) + 1;
            var end = Db2LintHelpers.StatementEnd(sql, m.Index);
            var statement = sql[start..end];
            if (!Regex.IsMatch(statement, @"\bORDER\s+BY\b", RegexOptions.IgnoreCase))
                yield return new LintIssue(Id, $"{Id}: {Description}", DefaultSeverity, m.Index, m.Index + m.Length);
        }
    }
}

public class RuleDB2007_NetezzaLimit : LintRule
{
    public override string Id => "DB2007";
    public override string Name => "Netezza Limit";
    public override string Description => "LIMIT is Netezza-only; use FETCH FIRST n ROWS ONLY on Db2 LUW.";
    public override LintSeverity DefaultSeverity => LintSeverity.Error;
    public override RuleCost Cost => RuleCost.Cheap;

    public override IEnumerable<LintIssue> Check(string sql)
    {
        foreach (Match m in Regex.Matches(sql, @"\bLIMIT\s+\d+", RegexOptions.IgnoreCase))
        {
            if (LintHelpers.IsInsideStringOrComment(sql, m.Index)) continue;
            yield return new LintIssue(Id, $"{Id}: {Description}", DefaultSeverity, m.Index, m.Index + m.Length);
        }
    }
}

public class RuleDB2008_NetezzaDoubleDot : LintRule
{
    public override string Id => "DB2008";
    public override string Name => "Netezza Double-Dot Table";
    public override string Description => "DB..TABLE is Netezza-only; use SCHEMA.TABLE or CURRENT SCHEMA on Db2 LUW.";
    public override LintSeverity DefaultSeverity => LintSeverity.Error;
    public override RuleCost Cost => RuleCost.Cheap;

    public override IEnumerable<LintIssue> Check(string sql)
    {
        foreach (Match m in Regex.Matches(sql, @"\b[A-Za-z_][\w$#]*\s*\.\s*\.\s*[A-Za-z_][\w$#]*"))
        {
            if (LintHelpers.IsInsideStringOrComment(sql, m.Index)) continue;
            yield return new LintIssue(Id, $"{Id}: {Description}", DefaultSeverity, m.Index, m.Index + m.Length);
        }
    }
}

internal static class Db2LintHelpers
{
    public static int StatementEnd(string sql, int start)
    {
        char? quote = null;
        var lineComment = false;
        var blockComment = false;

        for (var index = start; index < sql.Length; index++)
        {
            var ch = sql[index];
            var next = index + 1 < sql.Length ? sql[index + 1] : '\0';

            if (lineComment)
            {
                if (ch is '\n' or '\r') lineComment = false;
                continue;
            }

            if (blockComment)
            {
                if (ch == '*' && next == '/')
                {
                    blockComment = false;
                    index++;
                }
                continue;
            }

            if (quote is not null)
            {
                if (ch == quote)
                {
                    if (next == quote) index++;
                    else quote = null;
                }
                continue;
            }

            if (ch == '-' && next == '-')
            {
                lineComment = true;
                index++;
                continue;
            }
            if (ch == '/' && next == '*')
            {
                blockComment = true;
                index++;
                continue;
            }
            if (ch is '\'' or '"')
            {
                quote = ch;
                continue;
            }
            if (ch == ';')
                return index;
        }

        return sql.Length;
    }
}
