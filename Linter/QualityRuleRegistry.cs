namespace JustyBase.NetezzaSqlParser.Linter;

/// <summary>
/// Registration entry for a single rule in the QualityRuleRegistry.
/// Holds the rule instance and its effective default severity.
/// </summary>
internal sealed record RuleRegistration(LintRule Rule, LintSeverity DefaultSeverity);

/// <summary>
/// Centralized registry for all lint rules with configurable severity per rule.
/// Single source of truth for rule configuration — replaces ad-hoc rule collection
/// in LintEngine and severity-overrides dictionary throughout the codebase.
/// Port of qualityRuleRegistry.ts from the reference TypeScript project.
/// Thread-safe for concurrent reads; writes (severity changes) are lock-protected.
/// </summary>
public sealed class QualityRuleRegistry
{
    private readonly Dictionary<string, RuleRegistration> _rules = new();
    private readonly Dictionary<string, RuleSeverityConfig> _severityOverrides = new();
    private readonly Dictionary<string, int> _priorityOverrides = new();
    private readonly object _lock = new();
    private IReadOnlyList<LintRule>? _cachedCheapRules;
    private IReadOnlyList<LintRule>? _cachedExpensiveRules;
    private IReadOnlyList<LintRule>? _cachedAllRules;
    private IReadOnlyList<(LintRule Rule, int Priority)>? _cachedCheapRulesSorted;
    private IReadOnlyList<(LintRule Rule, int Priority)>? _cachedExpensiveRulesSorted;

    /// <summary>
    /// Create registry with all built-in NZ, NZP, and Expensive rules.
    /// </summary>
    public QualityRuleRegistry()
    {
        foreach (var rule in NzLintRules.AllRules)
            RegisterRuleInternal(rule);
        foreach (var rule in NzLintRulesExtensions.ProcedureRules)
            RegisterRuleInternal(rule);
        foreach (var rule in NzExpensiveRules.AllRules)
            RegisterRuleInternal(rule);

        // Parser-owned structural diagnostics supersede legacy NZ regex rules.
        DisableSupersededRegexRules();
    }

    private void DisableSupersededRegexRules()
    {
        foreach (var ruleId in new[]
        {
            "NZ002", "NZ003", "NZ011", "NZ012", "NZ019", "NZ023", "NZ024"
        })
        {
            SetSeverity(ruleId, RuleSeverityConfig.Off);
        }
    }

    /// <summary>
    /// Create registry with a custom set of rules.
    /// </summary>
    public QualityRuleRegistry(IEnumerable<LintRule> rules)
    {
        foreach (var rule in rules)
            RegisterRuleInternal(rule);
    }

    private void RegisterRuleInternal(LintRule rule)
    {
        _rules[rule.Id] = new RuleRegistration(rule, rule.DefaultSeverity);
    }

    /// <summary>
    /// All registered rules, regardless of severity or enabled status.
    /// </summary>
    public IReadOnlyList<LintRule> AllRules
    {
        get
        {
            var cached = _cachedAllRules;
            if (cached is null)
            {
                cached = _rules.Values.Select(r => r.Rule).ToList();
                _cachedAllRules = cached;
            }
            return cached;
        }
    }

    /// <summary>
    /// Cheap (regex-only) rules that are not set to Off.
    /// </summary>
    public IReadOnlyList<LintRule> CheapRules
    {
        get
        {
            var cached = _cachedCheapRules;
            if (cached is null)
            {
                cached = _rules.Values
                    .Where(r => r.Rule.Cost == RuleCost.Cheap && GetEffectiveSeverity(r.Rule.Id) != RuleSeverityConfig.Off)
                    .Select(r => r.Rule)
                    .ToList();
                _cachedCheapRules = cached;
            }
            return cached;
        }
    }

    /// <summary>
    /// Expensive (parser+visitor) rules that are not set to Off.
    /// </summary>
    public IReadOnlyList<LintRule> ExpensiveRules
    {
        get
        {
            var cached = _cachedExpensiveRules;
            if (cached is null)
            {
                cached = _rules.Values
                    .Where(r => r.Rule.Cost == RuleCost.Expensive && GetEffectiveSeverity(r.Rule.Id) != RuleSeverityConfig.Off)
                    .Select(r => r.Rule)
                    .ToList();
                _cachedExpensiveRules = cached;
            }
            return cached;
        }
    }

    /// <summary>
    /// Get the effective severity for a rule, considering user overrides and rule defaults.
    /// Returns Warning as fallback if rule is not found.
    /// </summary>
    public RuleSeverityConfig GetEffectiveSeverity(string ruleId)
    {
        lock (_lock)
        {
            if (_severityOverrides.TryGetValue(ruleId, out var overrideSev))
                return overrideSev;
        }

        if (_rules.TryGetValue(ruleId, out var reg))
            return MapSeverity(reg.DefaultSeverity);

        return RuleSeverityConfig.Warning;
    }

    /// <summary>
    /// Set a severity override for a specific rule.
    /// Pass RuleSeverityConfig.Off to disable the rule.
    /// To re-enable a disabled rule, call SetSeverity with its default severity value
    /// or call ResetSeverities() to clear all overrides.
    /// </summary>
    public void SetSeverity(string ruleId, RuleSeverityConfig severity)
    {
        lock (_lock)
        {
            _severityOverrides[ruleId] = severity;
        }
        InvalidateSeverityCache();
    }

    /// <summary>
    /// Set multiple severity overrides at once.
    /// </summary>
    public void SetSeverities(IEnumerable<KeyValuePair<string, RuleSeverityConfig>> overrides)
    {
        lock (_lock)
        {
            foreach (var kvp in overrides)
            {
                _severityOverrides[kvp.Key] = kvp.Value;
            }
        }
        InvalidateSeverityCache();
    }

    /// <summary>
    /// Remove all severity overrides — all rules use their default severity.
    /// </summary>
    public void ResetSeverities()
    {
        lock (_lock)
        {
            _severityOverrides.Clear();
        }
        DisableSupersededRegexRules();
        InvalidateSeverityCache();
    }

    /// <summary>
    /// Get a snapshot of all current severity overrides.
    /// </summary>
    public IReadOnlyDictionary<string, RuleSeverityConfig> GetSeverityOverrides()
    {
        lock (_lock)
        {
            return new Dictionary<string, RuleSeverityConfig>(_severityOverrides);
        }
    }

    /// <summary>
    /// Get the effective priority for a rule, considering user overrides and rule defaults.
    /// Returns the rule's built-in priority as fallback if rule is not found.
    /// </summary>
    public int GetEffectivePriority(string ruleId)
    {
        lock (_lock)
        {
            if (_priorityOverrides.TryGetValue(ruleId, out var overridePri))
                return overridePri;
        }

        return _rules.TryGetValue(ruleId, out var reg) ? reg.Rule.Priority : 50;
    }

    /// <summary>
    /// Set a priority override for a specific rule.
    /// Higher priority = more important, runs first.
    /// </summary>
    public void SetPriority(string ruleId, int priority)
    {
        lock (_lock)
        {
            _priorityOverrides[ruleId] = priority;
        }
        InvalidatePriorityCache();
    }

    /// <summary>
    /// Reset all priority overrides — all rules use their built-in priority.
    /// </summary>
    public void ResetPriorities()
    {
        lock (_lock)
        {
            _priorityOverrides.Clear();
        }
        InvalidatePriorityCache();
    }

    /// <summary>
    /// Get priority-sorted cheap rules: (Rule, EffectivePriority) tuples sorted by priority descending.
    /// Cached — invalidated when severity or priority overrides change.
    /// </summary>
    public IReadOnlyList<(LintRule Rule, int Priority)> CheapRulesSorted
    {
        get
        {
            var cached = _cachedCheapRulesSorted;
            if (cached is null)
            {
                cached = _rules.Values
                    .Where(r => r.Rule.Cost == RuleCost.Cheap && GetEffectiveSeverity(r.Rule.Id) != RuleSeverityConfig.Off)
                    .Select(r => (Rule: r.Rule, Priority: GetEffectivePriority(r.Rule.Id)))
                    .OrderByDescending(x => x.Priority)
                    .ThenBy(x => x.Rule.Id)
                    .ToList();
                _cachedCheapRulesSorted = cached;
            }
            return cached;
        }
    }

    /// <summary>
    /// Get priority-sorted expensive rules: (Rule, EffectivePriority) tuples sorted by priority descending.
    /// Cached — invalidated when severity or priority overrides change.
    /// </summary>
    public IReadOnlyList<(LintRule Rule, int Priority)> ExpensiveRulesSorted
    {
        get
        {
            var cached = _cachedExpensiveRulesSorted;
            if (cached is null)
            {
                cached = _rules.Values
                    .Where(r => r.Rule.Cost == RuleCost.Expensive && GetEffectiveSeverity(r.Rule.Id) != RuleSeverityConfig.Off)
                    .Select(r => (Rule: r.Rule, Priority: GetEffectivePriority(r.Rule.Id)))
                    .OrderByDescending(x => x.Priority)
                    .ThenBy(x => x.Rule.Id)
                    .ToList();
                _cachedExpensiveRulesSorted = cached;
            }
            return cached;
        }
    }

    /// <summary>
    /// Check if a rule is registered.
    /// </summary>
    public bool HasRule(string ruleId) => _rules.ContainsKey(ruleId);

    /// <summary>
    /// Try to get a rule by Id.
    /// </summary>
    public LintRule? GetRule(string ruleId)
    {
        return _rules.TryGetValue(ruleId, out var reg) ? reg.Rule : null;
    }

    /// <summary>
    /// Build a severity overrides dictionary from the current registry state,
    /// optionally merged with per-call overrides.
    /// Copies overrides once under the lock, then resolves all rules outside the lock.
    /// </summary>
    public IDictionary<string, RuleSeverityConfig> BuildEffectiveSeverities(
        IDictionary<string, RuleSeverityConfig>? perCallOverrides = null)
    {
        // Snapshot overrides once under the lock
        Dictionary<string, RuleSeverityConfig> overrides;
        lock (_lock)
        {
            overrides = new Dictionary<string, RuleSeverityConfig>(_severityOverrides);
        }

        var result = new Dictionary<string, RuleSeverityConfig>();
        foreach (var (ruleId, reg) in _rules)
        {
            result[ruleId] = overrides.TryGetValue(ruleId, out var v) ? v : MapSeverity(reg.DefaultSeverity);
        }

        if (perCallOverrides is not null)
        {
            foreach (var kvp in perCallOverrides)
            {
                result[kvp.Key] = kvp.Value;
            }
        }
        return result;
    }

    /// <summary>
    /// Invalidate cache entries that depend on severity overrides.
    /// Called when severity overrides change.
    /// </summary>
    private void InvalidateSeverityCache()
    {
        _cachedCheapRules = null;
        _cachedExpensiveRules = null;
        _cachedCheapRulesSorted = null;
        _cachedExpensiveRulesSorted = null;
    }

    /// <summary>
    /// Invalidate cache entries that depend on priority overrides.
    /// Called when priority overrides change.
    /// </summary>
    private void InvalidatePriorityCache()
    {
        _cachedCheapRulesSorted = null;
        _cachedExpensiveRulesSorted = null;
    }

    private static RuleSeverityConfig MapSeverity(LintSeverity sev) => sev switch
    {
        LintSeverity.Error => RuleSeverityConfig.Error,
        LintSeverity.Warning => RuleSeverityConfig.Warning,
        LintSeverity.Information => RuleSeverityConfig.Information,
        LintSeverity.Hint => RuleSeverityConfig.Hint,
        _ => RuleSeverityConfig.Warning
    };
}
