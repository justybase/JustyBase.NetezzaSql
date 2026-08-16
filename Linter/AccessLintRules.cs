using System.Text.RegularExpressions;

namespace JustyBase.NetezzaSqlParser.Linter;

/// <summary>Quality rules for Microsoft Access / Jet / ACE SQL.</summary>
public static class AccessLintRules
{
    public static IReadOnlyList<LintRule> AllRules { get; } =
    [
        new RuleAccess001_SelectStar(),
        new RuleAccess002_DeleteWithoutWhere(),
        new RuleAccess003_UpdateWithoutWhere(),
        new RuleAccess004_TopWithoutOrderBy(),
        new RuleAccess005_NonAccessSyntax(),
    ];
}

public sealed class RuleAccess001_SelectStar : LintRule
{
    public override string Id => "ACC001";
    public override string Name => "Select Star";
    public override string Description =>
        "Avoid SELECT * when a stable projection is possible.";
    public override LintSeverity DefaultSeverity => LintSeverity.Warning;
    public override RuleCost Cost => RuleCost.Cheap;

    public override IEnumerable<LintIssue> Check(string sql)
    {
        foreach (Match match in Regex.Matches(sql,
            @"\bSELECT\s+(?:(?:DISTINCTROW|DISTINCT|ALL)\s+)?(?:TOP\s+(?:\d+|\?|[@:][A-Za-z_]\w*)\s+(?:PERCENT\s+)?)?\*",
            RegexOptions.IgnoreCase))
        {
            if (LintHelpers.IsInsideStringOrComment(sql, match.Index))
                continue;

            var star = match.Index + match.Value.LastIndexOf('*');
            yield return new LintIssue(Id, $"{Id}: {Description}", DefaultSeverity, star, star + 1);
        }
    }
}

public sealed class RuleAccess002_DeleteWithoutWhere : DelegatingLintRule
{
    public RuleAccess002_DeleteWithoutWhere() : base(SharedQualityRuleFactory.CreateDeleteWithoutWhere(
        "ACC002", "Delete Without Where",
        "DELETE without WHERE removes every row in the target table.",
        LintSeverity.Error,
        new SqlLintScannerOptions()))
    {
    }
}

public sealed class RuleAccess003_UpdateWithoutWhere : DelegatingLintRule
{
    public RuleAccess003_UpdateWithoutWhere() : base(SharedQualityRuleFactory.CreateUpdateWithoutWhere(
        "ACC003", "Update Without Where",
        "UPDATE without WHERE changes every row in the target table.",
        LintSeverity.Error,
        new SqlLintScannerOptions()))
    {
    }
}

public sealed class RuleAccess004_TopWithoutOrderBy : LintRule
{
    public override string Id => "ACC004";
    public override string Name => "Top-N Without Order By";
    public override string Description =>
        "TOP without ORDER BY can return non-deterministic rows; add ORDER BY for stable results.";
    public override LintSeverity DefaultSeverity => LintSeverity.Warning;
    public override RuleCost Cost => RuleCost.Cheap;

    public override IEnumerable<LintIssue> Check(string sql)
    {
        foreach (Match match in Regex.Matches(sql, @"\bTOP\s+(?:\d+|\?|[@:][A-Za-z_]\w*)", RegexOptions.IgnoreCase))
        {
            if (LintHelpers.IsInsideStringOrComment(sql, match.Index))
                continue;

            var start = sql.LastIndexOf(';', Math.Max(0, match.Index - 1)) + 1;
            var end = sql.IndexOf(';', match.Index);
            if (end < 0) end = sql.Length;
            if (!Regex.IsMatch(sql[start..end], @"\bORDER\s+BY\b", RegexOptions.IgnoreCase))
                yield return new LintIssue(Id, $"{Id}: {Description}", DefaultSeverity,
                    match.Index, match.Index + match.Length);
        }
    }
}

public sealed class RuleAccess005_NonAccessSyntax : LintRule
{
    public override string Id => "ACC005";
    public override string Name => "Non-Access Syntax";
    public override string Description =>
        "The statement contains syntax from another SQL dialect; use Access equivalents such as TOP and &.";
    public override LintSeverity DefaultSeverity => LintSeverity.Error;
    public override RuleCost Cost => RuleCost.Cheap;

    public override IEnumerable<LintIssue> Check(string sql)
    {
        const string pattern =
            @"\b(?:LIMIT|ILIKE|GROOM|MERGE|RETURNING|OUTPUT)\b|\b(?:DISTRIBUTE|ORGANIZE)\s+ON\b|\bGENERATE\s+STATISTICS\b|\bON\s+CONFLICT\b|\|\|";

        foreach (Match match in Regex.Matches(sql, pattern, RegexOptions.IgnoreCase))
        {
            if (LintHelpers.IsInsideStringOrComment(sql, match.Index)
                || IsInsideAccessIdentifier(sql, match.Index))
                continue;
            yield return new LintIssue(Id, $"{Id}: {Description}", DefaultSeverity,
                match.Index, match.Index + match.Length);
        }
    }

    private static bool IsInsideAccessIdentifier(string sql, int position)
    {
        var inBracketIdentifier = false;
        var inBacktickIdentifier = false;
        for (var i = 0; i < position && i < sql.Length; i++)
        {
            if (inBracketIdentifier)
            {
                if (sql[i] == ']')
                {
                    if (i + 1 < position && sql[i + 1] == ']')
                        i++;
                    else
                        inBracketIdentifier = false;
                }
                continue;
            }

            if (inBacktickIdentifier)
            {
                if (sql[i] == '`')
                {
                    if (i + 1 < position && sql[i + 1] == '`')
                        i++;
                    else
                        inBacktickIdentifier = false;
                }
                continue;
            }

            if (sql[i] == '[')
                inBracketIdentifier = true;
            else if (sql[i] == '`')
                inBacktickIdentifier = true;
        }

        return inBracketIdentifier || inBacktickIdentifier;
    }
}
