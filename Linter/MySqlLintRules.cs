namespace JustyBase.NetezzaSqlParser.Linter;

/// <summary>MySQL 8 currently has no dialect-specific quality rules.</summary>
public static class MySqlLintRules
{
    public static IReadOnlyList<LintRule> AllRules { get; } = [];
}
