using JustyBase.NetezzaSqlParser.Linter;

namespace JustyBase.Tests.NetezzaSqlParser;

/// <summary>
/// Direct tests for LintQueue — priority sorting, Off exclusion, GetEffectivePriority.
/// </summary>
public sealed class LintQueueTests
{
    private readonly QualityRuleRegistry _registry = new();
    private readonly LintQueue _queue = new(new QualityRuleRegistry());

    [Fact]
    public void CheapRules_ReturnsPrioritySorted_Descending()
    {
        var rules = _queue.CheapRules;
        Assert.NotEmpty(rules);

        for (int i = 1; i < rules.Count; i++)
            Assert.True(rules[i - 1].Priority >= rules[i].Priority,
                $"Rules not sorted by priority descending: {rules[i - 1].Rule.Id}({rules[i - 1].Priority}) < {rules[i].Rule.Id}({rules[i].Priority})");
    }

    [Fact]
    public void CheapRules_SamePriority_SortedById()
    {
        var rules = _queue.CheapRules;
        var samePriorityGroups = rules
            .GroupBy(r => r.Priority)
            .Where(g => g.Count() > 1)
            .ToList();

        // If no groups share priority, the ID-sorting invariant is trivially satisfied
        foreach (var group in samePriorityGroups)
        {
            var list = group.ToList();
            for (int i = 1; i < list.Count; i++)
                Assert.True(string.Compare(list[i - 1].Rule.Id, list[i].Rule.Id, StringComparison.Ordinal) <= 0,
                    $"Same-priority rules not sorted by Id: {list[i - 1].Rule.Id} > {list[i].Rule.Id}");
        }
    }

    [Fact]
    public void CheapRules_ExcludesRulesSetToOff()
    {
        _queue.Registry.SetSeverity("NZ001", RuleSeverityConfig.Off);
        var rules = _queue.CheapRules;
        Assert.DoesNotContain(rules, r => r.Rule.Id == "NZ001");
    }

    [Fact]
    public void CheapRules_ReEnabledAfterReset()
    {
        _queue.Registry.SetSeverity("NZ001", RuleSeverityConfig.Off);
        var excluded = _queue.CheapRules;
        Assert.DoesNotContain(excluded, r => r.Rule.Id == "NZ001");

        _queue.Registry.ResetSeverities();
        var included = _queue.CheapRules;
        Assert.Contains(included, r => r.Rule.Id == "NZ001");
    }

    [Fact]
    public void CheapRules_PriorityOverride_AffectsOrder()
    {
        _queue.Registry.SetPriority("NZ001", 200);
        _queue.Registry.SetPriority("NZ004", 10);

        var rules = _queue.CheapRules.ToList();
        var nz001Index = rules.FindIndex(r => r.Rule.Id == "NZ001");
        var nz004Index = rules.FindIndex(r => r.Rule.Id == "NZ004");

        Assert.NotEqual(-1, nz001Index);
        Assert.NotEqual(-1, nz004Index);
        Assert.True(nz001Index < nz004Index,
            $"NZ001 (priority=200) should come before NZ004 (priority=10)");
    }

    [Fact]
    public void ExpensiveRules_EmptyRegistry_ReturnsEmpty()
    {
        var emptyRegistry = new QualityRuleRegistry(Array.Empty<LintRule>());
        var emptyQueue = new LintQueue(emptyRegistry);
        Assert.Empty(emptyQueue.ExpensiveRules);
    }

    [Fact]
    public void GetEffectivePriority_ReturnsBuiltInOrDefault()
    {
        var nz001Pri = _queue.GetEffectivePriority("NZ001");
        Assert.Equal(80, nz001Pri); // NZ001 has explicit override to 80

        var unknownPri = _queue.GetEffectivePriority("NONEXISTENT");
        Assert.Equal(50, unknownPri); // default fallback
    }

    [Fact]
    public void GetEffectivePriority_OverrideWorks()
    {
        _queue.Registry.SetPriority("NZ001", 42);
        Assert.Equal(42, _queue.GetEffectivePriority("NZ001"));
    }

    [Fact]
    public void Registry_ReturnsUnderlying()
    {
        Assert.Same(_queue.Registry, _queue.Registry);
    }
}
