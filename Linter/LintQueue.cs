namespace JustyBase.NetezzaSqlParser.Linter;

/// <summary>
/// Priority queue that returns lint rules sorted by effective priority (descending).
/// Wraps QualityRuleRegistry and provides sorted access to cheap and expensive rules.
/// Higher priority rules run first, ensuring the most important issues surface earliest
/// when lint time is limited (e.g., during keystroke-triggered analysis).
/// </summary>
public sealed class LintQueue
{
    private readonly QualityRuleRegistry _registry;

    /// <summary>
    /// Create a LintQueue backed by the given registry.
    /// </summary>
    public LintQueue(QualityRuleRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <summary>
    /// Cheap rules sorted by effective priority descending, then by rule ID.
    /// Rules set to Off are excluded.
    /// </summary>
    public IReadOnlyList<(LintRule Rule, int Priority)> CheapRules => _registry.CheapRulesSorted;

    /// <summary>
    /// Expensive rules sorted by effective priority descending, then by rule ID.
    /// Rules set to Off are excluded.
    /// </summary>
    public IReadOnlyList<(LintRule Rule, int Priority)> ExpensiveRules => _registry.ExpensiveRulesSorted;

    /// <summary>
    /// Get the effective priority for a rule, considering any overrides in the registry.
    /// </summary>
    public int GetEffectivePriority(string ruleId) => _registry.GetEffectivePriority(ruleId);

    /// <summary>
    /// Get the underlying QualityRuleRegistry.
    /// </summary>
    public QualityRuleRegistry Registry => _registry;
}
