namespace JustyBase.NetezzaSqlParser.Linter;

/// <summary>
/// SQL lint rule framework. Each rule is a pure function that analyzes source SQL text.
/// Port of linterRules.ts from the reference TypeScript project.
/// </summary>

public enum LintSeverity
{
    Error = 0,
    Warning = 1,
    Information = 2,
    Hint = 3
}

public record LintIssue(
    string RuleId,
    string Message,
    LintSeverity Severity,
    int StartOffset,
    int EndOffset,
    int StartLine = 0,
    int StartColumn = 0,
    int EndLine = 0,
    int EndColumn = 0,
    string? SuggestedFix = null
);

public enum RuleSeverityConfig
{
    Error, Warning, Information, Hint, Off
}

/// <summary>
/// Cost category for lint rules — cheap rules run on every keystroke,
/// expensive rules run only when parser + visitor are already needed.
/// </summary>
public enum RuleCost
{
    /// Regex-only rule, fast enough to run on every keystroke.
    Cheap,
    /// Parser or visitor-based rule, only run when parsing is already happening.
    Expensive
}

public abstract class LintRule
{
    public abstract string Id { get; }
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract LintSeverity DefaultSeverity { get; }
    /// <summary>Cost category — Cheap (regex) vs Expensive (parser/visitor).</summary>
    public abstract RuleCost Cost { get; }
    public virtual bool OnDemandOnly => false;

    /// <summary>
    /// Execution priority — higher = more important, runs first.
    /// Defaults based on severity: Error=90, Warning=70, Information=50, Hint=30.
    /// Can be overridden per-rule in QualityRuleRegistry.
    /// </summary>
    public virtual int Priority => DefaultSeverity switch
    {
        LintSeverity.Error => 90,
        LintSeverity.Warning => 70,
        LintSeverity.Information => 50,
        LintSeverity.Hint => 30,
        _ => 50
    };

    public abstract IEnumerable<LintIssue> Check(string sql);

    /// <summary>
    /// For Expensive rules: check against a parsed statement and its visitor errors.
    /// Base implementation returns empty — override in rules that need AST access.
    /// </summary>
    public virtual IEnumerable<LintIssue> CheckStatement(Ast.Statement stmt) => [];
}public static class LintHelpers
{
    /// <summary>Check if a position is inside a string literal or comment.</summary>
    public static bool IsInsideStringOrComment(string sql, int position)
    {
        bool inSingle = false, inDouble = false, inLine = false, inBlock = false;
        for (int i = 0; i < position && i < sql.Length; i++)
        {
            char c = sql[i];
            char next = i + 1 < sql.Length ? sql[i + 1] : '\0';

            if (inLine)
            {
                if (c == '\n') inLine = false;
            }
            else if (inBlock)
            {
                if (c == '*' && next == '/') { inBlock = false; i++; }
            }
            else if (inSingle)
            {
                if (c == '\'' && next == '\'') i++;
                else if (c == '\'') inSingle = false;
            }
            else if (inDouble)
            {
                if (c == '"' && next == '"') i++;
                else if (c == '"') inDouble = false;
            }
            else
            {
                if (c == '-' && next == '-') inLine = true;
                else if (c == '/' && next == '*') inBlock = true;
                else if (c == '\'') inSingle = true;
                else if (c == '"') inDouble = true;
            }
        }
        return inSingle || inDouble || inLine || inBlock;
    }

    /// <summary>Returns true when a position is outside comments, literals, and parentheses.</summary>
    public static bool IsTopLevelSqlPosition(string sql, int position)
    {
        if (IsInsideStringOrComment(sql, position))
            return false;

        var depth = 0;
        var inSingle = false;
        var inDouble = false;
        var inLine = false;
        var inBlock = false;
        for (var i = 0; i < position && i < sql.Length; i++)
        {
            var c = sql[i];
            var next = i + 1 < sql.Length ? sql[i + 1] : '\0';
            if (inLine)
            {
                if (c == '\n') inLine = false;
                continue;
            }
            if (inBlock)
            {
                if (c == '*' && next == '/') { inBlock = false; i++; }
                continue;
            }
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

            if (c == '-' && next == '-') { inLine = true; i++; }
            else if (c == '/' && next == '*') { inBlock = true; i++; }
            else if (c == '\'') inSingle = true;
            else if (c == '"') inDouble = true;
            else if (c == '(') depth++;
            else if (c == ')' && depth > 0) depth--;
        }
        return depth == 0;
    }
} 
