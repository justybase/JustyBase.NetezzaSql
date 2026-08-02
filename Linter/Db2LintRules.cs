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

public sealed class RuleDB2001_SelectStar : DelegatingLintRule
{
    public RuleDB2001_SelectStar() : base(SharedQualityRuleFactory.CreateSelectStar(
        "DB2001", "Select Star",
        "Avoid SELECT * in production Db2 queries when a stable projection is possible.",
        LintSeverity.Warning))
    {
    }
}

public sealed class RuleDB2002_DeleteWithoutWhere : DelegatingLintRule
{
    public RuleDB2002_DeleteWithoutWhere() : base(SharedQualityRuleFactory.CreateDeleteWithoutWhere(
        "DB2002", "Delete Without Where",
        "DELETE without WHERE removes every row in the target table.",
        LintSeverity.Error,
        new SqlLintScannerOptions(HandleQQuotedStrings: true, StatementEnd: Db2LintHelpers.StatementEnd)))
    {
    }
}

public sealed class RuleDB2003_UpdateWithoutWhere : DelegatingLintRule
{
    public RuleDB2003_UpdateWithoutWhere() : base(SharedQualityRuleFactory.CreateUpdateWithoutWhere(
        "DB2003", "Update Without Where",
        "UPDATE without WHERE changes every row in the target table.",
        LintSeverity.Error,
        new SqlLintScannerOptions(HandleQQuotedStrings: true, StatementEnd: Db2LintHelpers.StatementEnd)))
    {
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

public sealed class RuleDB2008_NetezzaDoubleDot : DelegatingLintRule
{
    public RuleDB2008_NetezzaDoubleDot() : base(SharedQualityRuleFactory.CreateDoubleDotTable(
        "DB2008", "Netezza Double-Dot Table",
        "DB..TABLE is Netezza-only; use SCHEMA.TABLE or CURRENT SCHEMA on Db2 LUW.",
        LintSeverity.Error))
    {
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
