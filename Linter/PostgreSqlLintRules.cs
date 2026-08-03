namespace JustyBase.NetezzaSqlParser.Linter;

/// <summary>PostgreSQL intentionally has no dialect-specific lint rules yet.</summary>
public static class PostgreSqlLintRules
{
    public static IReadOnlyList<LintRule> AllRules { get; } = [];
}
