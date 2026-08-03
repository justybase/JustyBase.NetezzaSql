using System.Text.RegularExpressions;

namespace JustyBase.NetezzaSqlParser.Linter;

/// <summary>
/// MSSQL (T-SQL) dialect lint rules. Port of
/// extensions/mssql/src/sql/qualityRules.ts (MSS001–MSS008).
/// Registry for MSSQL documents contains only these rules.
/// </summary>
public static class MssqlLintRules
{
    public static IReadOnlyList<LintRule> AllRules { get; } =
    [
        new RuleMSS001_SelectStar(),
        new RuleMSS002_DeleteWithoutWhere(),
        new RuleMSS003_UpdateWithoutWhere(),
        new RuleMSS004_NetezzaGroom(),
        new RuleMSS005_NetezzaDistributeOn(),
        new RuleMSS006_TopNWithoutOrderBy(),
        new RuleMSS007_NetezzaLimit(),
        new RuleMSS008_NetezzaDoubleDot(),
    ];
}

public class RuleMSS001_SelectStar : LintRule
{
    public override string Id => "MSS001";
    public override string Name => "Select Star";
    public override string Description =>
        "Avoid SELECT * in production T-SQL when a stable projection is possible.";
    public override LintSeverity DefaultSeverity => LintSeverity.Warning;
    public override RuleCost Cost => RuleCost.Cheap;

    public override IEnumerable<LintIssue> Check(string sql)
    {
        foreach (Match m in Regex.Matches(sql,
            @"\bSELECT\s+(?:TOP\s*\([^)]*\)\s+|TOP\s+\d+\s+)?\*",
            RegexOptions.IgnoreCase))
        {
            if (LintHelpers.IsInsideStringOrComment(sql, m.Index)) continue;
            var star = m.Index + m.Value.LastIndexOf('*');
            yield return new LintIssue(Id, $"{Id}: {Description}", DefaultSeverity, star, star + 1);
        }
    }
}

public sealed class RuleMSS002_DeleteWithoutWhere : DelegatingLintRule
{
    public RuleMSS002_DeleteWithoutWhere() : base(SharedQualityRuleFactory.CreateDeleteWithoutWhere(
        "MSS002", "Delete Without Where",
        "DELETE without WHERE removes every row in the target table.",
        LintSeverity.Error,
        new SqlLintScannerOptions(StatementEnd: MssqlLintHelpers.StatementEnd)))
    {
    }
}

public sealed class RuleMSS003_UpdateWithoutWhere : DelegatingLintRule
{
    public RuleMSS003_UpdateWithoutWhere() : base(SharedQualityRuleFactory.CreateUpdateWithoutWhere(
        "MSS003", "Update Without Where",
        "UPDATE without WHERE changes every row in the target table.",
        LintSeverity.Error,
        new SqlLintScannerOptions(StatementEnd: MssqlLintHelpers.StatementEnd)))
    {
    }
}

public class RuleMSS004_NetezzaGroom : LintRule
{
    public override string Id => "MSS004";
    public override string Name => "Netezza Groom";
    public override string Description =>
        "GROOM is Netezza-only; use ALTER INDEX / maintenance plans on SQL Server.";
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

public class RuleMSS005_NetezzaDistributeOn : LintRule
{
    public override string Id => "MSS005";
    public override string Name => "Netezza Distribute On";
    public override string Description =>
        "DISTRIBUTE ON is Netezza-only; use partitioned tables / indexes on SQL Server.";
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

public class RuleMSS006_TopNWithoutOrderBy : LintRule
{
    public override string Id => "MSS006";
    public override string Name => "Top-N Without Order By";
    public override string Description =>
        "TOP / OFFSET FETCH without ORDER BY in the same SELECT can return non-deterministic rows; add ORDER BY for stable top-N.";
    public override LintSeverity DefaultSeverity => LintSeverity.Warning;
    public override RuleCost Cost => RuleCost.Cheap;

    public override IEnumerable<LintIssue> Check(string sql)
    {
        foreach (Match m in Regex.Matches(sql, @"\b(?:TOP\s*\(|TOP\s+\d+|OFFSET\s+\d+)", RegexOptions.IgnoreCase))
        {
            if (LintHelpers.IsInsideStringOrComment(sql, m.Index)) continue;
            var start = sql.LastIndexOf(';', Math.Max(0, m.Index - 1)) + 1;
            var end = MssqlLintHelpers.StatementEnd(sql, m.Index);
            var statement = sql[start..end];
            if (!Regex.IsMatch(statement, @"\bORDER\s+BY\b", RegexOptions.IgnoreCase))
                yield return new LintIssue(Id, $"{Id}: {Description}", DefaultSeverity, m.Index, m.Index + m.Length);
        }
    }
}

public class RuleMSS007_NetezzaLimit : LintRule
{
    public override string Id => "MSS007";
    public override string Name => "Netezza Limit";
    public override string Description =>
        "LIMIT is Netezza-only; use TOP or OFFSET/FETCH NEXT on SQL Server.";
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

public sealed class RuleMSS008_NetezzaDoubleDot : DelegatingLintRule
{
    public RuleMSS008_NetezzaDoubleDot() : base(SharedQualityRuleFactory.CreateDoubleDotTable(
        "MSS008", "Netezza Double-Dot Table",
        "DB..TABLE is Netezza-only; use SCHEMA.TABLE or database.schema.table on SQL Server.",
        LintSeverity.Error))
    {
    }
}

internal static class MssqlLintHelpers
{
    /// <summary>
    /// Finds the end of the statement starting at <paramref name="start"/>,
    /// skipping [bracket] identifiers (with the ]] escape), strings and
    /// comments — the port of createStatementEndScanner({ brackets: true }).
    /// </summary>
    public static int StatementEnd(string sql, int start)
    {
        char? quote = null;
        var bracket = false;
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

            if (bracket)
            {
                if (ch == ']' && next == ']')
                {
                    index++;
                    continue;
                }
                if (ch == ']') bracket = false;
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
            if (ch == '[')
            {
                bracket = true;
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
