using System.Text.RegularExpressions;

namespace JustyBase.NetezzaSqlParser.Linter;

/// <summary>
/// SQLite dialect lint rules (SQLITE001–SQLITE006). Registry for SQLite
/// documents contains only these rules.
/// </summary>
public static class SqliteLintRules
{
    public static IReadOnlyList<LintRule> AllRules { get; } =
    [
        new RuleSqlite001_AutoincrementRequiresIntegerPk(),
        new RuleSqlite002_WithoutRowidRequiresPrimaryKey(),
        new RuleSqlite003_CreateSequence(),
        new RuleSqlite004_NonSqliteFunctions(),
        new RuleSqlite005_AlterTableAddConstraint(),
        new RuleSqlite006_StrictRequiresModernVersion(),
    ];
}

public sealed class RuleSqlite001_AutoincrementRequiresIntegerPk : LintRule
{
    public override string Id => "SQLITE001";
    public override string Name => "AUTOINCREMENT Requires INTEGER PRIMARY KEY";
    public override string Description =>
        "AUTOINCREMENT may only follow an INTEGER PRIMARY KEY column declaration; use plain PRIMARY KEY to reuse deleted rowids.";
    public override LintSeverity DefaultSeverity => LintSeverity.Error;
    public override RuleCost Cost => RuleCost.Cheap;

    public override IEnumerable<LintIssue> Check(string sql)
    {
        foreach (Match m in Regex.Matches(sql, @"\bAUTOINCREMENT\b", RegexOptions.IgnoreCase))
        {
            if (LintHelpers.IsInsideStringOrComment(sql, m.Index)) continue;
            // Scope the check to the enclosing column definition: AUTOINCREMENT
            // is only valid directly after an INTEGER PRIMARY KEY column.
            var columnStart = sql.LastIndexOfAny(['(', ','], m.Index) + 1;
            var column = sql[columnStart..m.Index];
            if (!Regex.IsMatch(column, @"\bINTEGER\s+PRIMARY\s+KEY\b", RegexOptions.IgnoreCase))
                yield return new LintIssue(Id, $"{Id}: {Description}", DefaultSeverity, m.Index, m.Index + m.Length);
        }
    }
}

public sealed class RuleSqlite002_WithoutRowidRequiresPrimaryKey : LintRule
{
    public override string Id => "SQLITE002";
    public override string Name => "WITHOUT ROWID Requires PRIMARY KEY";
    public override string Description =>
        "A WITHOUT ROWID table must declare a PRIMARY KEY; the primary key becomes the clustered b-tree key.";
    public override LintSeverity DefaultSeverity => LintSeverity.Error;
    public override RuleCost Cost => RuleCost.Cheap;

    public override IEnumerable<LintIssue> Check(string sql)
    {
        foreach (Match m in Regex.Matches(sql, @"\bWITHOUT\s+ROWID\b", RegexOptions.IgnoreCase))
        {
            if (LintHelpers.IsInsideStringOrComment(sql, m.Index)) continue;
            var statement = SqliteLintHelpers.StatementText(sql, m.Index);
            if (!Regex.IsMatch(statement, @"\bPRIMARY\s+KEY\b", RegexOptions.IgnoreCase))
                yield return new LintIssue(Id, $"{Id}: {Description}", DefaultSeverity, m.Index, m.Index + m.Length);
        }
    }
}

public sealed class RuleSqlite003_CreateSequence : LintRule
{
    public override string Id => "SQLITE003";
    public override string Name => "SQLite Has No Sequences";
    public override string Description =>
        "SQLite has no CREATE SEQUENCE or SERIAL columns; declare the column as INTEGER PRIMARY KEY (AUTOINCREMENT) instead.";
    public override LintSeverity DefaultSeverity => LintSeverity.Error;
    public override RuleCost Cost => RuleCost.Cheap;

    public override IEnumerable<LintIssue> Check(string sql)
    {
        foreach (Match m in Regex.Matches(sql, @"\b(?:CREATE\s+SEQUENCE|SERIAL)\b", RegexOptions.IgnoreCase))
        {
            if (LintHelpers.IsInsideStringOrComment(sql, m.Index)) continue;
            yield return new LintIssue(Id, $"{Id}: {Description}", DefaultSeverity, m.Index, m.Index + m.Length);
        }
    }
}

public sealed class RuleSqlite004_NonSqliteFunctions : LintRule
{
    public override string Id => "SQLITE004";
    public override string Name => "Non-SQLite Function";
    public override string Description =>
        "{0} is not a SQLite built-in; use IFNULL, datetime('now') or STRFTIME instead.";
    public override LintSeverity DefaultSeverity => LintSeverity.Warning;
    public override RuleCost Cost => RuleCost.Cheap;

    public override IEnumerable<LintIssue> Check(string sql)
    {
        foreach (Match m in Regex.Matches(sql, @"\b(?:NVL|TO_CHAR|TO_DATE)\s*\(|\bSYSDATE\b", RegexOptions.IgnoreCase))
        {
            if (LintHelpers.IsInsideStringOrComment(sql, m.Index)) continue;
            var word = m.Value.TrimStart().StartsWith("SYSDATE", StringComparison.OrdinalIgnoreCase)
                ? "SYSDATE"
                : m.Value.Trim();
            var message = string.Format(Description, word);
            yield return new LintIssue(Id, $"{Id}: {message}", DefaultSeverity, m.Index, m.Index + m.Length);
        }
    }
}

public sealed class RuleSqlite005_AlterTableAddConstraint : LintRule
{
    public override string Id => "SQLITE005";
    public override string Name => "ALTER TABLE ADD CONSTRAINT Unsupported";
    public override string Description =>
        "SQLite cannot add constraints to existing tables (except NOT NULL with a default); recreate the table instead.";
    public override LintSeverity DefaultSeverity => LintSeverity.Error;
    public override RuleCost Cost => RuleCost.Cheap;

    public override IEnumerable<LintIssue> Check(string sql)
    {
        foreach (Match m in Regex.Matches(sql, @"\bALTER\s+TABLE\b[^;]*?\bADD\s+(?:CONSTRAINT|PRIMARY\s+KEY|FOREIGN\s+KEY|UNIQUE|CHECK)\b",
            RegexOptions.IgnoreCase))
        {
            if (LintHelpers.IsInsideStringOrComment(sql, m.Index)) continue;
            yield return new LintIssue(Id, $"{Id}: {Description}", DefaultSeverity, m.Index, m.Index + m.Length);
        }
    }
}

public sealed class RuleSqlite006_StrictRequiresModernVersion : LintRule
{
    public override string Id => "SQLITE006";
    public override string Name => "STRICT Requires SQLite 3.37+";
    public override string Description =>
        "STRICT tables require SQLite 3.37.0 or newer; older engines reject the table definition.";
    public override LintSeverity DefaultSeverity => LintSeverity.Warning;
    public override RuleCost Cost => RuleCost.Cheap;

    public override IEnumerable<LintIssue> Check(string sql)
    {
        foreach (Match m in Regex.Matches(sql, @"\bSTRICT\b", RegexOptions.IgnoreCase))
        {
            if (LintHelpers.IsInsideStringOrComment(sql, m.Index)) continue;
            yield return new LintIssue(Id, $"{Id}: {Description}", DefaultSeverity, m.Index, m.Index + m.Length);
        }
    }
}

internal static class SqliteLintHelpers
{
    /// <summary>Returns the current statement text (from the previous ';' to the end).</summary>
    public static string StatementText(string sql, int index)
    {
        var start = sql.LastIndexOf(';', Math.Max(0, index - 1)) + 1;
        var end = sql.IndexOf(';', index);
        return end < 0 ? sql[start..] : sql[start..end];
    }
}
