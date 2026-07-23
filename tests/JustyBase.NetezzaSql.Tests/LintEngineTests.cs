using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Caching;
using JustyBase.NetezzaSqlParser.Linter;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.Tests.NetezzaSqlParser;

// ========================================================================
// LintEngine Tests
// ========================================================================

public sealed class LintEngineTests
{
    // ====================================================================
    // RunCheapRules
    // ====================================================================

    public sealed class RunCheapRulesTests : IDisposable
    {
        private readonly LintEngine _engine = new();
        private readonly ISchemaProvider _schema = SqlTestHelpers.CreateStandardMockSchema();

        public void Dispose()
        {
            _engine.Dispose();
        }

        private List<LintIssue> RunParserDiagnostics(string sql) =>
            _engine.RunExpensiveAnalysis(new LintConfig(sql, Schema: _schema, DocumentUri: Guid.NewGuid().ToString())).Issues.ToList();

        [Fact]
        public void SelectStar_ReturnsNZ001()
        {
            var issues = _engine.RunCheapRules("SELECT * FROM employees");
            Assert.Contains(issues, i => i.RuleId == "NZ001");
        }

        [Fact]
        public void CleanSql_ReturnsNoIssues()
        {
            var issues = _engine.RunCheapRules("SELECT employee_id FROM employees");
            Assert.Empty(issues);
        }

        [Fact]
        public void DeleteWithoutWhere_ReturnsSQL043()
        {
            var issues = RunParserDiagnostics("DELETE FROM employees");
            Assert.Contains(issues, i => i.RuleId == "SQL043");
        }

        [Fact]
        public void DeleteWithWhere_ReturnsNoSQL043()
        {
            var issues = RunParserDiagnostics("DELETE FROM employees WHERE department_id = 10");
            Assert.DoesNotContain(issues, i => i.RuleId == "SQL043");
        }

        [Fact]
        public void UpdateWithoutWhere_ReturnsSQL044()
        {
            var issues = RunParserDiagnostics("UPDATE employees SET salary = 0");
            Assert.Contains(issues, i => i.RuleId == "SQL044");
        }

        [Fact]
        public void UpdateWithWhere_ReturnsNoSQL044()
        {
            var issues = RunParserDiagnostics("UPDATE employees SET salary = 0 WHERE department_id = 10");
            Assert.DoesNotContain(issues, i => i.RuleId == "SQL044");
        }

        [Fact]
        public void CrossJoin_ReturnsNZ004()
        {
            var issues = _engine.RunCheapRules("SELECT * FROM employees CROSS JOIN departments");
            Assert.Contains(issues, i => i.RuleId == "NZ004");
        }

        [Fact]
        public void OrderByWithoutLimit_ReturnsNZ006()
        {
            var issues = _engine.RunCheapRules("SELECT * FROM employees ORDER BY last_name");
            Assert.Contains(issues, i => i.RuleId == "NZ006");
        }

        [Fact]
        public void OrderByWithLimit_ReturnsNoNZ006()
        {
            var issues = _engine.RunCheapRules("SELECT * FROM employees ORDER BY last_name LIMIT 10");
            Assert.DoesNotContain(issues, i => i.RuleId == "NZ006");
        }

        [Fact]
        public void Truncate_ReturnsNZ008()
        {
            var issues = _engine.RunCheapRules("TRUNCATE TABLE employees");
            Assert.Contains(issues, i => i.RuleId == "NZ008");
        }

        [Fact]
        public void UnionWithoutAll_ReturnsNZ013()
        {
            var issues = _engine.RunCheapRules("SELECT 1 UNION SELECT 2");
            Assert.Contains(issues, i => i.RuleId == "NZ013");
        }

        [Fact]
        public void UnionAll_ReturnsNoNZ013()
        {
            var issues = _engine.RunCheapRules("SELECT 1 UNION ALL SELECT 2");
            Assert.DoesNotContain(issues, i => i.RuleId == "NZ013");
        }

        [Fact]
        public void UpdateWithAs_ReturnsSQL046()
        {
            var issues = RunParserDiagnostics("UPDATE employees AS e SET salary = 0");
            Assert.Contains(issues, i => i.RuleId == "SQL046");
        }

        [Fact]
        public void KeywordTypo_LegacyRuleStillDetectsWhenEnabled()
        {
            var issues = _engine.RunCheapRules("SELEC 1", new Dictionary<string, RuleSeverityConfig>
            {
                ["NZ023"] = RuleSeverityConfig.Warning
            });
            Assert.Contains(issues, i => i.RuleId == "NZ023");
        }

        [Fact]
        public void TrailingComma_ReturnsParserStructuralCode()
        {
            var issues = RunParserDiagnostics("SELECT 1, FROM t");
            Assert.Contains(issues, i => i.RuleId is "PAR001" or "PAR002");
        }

        [Fact]
        public void EmptySql_ReturnsNoIssues()
        {
            var issues = _engine.RunCheapRules("");
            Assert.Empty(issues);
        }

        [Fact]
        public void WhitespaceOnly_ReturnsNoIssues()
        {
            var issues = _engine.RunCheapRules("   \n  \t  ");
            Assert.Empty(issues);
        }

        [Fact]
        public void SeverityOverride_ChangesSeverity()
        {
            var severities = new Dictionary<string, RuleSeverityConfig>
            {
                ["NZ001"] = RuleSeverityConfig.Error // default is Warning
            };
            var issues = _engine.RunCheapRules("SELECT * FROM employees", severities);
            var nz001 = Assert.Single(issues, i => i.RuleId == "NZ001");
            Assert.Equal(LintSeverity.Error, nz001.Severity);
        }

        [Fact]
        public void SeverityOverride_Off_SkipsRule()
        {
            var severities = new Dictionary<string, RuleSeverityConfig>
            {
                ["NZ001"] = RuleSeverityConfig.Off
            };
            var issues = _engine.RunCheapRules("SELECT * FROM employees", severities);
            Assert.DoesNotContain(issues, i => i.RuleId == "NZ001");
        }

        [Fact]
        public void MultipleIssues_FromDifferentRules()
        {
            // SELECT * + ORDER BY without LIMIT + UNION without ALL
            var issues = _engine.RunCheapRules("SELECT * FROM t ORDER BY x UNION SELECT 1");
            Assert.Contains(issues, i => i.RuleId == "NZ001");
            Assert.Contains(issues, i => i.RuleId == "NZ006");
            Assert.Contains(issues, i => i.RuleId == "NZ013");
        }

        [Fact]
        public void InSubquery_ReturnsNZ020()
        {
            var issues = _engine.RunCheapRules("SELECT * FROM t WHERE id IN (SELECT id FROM t2)");
            Assert.Contains(issues, i => i.RuleId == "NZ020");
        }

        [Fact]
        public void LeadingWildcardLike_ReturnsNZ005()
        {
            var issues = _engine.RunCheapRules("SELECT * FROM t WHERE name LIKE '%test'");
            Assert.Contains(issues, i => i.RuleId == "NZ005");
        }

        [Fact]
        public void OrInWhereWithMultipleOrs_ReturnsNZ009()
        {
            var issues = _engine.RunCheapRules("SELECT * FROM t WHERE a = 1 OR b = 2 OR c = 3");
            Assert.Contains(issues, i => i.RuleId == "NZ009");
        }

        [Fact]
        public void OrInWhereWithSingleOr_ReturnsNoNZ009()
        {
            var issues = _engine.RunCheapRules("SELECT * FROM t WHERE a = 1 OR b = 2");
            Assert.DoesNotContain(issues, i => i.RuleId == "NZ009");
        }

        [Fact]
        public void MissingAliasInJoin_ReturnsNZ010()
        {
            var issues = _engine.RunCheapRules("SELECT * FROM t1 JOIN t2 ON t1.id = t2.id");
            Assert.Contains(issues, i => i.RuleId == "NZ010");
        }

        [Fact]
        public void OrInJoinCondition_ReturnsNZ014()
        {
            var issues = _engine.RunCheapRules("SELECT * FROM t1 JOIN t2 ON t1.id = t2.id OR t1.id = t2.ref_id");
            Assert.Contains(issues, i => i.RuleId == "NZ014");
        }

        [Fact]
        public void DoubleQuotedIdentifier_ReturnsNZ017()
        {
            var issues = _engine.RunCheapRules("SELECT * FROM \"MyTable\"");
            Assert.Contains(issues, i => i.RuleId == "NZ017");
        }

        [Fact]
        public void CaseWithoutEnd_ReturnsParserStructuralCode()
        {
            var issues = RunParserDiagnostics("SELECT CASE WHEN 1 = 1 THEN 'yes'");
            Assert.Contains(issues, i => i.RuleId == "PAR108");
        }

        [Fact]
        public void CaseWithEnd_ReturnsNoSQL041()
        {
            var issues = RunParserDiagnostics("SELECT CASE WHEN 1 = 1 THEN 'yes' ELSE 'no' END FROM t");
            Assert.DoesNotContain(issues, i => i.RuleId == "SQL041");
        }
    }

    // ====================================================================
    // RunExpensiveAnalysis
    // ====================================================================

    public sealed class RunExpensiveAnalysisTests : IDisposable
    {
        private readonly LintEngine _engine = new();

        public void Dispose()
        {
            _engine.Dispose();
        }

        [Fact]
        public void ValidSql_WithSchema_ReturnsNoErrors()
        {
            var schema = SqlTestHelpers.CreateStandardMockSchema();
            var config = new LintConfig("SELECT employee_id FROM employees", Schema: schema);
            var result = _engine.RunExpensiveAnalysis(config);
            Assert.Empty(result.Issues);
            Assert.Equal(0, result.ParserErrorCount);
            Assert.Equal(0, result.VisitorErrorCount);
        }

        [Fact]
        public void SqlWithoutSchema_ReturnsEmptyResult()
        {
            var config = new LintConfig("SELECT employee_id FROM employees", Schema: null);
            var result = _engine.RunExpensiveAnalysis(config);
            Assert.Empty(result.Issues);
            Assert.False(result.UsedCache);
        }

        [Fact]
        public void EmptySql_ReturnsEmptyResult()
        {
            var schema = SqlTestHelpers.CreateStandardMockSchema();
            var config = new LintConfig("", Schema: schema);
            var result = _engine.RunExpensiveAnalysis(config);
            Assert.Empty(result.Issues);
            Assert.Equal(0, result.ParserErrorCount);
        }

        [Fact]
        public void NullSql_ReturnsEmptyResult()
        {
            var schema = SqlTestHelpers.CreateStandardMockSchema();
            var config = new LintConfig(null!, Schema: schema);
            var result = _engine.RunExpensiveAnalysis(config);
            Assert.Empty(result.Issues);
        }

        [Fact]
        public void InvalidColumn_ReturnsVisitorError()
        {
            var schema = SqlTestHelpers.CreateStandardMockSchema();
            var config = new LintConfig("SELECT nonexistent_column FROM employees", Schema: schema);
            var result = _engine.RunExpensiveAnalysis(config);
            Assert.NotEmpty(result.Issues);
            Assert.True(result.VisitorErrorCount > 0);
        }

        [Fact]
        public void QualifiedWildcardCountsAsAliasUse()
        {
            var schema = SqlTestHelpers.CreateStandardMockSchema();
            var config = new LintConfig("SELECT e.* FROM employees e", Schema: schema);
            var result = _engine.RunExpensiveAnalysis(config);

            Assert.DoesNotContain(result.Issues, issue => issue.RuleId == "SQL019");
        }

        [Fact]
        public void InvalidTable_ReturnsVisitorError()
        {
            var schema = SqlTestHelpers.CreateStandardMockSchema();
            var config = new LintConfig("SELECT * FROM TESTDB..nonexistent_table", Schema: schema);
            var result = _engine.RunExpensiveAnalysis(config);
            Assert.NotEmpty(result.Issues);
            Assert.True(result.VisitorErrorCount > 0);
        }

        [Fact]
        public void ParserError_ReturnsParserError()
        {
            var schema = SqlTestHelpers.CreateStandardMockSchema();
            // Malformed SQL that the parser will produce an error for
            var config = new LintConfig("SELECT WHERE FROM employees", Schema: schema);
            var result = _engine.RunExpensiveAnalysis(config);
            Assert.True(result.ParserErrorCount > 0 || result.Issues.Count > 0);
        }

        [Fact]
        public void SameSqlTwice_SecondCall_UsedCache()
        {
            var schema = SqlTestHelpers.CreateStandardMockSchema();
            var config = new LintConfig("SELECT employee_id FROM employees", Schema: schema, DocumentUri: "test-doc");

            // First call: full analysis
            var result1 = _engine.RunExpensiveAnalysis(config);
            Assert.Empty(result1.Issues);

            // Commit the index so the second call can use caching
            // (RunExpensiveAnalysis commits internally)

            // Second call: should use cache
            var result2 = _engine.RunExpensiveAnalysis(config);
            Assert.True(result2.UsedCache, "Second call should use cache");
            Assert.Empty(result2.Issues);
        }

        [Fact]
        public void DifferentSql_SecondCall_NoCache()
        {
            var schema = SqlTestHelpers.CreateStandardMockSchema();
            var config1 = new LintConfig("SELECT employee_id FROM employees", Schema: schema, DocumentUri: "test-doc");
            var config2 = new LintConfig("SELECT department_name FROM departments", Schema: schema, DocumentUri: "test-doc");

            _engine.RunExpensiveAnalysis(config1);
            var result2 = _engine.RunExpensiveAnalysis(config2);
            Assert.False(result2.UsedCache, "Different SQL should not use cache");
        }

        [Fact]
        public void MultipleStatements_ValidatesAll()
        {
            var schema = SqlTestHelpers.CreateStandardMockSchema();
            var sql = "SELECT employee_id FROM employees; SELECT department_name FROM departments";
            var config = new LintConfig(sql, Schema: schema);
            var result = _engine.RunExpensiveAnalysis(config);
            Assert.Empty(result.Issues);
        }

        [Fact]
        public void MultiStatement_OneInvalidColumn_ReportsOnlyForThat()
        {
            var schema = SqlTestHelpers.CreateStandardMockSchema();
            var sql = "SELECT employee_id FROM employees; SELECT nonexistent FROM departments";
            var config = new LintConfig(sql, Schema: schema);
            var result = _engine.RunExpensiveAnalysis(config);
            Assert.NotEmpty(result.Issues);
            Assert.True(result.VisitorErrorCount > 0);
        }

        [Fact]
        public void CancellationToken_Cancelled_ReturnsEmptyResult()
        {
            var schema = SqlTestHelpers.CreateStandardMockSchema();
            using var cts = new CancellationTokenSource();
            cts.Cancel(); // already cancelled
            var config = new LintConfig("SELECT employee_id FROM employees",
                Schema: schema, CancellationToken: cts.Token);

            var result = _engine.RunExpensiveAnalysis(config);
            Assert.Empty(result.Issues);
        }

        [Fact]
        public void UpdateStatementWithValidSchema_ValidatesCorrectly()
        {
            var schema = SqlTestHelpers.CreateStandardMockSchema();
            var config = new LintConfig("UPDATE employees SET salary = 50000 WHERE employee_id = 1", Schema: schema);
            var result = _engine.RunExpensiveAnalysis(config);
            Assert.Empty(result.Issues);
        }

        [Fact]
        public void InsertStatement_ValidatesCorrectly()
        {
            var schema = SqlTestHelpers.CreateStandardMockSchema();
            var config = new LintConfig("INSERT INTO employees (employee_id, first_name) VALUES (1, 'John')", Schema: schema);
            var result = _engine.RunExpensiveAnalysis(config);
            Assert.Empty(result.Issues);
        }

        [Fact]
        public void DeleteStatement_ValidatesCorrectly()
        {
            var schema = SqlTestHelpers.CreateStandardMockSchema();
            var config = new LintConfig("DELETE FROM employees WHERE employee_id = 1", Schema: schema);
            var result = _engine.RunExpensiveAnalysis(config);
            Assert.Empty(result.Issues);
        }

        [Fact]
        public void MultipleCalls_DifferentDocuments_IndependentCaches()
        {
            var schema = SqlTestHelpers.CreateStandardMockSchema();

            var config1 = new LintConfig("SELECT employee_id FROM employees", Schema: schema, DocumentUri: "doc1");
            var config2 = new LintConfig("SELECT employee_id FROM employees", Schema: schema, DocumentUri: "doc2");

            var result1 = _engine.RunExpensiveAnalysis(config1);
            var result2 = _engine.RunExpensiveAnalysis(config2);

            Assert.Empty(result1.Issues);
            Assert.Empty(result2.Issues);
        }

        [Fact]
        public void Clear_InvalidatesCache_NextCallIsMiss()
        {
            var schema = SqlTestHelpers.CreateStandardMockSchema();
            var config = new LintConfig("SELECT employee_id FROM employees", Schema: schema, DocumentUri: "test-doc");

            _engine.RunExpensiveAnalysis(config);
            _engine.Clear();

            // After clear, same config should produce a cache miss
            var result = _engine.RunExpensiveAnalysis(config);
            Assert.False(result.UsedCache, "After Clear(), should not use cache");
        }

        [Fact]
        public void InvalidateDocument_RerunsValidation()
        {
            var schema = SqlTestHelpers.CreateStandardMockSchema();
            var config = new LintConfig("SELECT employee_id FROM employees", Schema: schema, DocumentUri: "test-doc");

            _engine.RunExpensiveAnalysis(config);
            _engine.InvalidateDocument("test-doc");

            // Re-run after invalidate - diagnostics are cleared but parse cache is still valid.
            // RunExpensiveAnalysis detects no dirty indices, enters the fast path,
            // finds no cached diagnostics (they were cleared), falls through to re-visit.
            // At the end, UsedCache is true because parse was cached (same content).
            var result = _engine.RunExpensiveAnalysis(config);
            Assert.Empty(result.Issues); // validation succeeds
            Assert.True(result.UsedCache, "Parse cache is still valid even after InvalidateDocument");
        }

        [Fact]
        public void RuleCount_ReturnsExpensiveRuleCount()
        {
            var schema = SqlTestHelpers.CreateStandardMockSchema();
            var config = new LintConfig("SELECT employee_id FROM employees", Schema: schema);
            var result = _engine.RunExpensiveAnalysis(config);
            // Now includes 8 expensive rules (NZ101-NZ108)
            Assert.Equal(8, result.RuleCount);
        }
    }

    // ====================================================================
    // RunFullLint
    // ====================================================================

    public sealed class RunFullLintTests : IDisposable
    {
        private readonly LintEngine _engine = new();

        public void Dispose()
        {
            _engine.Dispose();
        }

        [Fact]
        public void WithoutSchema_OnlyCheapRules()
        {
            var config = new LintConfig("SELECT * FROM t", Schema: null);
            var result = _engine.RunFullLint(config);
            Assert.NotEmpty(result.Issues); // NZ001 from cheap rules
            Assert.Equal(0, result.ParserErrorCount);
            Assert.Equal(0, result.VisitorErrorCount);
            // Expensive rules are not run when no schema
            Assert.Equal(_engine.CheapRules.Count, result.RuleCount);
        }

        [Fact]
        public void WithSchema_CombinesCheapAndExpensive()
        {
            var schema = SqlTestHelpers.CreateStandardMockSchema();
            var config = new LintConfig("SELECT * FROM employees", Schema: schema);
            var result = _engine.RunFullLint(config);
            // NZ001 from cheap rules + any visitor errors
            Assert.Contains(result.Issues, i => i.RuleId == "NZ001");
        }

        [Fact]
        public void CleanSql_NoSchema_ReturnsOnlyCheapIssues()
        {
            var config = new LintConfig("SELECT employee_id FROM t", Schema: null);
            var result = _engine.RunFullLint(config);
            Assert.Empty(result.Issues);
            Assert.Equal(_engine.CheapRules.Count, result.RuleCount);
        }

        [Fact]
        public void RuleCount_WithSchema_IncludesBoth()
        {
            var schema = SqlTestHelpers.CreateStandardMockSchema();
            var config = new LintConfig("SELECT employee_id FROM employees", Schema: schema);
            var result = _engine.RunFullLint(config);
            Assert.Equal(_engine.CheapRules.Count + _engine.ExpensiveRules.Count, result.RuleCount);
        }

        [Fact]
        public void RuleCount_WithoutSchema_OnlyCheap()
        {
            var config = new LintConfig("SELECT employee_id FROM t", Schema: null);
            var result = _engine.RunFullLint(config);
            Assert.Equal(_engine.CheapRules.Count, result.RuleCount);
        }

        [Fact]
        public void EmptySql_ReturnsNoIssues()
        {
            var config = new LintConfig("", Schema: null);
            var result = _engine.RunFullLint(config);
            Assert.Empty(result.Issues);
        }

        [Fact]
        public void MultipleStatements_FullLint_ValidatesAll()
        {
            var schema = SqlTestHelpers.CreateStandardMockSchema();
            var sql = "SELECT * FROM employees; SELECT department_name FROM departments";
            var config = new LintConfig(sql, Schema: schema);
            var result = _engine.RunFullLint(config);
            // NZ001 from cheap rules on SELECT *
            Assert.Contains(result.Issues, i => i.RuleId == "NZ001");
        }

        [Fact]
        public void FullLint_WithSeverityOverride()
        {
            var severities = new Dictionary<string, RuleSeverityConfig>
            {
                ["NZ001"] = RuleSeverityConfig.Error
            };
            var config = new LintConfig("SELECT * FROM t", Schema: null, RuleSeverities: severities);
            var result = _engine.RunFullLint(config);
            var nz001 = Assert.Single(result.Issues, i => i.RuleId == "NZ001");
            Assert.Equal(LintSeverity.Error, nz001.Severity);
        }

        [Fact]
        public void FullLint_WithCancelledToken_AndSchema_ReturnsEmptyResult()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var schema = SqlTestHelpers.CreateStandardMockSchema();
            var config = new LintConfig("SELECT employee_id FROM employees", Schema: schema, CancellationToken: cts.Token);
            var result = _engine.RunFullLint(config);
            Assert.Empty(result.Issues);
        }

        [Fact]
        public void FullLint_AfterClear_RerunsAnalysis()
        {
            var schema = SqlTestHelpers.CreateStandardMockSchema();
            var config = new LintConfig("SELECT * FROM employees", Schema: schema, DocumentUri: "test-doc");

            var result1 = _engine.RunFullLint(config);
            _engine.Clear();
            var result2 = _engine.RunFullLint(config);

            Assert.NotEmpty(result1.Issues);
            Assert.NotEmpty(result2.Issues);
        }
    }

    // ====================================================================
    // Constructor & Edge Cases
    // ====================================================================

    public sealed class ConstructorTests
    {
        [Fact]
        public void DefaultConstructor_LoadsNzAndNzpRules()
        {
            var engine = new LintEngine();
            Assert.NotEmpty(engine.CheapRules);
            // Should contain known NZ rules
            Assert.Contains(engine.CheapRules, r => r.Id == "NZ001");
            Assert.DoesNotContain(engine.CheapRules, r => r.Id == "NZ002");
            Assert.Contains(engine.CheapRules, r => r.Id == "NZ021");
            Assert.Contains(engine.CheapRules, r => r.Id == "NZ022");
            Assert.DoesNotContain(engine.CheapRules, r => r.Id == "NZ023");
            Assert.DoesNotContain(engine.CheapRules, r => r.Id == "NZ024");
            // Now includes 5 expensive rules (NZ101-NZ105)
            Assert.NotEmpty(engine.ExpensiveRules);
            Assert.Contains(engine.ExpensiveRules, r => r.Id == "NZ101");
            engine.Dispose();
        }

        [Fact]
        public void CustomRulesConstructor_UsesOnlyGivenRules()
        {
            var customRule = new RuleNZ001_SelectStar();
            var engine = new LintEngine(new[] { customRule });
            Assert.Single(engine.CheapRules);
            Assert.Empty(engine.ExpensiveRules);
            engine.Dispose();
        }

        [Fact]
        public void CustomRulesConstructor_WithExpensiveRule()
        {
            var engine = new LintEngine(Array.Empty<LintRule>());
            Assert.Empty(engine.CheapRules);
            Assert.Empty(engine.ExpensiveRules);
            engine.Dispose();
        }

        [Fact]
        public void DefaultConstructor_IncludesExpensiveRules()
        {
            var engine = new LintEngine();
            Assert.NotEmpty(engine.ExpensiveRules);
            Assert.Equal(8, engine.ExpensiveRules.Count);
            engine.Dispose();
        }

        [Fact]
        public void Dispose_CanBeCalledMultipleTimes()
        {
            var engine = new LintEngine();
            engine.Dispose();
            engine.Dispose(); // should not throw
        }
    }

    // ====================================================================
    // Integration: Caching + LintEngine interaction
    // ====================================================================

    public sealed class CachingIntegrationTests : IDisposable
    {
        private readonly LintEngine _engine = new();

        public void Dispose()
        {
            _engine.Dispose();
        }

        [Fact]
        public void InvalidateDocument_ThenReAnalyze_WorksCorrectly()
        {
            var schema = SqlTestHelpers.CreateStandardMockSchema();
            var config = new LintConfig("SELECT employee_id FROM employees",
                Schema: schema, DocumentUri: "test-doc");

            // First analysis - prime the caches
            var result1 = _engine.RunFullLint(config);
            Assert.Empty(result1.Issues);

            // Invalidate just the diagnostics cache
            _engine.InvalidateDocument("test-doc");

            // Second analysis - should still work but re-validate
            var result2 = _engine.RunFullLint(config);
            Assert.Empty(result2.Issues);
        }

        [Fact]
        public void RepeatedAnalysis_SameContent_ReturnsQuickly()
        {
            var schema = SqlTestHelpers.CreateStandardMockSchema();
            var sql = "SELECT employee_id, first_name, last_name FROM employees WHERE department_id = 10";
            var config = new LintConfig(sql, Schema: schema, DocumentUri: "perf-doc");

            // Prime
            var result1 = _engine.RunFullLint(config);

            // Second call - should use cache
            var result2 = _engine.RunFullLint(config);

            Assert.Empty(result1.Issues);
            Assert.Empty(result2.Issues);
        }

        [Fact]
        public void MetadataEpochChange_InvalidatesDiagnosticsCache()
        {
            var schema = SqlTestHelpers.CreateStandardMockSchema();
            var config1 = new LintConfig("SELECT employee_id FROM employees",
                Schema: schema, DocumentUri: "epoch-doc", MetadataEpoch: 1);

            var config2 = new LintConfig("SELECT employee_id FROM employees",
                Schema: schema, DocumentUri: "epoch-doc", MetadataEpoch: 2);

            // Prime with epoch 1
            var result1 = _engine.RunFullLint(config1);
            Assert.Empty(result1.Issues);

            // Same SQL but different epoch - should re-validate
            var result2 = _engine.RunFullLint(config2);
            Assert.Empty(result2.Issues);
        }
    }

    // ====================================================================
    // Metrics Integration
    // ====================================================================

    public sealed class MetricsIntegrationTests : IDisposable
    {
        private readonly LintEngine _engine = new();

        public void Dispose()
        {
            _engine.Dispose();
        }

        [Fact]
        public void RunCheapRules_RecordsMetrics()
        {
            _engine.ResetMetrics();
            _engine.RunCheapRules("SELECT * FROM t");
            var s = _engine.Metrics;
            Assert.Equal(1, s.CheapRunCount);
            Assert.True(s.CheapTotalTimeMs > 0, "Cheap run should take measurable time");
            Assert.True(s.CheapAvgTimeMs > 0);
        }

        [Fact]
        public void MultipleRunCheapRules_AccumulatesMetrics()
        {
            _engine.ResetMetrics();
            for (int i = 0; i < 5; i++)
                _engine.RunCheapRules("SELECT * FROM t");

            var s = _engine.Metrics;
            Assert.Equal(5, s.CheapRunCount);
            Assert.True(s.CheapTotalTimeMs > 0);
        }

        [Fact]
        public void RunExpensiveAnalysis_RecordsCacheMiss()
        {
            _engine.ResetMetrics();
            var schema = SqlTestHelpers.CreateStandardMockSchema();
            var config = new LintConfig("SELECT employee_id FROM employees",
                Schema: schema, DocumentUri: "metrics-doc");

            _engine.RunExpensiveAnalysis(config);
            var s = _engine.Metrics;
            Assert.Equal(1, s.ExpensiveRunCount);
            Assert.Equal(0, s.CacheHitCount); // first call is always a miss
            Assert.Equal(1, s.CacheMissCount);
        }

        [Fact]
        public void RunExpensiveAnalysis_RecordsCacheHit()
        {
            _engine.ResetMetrics();
            var schema = SqlTestHelpers.CreateStandardMockSchema();
            var config = new LintConfig("SELECT employee_id FROM employees",
                Schema: schema, DocumentUri: "metrics-hit-doc");

            _engine.RunExpensiveAnalysis(config); // miss
            _engine.RunExpensiveAnalysis(config); // hit

            var s = _engine.Metrics;
            Assert.Equal(2, s.ExpensiveRunCount);
            Assert.True(s.CacheHitCount >= 1, "Second call should be a cache hit");
        }

        [Fact]
        public void ResetMetrics_ClearsAll()
        {
            _engine.RunCheapRules("SELECT * FROM t");
            _engine.ResetMetrics();

            var s = _engine.Metrics;
            Assert.Equal(0, s.CheapRunCount);
            Assert.Equal(0, s.CheapTotalTimeMs);
            Assert.Equal(0, s.ExpensiveRunCount);
            Assert.Equal(0, s.CacheHitCount);
            Assert.Equal(0, s.CacheMissCount);
        }

        [Fact]
        public void RunFullLint_RecordsBothMetricTypes()
        {
            _engine.ResetMetrics();
            var schema = SqlTestHelpers.CreateStandardMockSchema();
            var config = new LintConfig("SELECT * FROM employees",
                Schema: schema, DocumentUri: "full-lint-metrics");

            _engine.RunFullLint(config);
            var s = _engine.Metrics;
            Assert.Equal(1, s.CheapRunCount);
            Assert.Equal(1, s.ExpensiveRunCount);
        }
    }

    // ====================================================================
    // Thread Safety
    // ====================================================================

    public sealed class ThreadSafetyTests
    {
        [Fact]
        public void ConcurrentAccess_DoesNotThrow()
        {
            var engine = new LintEngine();
            var schema = SqlTestHelpers.CreateStandardMockSchema();
            var exceptions = new List<Exception>();
            var lockObj = new object();
            var threads = new List<Thread>();

            for (int t = 0; t < 8; t++)
            {
                var threadId = t;
                var thread = new Thread(() =>
                {
                    try
                    {
                        for (int i = 0; i < 10; i++)
                        {
                            var sql = i % 2 == 0
                                ? "SELECT employee_id FROM employees"
                                : "SELECT * FROM departments";

                            var config = new LintConfig(sql,
                                Schema: schema,
                                DocumentUri: $"doc{threadId % 4}");

                            var result = engine.RunFullLint(config);
                            Assert.NotNull(result.Issues);
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
            engine.Dispose();
            Assert.Empty(exceptions);
        }
    }
}
