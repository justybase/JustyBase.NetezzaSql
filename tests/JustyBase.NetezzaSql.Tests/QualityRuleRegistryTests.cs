using JustyBase.NetezzaSqlParser.Linter;

namespace JustyBase.Tests.NetezzaSqlParser;

// ========================================================================
// QualityRuleRegistry Tests
// ========================================================================

public sealed class QualityRuleRegistryTests
{
    // ====================================================================
    // Constructor
    // ====================================================================

    [Fact]
    public void DefaultConstructor_LoadsAllNzAndNzpRules()
    {
        var registry = new QualityRuleRegistry();
        Assert.NotEmpty(registry.AllRules);
        // Should have NZ rules (NZ001-NZ024)
        Assert.Contains(registry.AllRules, r => r.Id == "NZ001");
        Assert.Contains(registry.AllRules, r => r.Id == "NZ022");
        Assert.Contains(registry.AllRules, r => r.Id == "NZ024");
        // Should have NZP rules (NZP001-NZP030)
        Assert.Contains(registry.AllRules, r => r.Id == "NZP001");
        Assert.Contains(registry.AllRules, r => r.Id == "NZP030");
        // Should have Expensive rules (NZ101-NZ105)
        Assert.Contains(registry.AllRules, r => r.Id == "NZ101");
        Assert.Contains(registry.AllRules, r => r.Id == "NZ105");
        // Not all rules are Cheap now; 8 are Expensive and 7 legacy regex rules are disabled.
        Assert.Equal(registry.AllRules.Count - registry.ExpensiveRules.Count - 7, registry.CheapRules.Count);
        Assert.Equal(8, registry.ExpensiveRules.Count);
    }

    [Fact]
    public void CustomConstructor_WithSingleRule()
    {
        var registry = new QualityRuleRegistry(new[] { new RuleNZ001_SelectStar() });
        Assert.Single(registry.AllRules);
        Assert.Equal("NZ001", registry.AllRules[0].Id);
    }

    [Fact]
    public void CustomConstructor_WithEmptyRules()
    {
        var registry = new QualityRuleRegistry(Array.Empty<LintRule>());
        Assert.Empty(registry.AllRules);
        Assert.Empty(registry.CheapRules);
        Assert.Empty(registry.ExpensiveRules);
    }

    [Fact]
    public void CustomConstructor_DuplicateId_LastWins()
    {
        var registry = new QualityRuleRegistry(new LintRule[]
        {
            new RuleNZ001_SelectStar(),
            new RuleNZ001_SelectStar()  // duplicate - last wins
        });
        Assert.Single(registry.AllRules);
    }

    // ====================================================================
    // HasRule / GetRule
    // ====================================================================

    [Fact]
    public void HasRule_ExistingRule_ReturnsTrue()
    {
        var registry = new QualityRuleRegistry();
        Assert.True(registry.HasRule("NZ001"));
        Assert.True(registry.HasRule("NZP001"));
    }

    [Fact]
    public void HasRule_NonExistingRule_ReturnsFalse()
    {
        var registry = new QualityRuleRegistry();
        Assert.False(registry.HasRule("NONEXISTENT"));
    }

    [Fact]
    public void GetRule_ExistingRule_ReturnsRule()
    {
        var registry = new QualityRuleRegistry();
        var rule = registry.GetRule("NZ001");
        Assert.NotNull(rule);
        Assert.Equal("NZ001", rule!.Id);
        Assert.Equal("Select Star", rule.Name);
    }

    [Fact]
    public void GetRule_NonExistingRule_ReturnsNull()
    {
        var registry = new QualityRuleRegistry();
        Assert.Null(registry.GetRule("NONEXISTENT"));
    }

    // ====================================================================
    // GetEffectiveSeverity
    // ====================================================================

    [Fact]
    public void GetEffectiveSeverity_NoOverride_ReturnsDefaultSeverity()
    {
        var registry = new QualityRuleRegistry();
        // NZ001 default is Warning
        var sev = registry.GetEffectiveSeverity("NZ001");
        Assert.Equal(RuleSeverityConfig.Warning, sev);
    }

    [Fact]
    public void GetEffectiveSeverity_RuleWithErrorDefault()
    {
        var registry = new QualityRuleRegistry();
        // NZ002 is superseded by SQL043 and disabled by default
        var sev = registry.GetEffectiveSeverity("NZ002");
        Assert.Equal(RuleSeverityConfig.Off, sev);
    }

    [Fact]
    public void GetEffectiveSeverity_RuleWithHintDefault()
    {
        var registry = new QualityRuleRegistry();
        // NZ005 default is Hint
        var sev = registry.GetEffectiveSeverity("NZ005");
        Assert.Equal(RuleSeverityConfig.Hint, sev);
    }

    [Fact]
    public void GetEffectiveSeverity_RuleWithInfoDefault()
    {
        var registry = new QualityRuleRegistry();
        // NZ006 default is Information
        var sev = registry.GetEffectiveSeverity("NZ006");
        Assert.Equal(RuleSeverityConfig.Information, sev);
    }

    [Fact]
    public void GetEffectiveSeverity_AfterOverride_ReturnsOverride()
    {
        var registry = new QualityRuleRegistry();
        registry.SetSeverity("NZ001", RuleSeverityConfig.Error);
        var sev = registry.GetEffectiveSeverity("NZ001");
        Assert.Equal(RuleSeverityConfig.Error, sev);
    }

    [Fact]
    public void GetEffectiveSeverity_AfterOverrideToHint()
    {
        var registry = new QualityRuleRegistry();
        registry.SetSeverity("NZ001", RuleSeverityConfig.Hint);
        var sev = registry.GetEffectiveSeverity("NZ001");
        Assert.Equal(RuleSeverityConfig.Hint, sev);
    }        [Fact]
        public void GetEffectiveSeverity_AfterOverrideToOff_ReturnsOff()
        {
            var registry = new QualityRuleRegistry();
            registry.SetSeverity("NZ001", RuleSeverityConfig.Off);
            var sev = registry.GetEffectiveSeverity("NZ001");
            Assert.Equal(RuleSeverityConfig.Off, sev);
        }

        [Fact]
        public void SetSeverity_Off_ReEnableBySettingDefault()
        {
            var registry = new QualityRuleRegistry();
            registry.SetSeverity("NZ001", RuleSeverityConfig.Off);
            Assert.Equal(RuleSeverityConfig.Off, registry.GetEffectiveSeverity("NZ001"));

            // To re-enable, set it back to its default severity
            registry.SetSeverity("NZ001", RuleSeverityConfig.Warning);
            Assert.Equal(RuleSeverityConfig.Warning, registry.GetEffectiveSeverity("NZ001"));
        }

    [Fact]
    public void GetEffectiveSeverity_NonExistingRule_ReturnsWarning()
    {
        var registry = new QualityRuleRegistry();
        var sev = registry.GetEffectiveSeverity("NONEXISTENT");
        Assert.Equal(RuleSeverityConfig.Warning, sev);
    }

    // ====================================================================
    // SetSeverity
    // ====================================================================

    [Fact]
    public void SetSeverity_ChangesEffectiveSeverity()
    {
        var registry = new QualityRuleRegistry();
        registry.SetSeverity("NZ001", RuleSeverityConfig.Error);
        Assert.Equal(RuleSeverityConfig.Error, registry.GetEffectiveSeverity("NZ001"));

        registry.SetSeverity("NZ001", RuleSeverityConfig.Hint);
        Assert.Equal(RuleSeverityConfig.Hint, registry.GetEffectiveSeverity("NZ001"));
    }        [Fact]
        public void SetSeverity_Off_DisablesRule()
        {
            var registry = new QualityRuleRegistry();
            registry.SetSeverity("NZ001", RuleSeverityConfig.Error);
            Assert.Equal(RuleSeverityConfig.Error, registry.GetEffectiveSeverity("NZ001"));

            // Setting to Off stores the override as Off — the rule is now disabled
            registry.SetSeverity("NZ001", RuleSeverityConfig.Off);
            Assert.Equal(RuleSeverityConfig.Off, registry.GetEffectiveSeverity("NZ001"));
        }

    [Fact]
    public void SetSeverity_OffOnDefault_DoesNotChangeDefault()
    {
        var registry = new QualityRuleRegistry();
        registry.SetSeverity("NZ001", RuleSeverityConfig.Off);
        // After setting to Off and then overriding again, it should work
        registry.SetSeverity("NZ001", RuleSeverityConfig.Error);
        Assert.Equal(RuleSeverityConfig.Error, registry.GetEffectiveSeverity("NZ001"));
    }    [Fact]
        public void SetSeverity_NonExistingRule_StoresOverride()
        {
            var registry = new QualityRuleRegistry();
            registry.SetSeverity("NONEXISTENT", RuleSeverityConfig.Error);
            // Non-existing rule: override is stored, GetEffectiveSeverity returns it
            Assert.Equal(RuleSeverityConfig.Error, registry.GetEffectiveSeverity("NONEXISTENT"));
        }

    // ====================================================================
    // SetSeverities (bulk)
    // ====================================================================

    [Fact]
    public void SetSeverities_AppliesMultipleOverrides()
    {
        var registry = new QualityRuleRegistry();
        registry.SetSeverities(new[]
        {
            new KeyValuePair<string, RuleSeverityConfig>("NZ001", RuleSeverityConfig.Error),
            new KeyValuePair<string, RuleSeverityConfig>("NZ002", RuleSeverityConfig.Warning),
            new KeyValuePair<string, RuleSeverityConfig>("NZ003", RuleSeverityConfig.Off),
        });

        Assert.Equal(RuleSeverityConfig.Error, registry.GetEffectiveSeverity("NZ001"));
        Assert.Equal(RuleSeverityConfig.Warning, registry.GetEffectiveSeverity("NZ002"));
        Assert.Equal(RuleSeverityConfig.Off, registry.GetEffectiveSeverity("NZ003"));
    }

    [Fact]
    public void SetSeverities_OffDisablesRule()
    {
        var registry = new QualityRuleRegistry();
        // First set an override
        registry.SetSeverity("NZ001", RuleSeverityConfig.Error);

        // Then bulk-set it to Off (should disable the rule)
        registry.SetSeverities(new[]
        {
            new KeyValuePair<string, RuleSeverityConfig>("NZ001", RuleSeverityConfig.Off)
        });

        Assert.Equal(RuleSeverityConfig.Off, registry.GetEffectiveSeverity("NZ001"));
    }

    // ====================================================================
    // ResetSeverities
    // ====================================================================

    [Fact]
    public void ResetSeverities_ClearsAllOverrides()
    {
        var registry = new QualityRuleRegistry();
        registry.SetSeverity("NZ001", RuleSeverityConfig.Error);
        registry.SetSeverity("NZ002", RuleSeverityConfig.Off);

        registry.ResetSeverities();

        Assert.Equal(RuleSeverityConfig.Warning, registry.GetEffectiveSeverity("NZ001")); // back to default
        Assert.Equal(RuleSeverityConfig.Off, registry.GetEffectiveSeverity("NZ002")); // superseded parser rule
    }

    [Fact]
    public void ResetSeverities_EmptyRegistry_DoesNotThrow()
    {
        var registry = new QualityRuleRegistry(Array.Empty<LintRule>());
        registry.ResetSeverities(); // should not throw
    }

    // ====================================================================
    // GetSeverityOverrides
    // ====================================================================

    [Fact]
    public void GetSeverityOverrides_IncludesSupersededRuleDefaults()
    {
        var registry = new QualityRuleRegistry();
        var overrides = registry.GetSeverityOverrides();
        Assert.Equal(RuleSeverityConfig.Off, overrides["NZ002"]);
        Assert.Equal(RuleSeverityConfig.Off, overrides["NZ003"]);
    }

    [Fact]
    public void GetSeverityOverrides_WithOverrides_ReturnsSnapshot()
    {
        var registry = new QualityRuleRegistry();
        registry.SetSeverity("NZ001", RuleSeverityConfig.Error);

        var overrides = registry.GetSeverityOverrides();
        Assert.True(overrides.Count >= 8);
        Assert.Equal(RuleSeverityConfig.Error, overrides["NZ001"]);
        Assert.Equal(RuleSeverityConfig.Off, overrides["NZ002"]);
    }

    [Fact]
    public void GetSeverityOverrides_ReturnsCopy_NotReference()
    {
        var registry = new QualityRuleRegistry();
        registry.SetSeverity("NZ001", RuleSeverityConfig.Error);

        var overrides = registry.GetSeverityOverrides();
        registry.SetSeverity("NZ001", RuleSeverityConfig.Hint);

        // The snapshot should still show the old value
        Assert.Equal(RuleSeverityConfig.Error, overrides["NZ001"]);
    }

    // ====================================================================
    // BuildEffectiveSeverities
    // ====================================================================

    [Fact]
    public void BuildEffectiveSeverities_NoOverrides_ReturnsDefaults()
    {
        var registry = new QualityRuleRegistry();
        var result = registry.BuildEffectiveSeverities();

        Assert.True(result.Count >= 48); // 22 NZ + 26 NZP
        Assert.Equal(RuleSeverityConfig.Warning, result["NZ001"]); // default
        Assert.Equal(RuleSeverityConfig.Off, result["NZ002"]); // superseded by SQL043
    }

    [Fact]
    public void BuildEffectiveSeverities_WithOverrides()
    {
        var registry = new QualityRuleRegistry();
        registry.SetSeverity("NZ001", RuleSeverityConfig.Error);

        var result = registry.BuildEffectiveSeverities();
        Assert.Equal(RuleSeverityConfig.Error, result["NZ001"]);
    }

    [Fact]
    public void BuildEffectiveSeverities_WithPerCallOverrides()
    {
        var registry = new QualityRuleRegistry();
        // Registry says Error for NZ001, but per-call says Hint
        registry.SetSeverity("NZ001", RuleSeverityConfig.Error);

        var perCall = new Dictionary<string, RuleSeverityConfig>
        {
            ["NZ001"] = RuleSeverityConfig.Hint
        };
        var result = registry.BuildEffectiveSeverities(perCall);

        // Per-call overrides registry
        Assert.Equal(RuleSeverityConfig.Hint, result["NZ001"]);
    }

    [Fact]
    public void BuildEffectiveSeverities_WithPerCallOff()
    {
        var registry = new QualityRuleRegistry();
        var perCall = new Dictionary<string, RuleSeverityConfig>
        {
            ["NZ001"] = RuleSeverityConfig.Off
        };
        var result = registry.BuildEffectiveSeverities(perCall);
        Assert.Equal(RuleSeverityConfig.Off, result["NZ001"]);
    }

    // ====================================================================
    // CheapRules / ExpensiveRules with severity overrides
    // ====================================================================

    [Fact]
    public void CheapRules_ExcludesRulesSetToOff()
    {
        var registry = new QualityRuleRegistry();
        var countBefore = registry.CheapRules.Count;

        // Disable one rule
        registry.SetSeverity("NZ001", RuleSeverityConfig.Off);

        // CheapRules should now exclude NZ001
        Assert.Equal(countBefore - 1, registry.CheapRules.Count);
        Assert.DoesNotContain(registry.CheapRules, r => r.Id == "NZ001");
    }

    [Fact]
    public void CheapRules_IncludesRulesWithSeverityOverride()
    {
        var registry = new QualityRuleRegistry();
        registry.SetSeverity("NZ001", RuleSeverityConfig.Error); // Change severity, not disable

        // Should still be included
        Assert.Contains(registry.CheapRules, r => r.Id == "NZ001");
    }

    [Fact]
    public void CheapRules_ReEnabledAfterOff()
    {
        var registry = new QualityRuleRegistry();
        registry.SetSeverity("NZ001", RuleSeverityConfig.Off);
        Assert.DoesNotContain(registry.CheapRules, r => r.Id == "NZ001");

        // Re-enable by removing the override
        registry.SetSeverity("NZ001", RuleSeverityConfig.Warning);
        Assert.Contains(registry.CheapRules, r => r.Id == "NZ001");
    }

    [Fact]
    public void CheapRules_EmptyRegistry_ReturnsEmpty()
    {
        var registry = new QualityRuleRegistry(Array.Empty<LintRule>());
        Assert.Empty(registry.CheapRules);
    }

    [Fact]
    public void ExpensiveRules_NotEmptyWithExpensiveRules()
    {
        var registry = new QualityRuleRegistry();
        Assert.NotEmpty(registry.ExpensiveRules);
        Assert.Contains(registry.ExpensiveRules, r => r.Id == "NZ101");
        Assert.Contains(registry.ExpensiveRules, r => r.Id == "NZ105");
    }

    // ====================================================================
    // Cache Invalidation
    // ====================================================================

    [Fact]
    public void AllRules_IsCached_ReturnsSameReference()
    {
        var registry = new QualityRuleRegistry();
        var first = registry.AllRules;
        var second = registry.AllRules;
        Assert.Same(first, second);
    }

    [Fact]
    public void CheapRules_IsCached_ReturnsSameReference()
    {
        var registry = new QualityRuleRegistry();
        var first = registry.CheapRules;
        var second = registry.CheapRules;
        Assert.Same(first, second);
    }

    [Fact]
    public void CheapRules_CacheInvalidated_AfterSetSeverity()
    {
        var registry = new QualityRuleRegistry();
        var before = registry.CheapRules;
        registry.SetSeverity("NZ001", RuleSeverityConfig.Off);
        var after = registry.CheapRules;
        Assert.NotSame(before, after); // new cache built
        Assert.DoesNotContain(after, r => r.Id == "NZ001");
    }

    [Fact]
    public void AllRules_CacheNotInvalidated_AfterSetSeverity()
    {
        var registry = new QualityRuleRegistry();
        var before = registry.AllRules;
        registry.SetSeverity("NZ001", RuleSeverityConfig.Off);
        var after = registry.AllRules;
        Assert.Same(before, after); // AllRules cache is NOT invalidated by severity changes
    }

    [Fact]
    public void CheapRules_CacheInvalidated_AfterResetSeverities()
    {
        var registry = new QualityRuleRegistry();
        registry.SetSeverity("NZ001", RuleSeverityConfig.Off);
        var beforeDisable = registry.CheapRules;

        registry.ResetSeverities();
        var afterReset = registry.CheapRules;

        Assert.NotSame(beforeDisable, afterReset);
        Assert.Contains(afterReset, r => r.Id == "NZ001"); // re-enabled
    }

    // ====================================================================
    // LintEngine + Registry Integration
    // ====================================================================

    public sealed class LintEngineRegistryIntegrationTests : IDisposable
    {
        private readonly LintEngine _engine = new();

        public void Dispose() => _engine.Dispose();

        [Fact]
        public void RunCheapRules_RespectsRegistrySeverity_Off()
        {
            // Disable NZ001 via registry
            _engine.Registry.SetSeverity("NZ001", RuleSeverityConfig.Off);

            var issues = _engine.RunCheapRules("SELECT * FROM employees");
            Assert.DoesNotContain(issues, i => i.RuleId == "NZ001");
        }

        [Fact]
        public void RunCheapRules_RespectsRegistrySeverity_ChangedSeverity()
        {
            // Change NZ001 from Warning to Error via registry
            _engine.Registry.SetSeverity("NZ001", RuleSeverityConfig.Error);

            var issues = _engine.RunCheapRules("SELECT * FROM employees");
            var nz001 = Assert.Single(issues, i => i.RuleId == "NZ001");
            Assert.Equal(LintSeverity.Error, nz001.Severity);
        }

        [Fact]
        public void RunCheapRules_PerCallOverride_WinsOverRegistry()
        {
            // Registry says Error for NZ001
            _engine.Registry.SetSeverity("NZ001", RuleSeverityConfig.Error);

            // Per-call says Hint (should win)
            var perCall = new Dictionary<string, RuleSeverityConfig>
            {
                ["NZ001"] = RuleSeverityConfig.Hint
            };
            var issues = _engine.RunCheapRules("SELECT * FROM employees", perCall);
            var nz001 = Assert.Single(issues, i => i.RuleId == "NZ001");
            Assert.Equal(LintSeverity.Hint, nz001.Severity);
        }

        [Fact]
        public void RunCheapRules_PerCallOff_WinsOverRegistry()
        {
            // Registry says Error for NZ001
            _engine.Registry.SetSeverity("NZ001", RuleSeverityConfig.Error);

            // Per-call says Off (should win - skip the rule)
            var perCall = new Dictionary<string, RuleSeverityConfig>
            {
                ["NZ001"] = RuleSeverityConfig.Off
            };
            var issues = _engine.RunCheapRules("SELECT * FROM employees", perCall);
            Assert.DoesNotContain(issues, i => i.RuleId == "NZ001");
        }

        [Fact]
        public void RunCheapRules_RegistryOff_PerCallOverride_Reenables()
        {
            // Registry disables NZ001
            _engine.Registry.SetSeverity("NZ001", RuleSeverityConfig.Off);

            // Per-call overrides to Warning (re-enables for this call)
            var perCall = new Dictionary<string, RuleSeverityConfig>
            {
                ["NZ001"] = RuleSeverityConfig.Warning
            };
            var issues = _engine.RunCheapRules("SELECT * FROM employees", perCall);
            Assert.Contains(issues, i => i.RuleId == "NZ001");
        }

        [Fact]
        public void RunFullLint_RegistrySeverityOff_AffectsCheapRules()
        {
            // Disable NZ001 via registry
            _engine.Registry.SetSeverity("NZ001", RuleSeverityConfig.Off);

            var config = new LintConfig("SELECT * FROM t", Schema: null);
            var result = _engine.RunFullLint(config);

            // NZ001 should NOT appear (disabled via registry)
            Assert.DoesNotContain(result.Issues, i => i.RuleId == "NZ001");
        }

        [Fact]
        public void RunFullLint_RegistrySeverityError_ChangesSeverity()
        {
            _engine.Registry.SetSeverity("NZ001", RuleSeverityConfig.Error);

            var config = new LintConfig("SELECT * FROM t", Schema: null);
            var result = _engine.RunFullLint(config);

            var nz001 = Assert.Single(result.Issues, i => i.RuleId == "NZ001");
            Assert.Equal(LintSeverity.Error, nz001.Severity);
        }

        [Fact]
        public void RunFullLint_WithSchema_RegistrySeverityOff()
        {
            var schema = SqlTestHelpers.CreateStandardMockSchema();
            _engine.Registry.SetSeverity("NZ001", RuleSeverityConfig.Off);

            var config = new LintConfig("SELECT * FROM employees", Schema: schema);
            var result = _engine.RunFullLint(config);

            // NZ001 should NOT appear, but visitor errors should still be reported
            Assert.DoesNotContain(result.Issues, i => i.RuleId == "NZ001");
        }

        [Fact]
        public void LintEngine_RegistryProperty_IsAccessible()
        {
            Assert.NotNull(_engine.Registry);
            Assert.IsType<QualityRuleRegistry>(_engine.Registry);
        }

        [Fact]
        public void LintEngine_WithCustomRegistry_Works()
        {
            var registry = new QualityRuleRegistry(new[] { new RuleNZ001_SelectStar() });
            using var engine = new LintEngine(registry);
            Assert.Same(registry, engine.Registry);
            Assert.Single(engine.CheapRules);
        }

        [Fact]
        public void RunCheapRules_RegistryVariablesReEnabledAfterReset()
        {
            _engine.Registry.SetSeverity("NZ001", RuleSeverityConfig.Off);
            Assert.DoesNotContain(_engine.RunCheapRules("SELECT * FROM t"), i => i.RuleId == "NZ001");

            _engine.Registry.ResetSeverities();
            Assert.Contains(_engine.RunCheapRules("SELECT * FROM t"), i => i.RuleId == "NZ001");
        }
    }

    // ====================================================================
    // Thread Safety
    // ====================================================================

    public sealed class RegistryThreadSafetyTests
    {
        [Fact]
        public void ConcurrentSetAndGet_DoesNotThrow()
        {
            var registry = new QualityRuleRegistry();
            var exceptions = new List<Exception>();
            var lockObj = new object();
            var threads = new List<Thread>();

            // 4 reader threads reading effective severities
            for (int t = 0; t < 4; t++)
            {
                var thread = new Thread(() =>
                {
                    try
                    {
                        for (int i = 0; i < 100; i++)
                        {
                            var sev = registry.GetEffectiveSeverity("NZ001");
                            Assert.True(sev >= RuleSeverityConfig.Error && sev <= RuleSeverityConfig.Off);
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (lockObj) { exceptions.Add(ex); }
                    }
                });
                threads.Add(thread);
                thread.Start();
            }

            // 4 writer threads changing severities
            for (int t = 0; t < 4; t++)
            {
                var thread = new Thread(() =>
                {
                    try
                    {
                        for (int i = 0; i < 100; i++)
                        {
                            registry.SetSeverity("NZ001", (RuleSeverityConfig)(i % 5));
                            registry.ResetSeverities();
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (lockObj) { exceptions.Add(ex); }
                    }
                });
                threads.Add(thread);
                thread.Start();
            }

            foreach (var t in threads) t.Join();
            Assert.Empty(exceptions);
        }

        [Fact]
        public void ConcurrentAccess_CheapRulesCache()
        {
            var registry = new QualityRuleRegistry();
            var exceptions = new List<Exception>();
            var lockObj = new object();
            var threads = new List<Thread>();

            for (int t = 0; t < 8; t++)
            {
                var thread = new Thread(() =>
                {
                    try
                    {
                        for (int i = 0; i < 50; i++)
                        {
                            // Toggle severity and read cheap rules concurrently
                            if (i % 2 == 0)
                                registry.SetSeverity("NZ001", RuleSeverityConfig.Off);
                            else
                                registry.SetSeverity("NZ001", RuleSeverityConfig.Error);

                            var cheap = registry.CheapRules;
                            Assert.NotNull(cheap);

                            var all = registry.AllRules;
                            Assert.NotNull(all);
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (lockObj) { exceptions.Add(ex); }
                    }
                });
                threads.Add(thread);
                thread.Start();
            }

            foreach (var t in threads) t.Join();
            Assert.Empty(exceptions);
        }
    }
}
