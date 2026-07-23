using JustyBase.NetezzaSqlParser.Linter;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.Tests.NetezzaSqlParser;

/// <summary>
/// Legacy tests for NZ lint rules, now using LintEngine.RunCheapRules().
/// These complement the more comprehensive LintEngineTests.
/// </summary>
public sealed class NzLinterTests : IDisposable
{
    private readonly LintEngine _engine = new();
    private readonly ISchemaProvider _schema = SqlTestHelpers.CreateStandardMockSchema();

    public void Dispose() => _engine.Dispose();

    private List<LintIssue> RunParserDiagnostics(string sql) =>
        _engine.RunExpensiveAnalysis(new LintConfig(sql, Schema: _schema, DocumentUri: Guid.NewGuid().ToString())).Issues.ToList();

    [Fact]
    public void NZ001_SelectStar_Detected()
    {
        var issues = _engine.RunCheapRules("SELECT * FROM t");
        Assert.Contains(issues, i => i.RuleId == "NZ001");
    }

    [Fact]
    public void NZ001_SelectStar_ExplicitColumns_NoIssue()
    {
        var issues = _engine.RunCheapRules("SELECT col1, col2 FROM t");
        Assert.DoesNotContain(issues, i => i.RuleId == "NZ001");
    }

    [Fact]
    public void SQL043_DeleteWithoutWhere_Detected()
    {
        var issues = RunParserDiagnostics("DELETE FROM t");
        Assert.Contains(issues, i => i.RuleId == "SQL043");
    }

    [Fact]
    public void SQL043_DeleteWithWhere_NoIssue()
    {
        var issues = RunParserDiagnostics("DELETE FROM t WHERE id = 1");
        Assert.DoesNotContain(issues, i => i.RuleId == "SQL043");
    }

    [Fact]
    public void SQL044_UpdateWithoutWhere_Detected()
    {
        var issues = RunParserDiagnostics("UPDATE t SET x = 1");
        Assert.Contains(issues, i => i.RuleId == "SQL044");
    }

    [Fact]
    public void SQL044_UpdateWithWhere_NoIssue()
    {
        var issues = RunParserDiagnostics("UPDATE t SET x = 1 WHERE id = 2");
        Assert.DoesNotContain(issues, i => i.RuleId == "SQL044");
    }

    [Fact]
    public void NZ004_CrossJoin_Detected()
    {
        var issues = _engine.RunCheapRules("SELECT * FROM a CROSS JOIN b");
        Assert.Contains(issues, i => i.RuleId == "NZ004");
    }

    [Fact]
    public void NZ005_LeadingWildcardLike_Detected()
    {
        var issues = _engine.RunCheapRules("SELECT * FROM t WHERE col LIKE '%test'");
        Assert.Contains(issues, i => i.RuleId == "NZ005");
    }

    [Fact]
    public void NZ006_OrderByWithoutLimit_Detected()
    {
        var issues = _engine.RunCheapRules("SELECT * FROM t ORDER BY col1");
        Assert.Contains(issues, i => i.RuleId == "NZ006");
    }

    [Fact]
    public void NZ006_OrderByWithLimit_NoIssue()
    {
        var issues = _engine.RunCheapRules("SELECT * FROM t ORDER BY col1 LIMIT 100");
        Assert.DoesNotContain(issues, i => i.RuleId == "NZ006");
    }

    [Fact]
    public void NZ008_Truncate_Detected()
    {
        var issues = _engine.RunCheapRules("TRUNCATE TABLE t");
        Assert.Contains(issues, i => i.RuleId == "NZ008");
    }

    [Fact]
    public void SQL046_UpdateWithAs_Detected()
    {
        var issues = RunParserDiagnostics("UPDATE t AS x SET col = 1");
        Assert.Contains(issues, i => i.RuleId == "SQL046");
    }

    [Fact]
    public void NZ013_PreferUnionAll_Detected()
    {
        var issues = _engine.RunCheapRules("SELECT 1 UNION SELECT 2");
        Assert.Contains(issues, i => i.RuleId == "NZ013");
    }

    [Fact]
    public void NZ013_UnionAll_NoIssue()
    {
        var issues = _engine.RunCheapRules("SELECT 1 UNION ALL SELECT 2");
        Assert.DoesNotContain(issues, i => i.RuleId == "NZ013");
    }

    [Fact]
    public void SQL041_CaseWithoutEnd_Detected()
    {
        var issues = RunParserDiagnostics("SELECT CASE WHEN x > 10 THEN 'big'");
        Assert.Contains(issues, i => i.RuleId == "PAR108");
    }

    [Fact]
    public void SQL041_CaseWithEnd_NoIssue()
    {
        var issues = RunParserDiagnostics("SELECT CASE WHEN x > 10 THEN 'big' ELSE 'small' END FROM t");
        Assert.DoesNotContain(issues, i => i.RuleId == "SQL041");
    }

    [Fact]
    public void NZ020_InSelectSubquery_Detected()
    {
        var issues = _engine.RunCheapRules("SELECT * FROM t WHERE col IN (SELECT id FROM u)");
        Assert.Contains(issues, i => i.RuleId == "NZ020");
    }

    [Fact]
    public void Comments_IgnoreLintRules()
    {
        var issues = _engine.RunCheapRules("-- SELECT * FROM t\nSELECT col1 FROM t");
        Assert.DoesNotContain(issues, i => i.RuleId == "NZ001");
    }

    [Fact]
    public void Strings_IgnoreStructuralDeleteRule()
    {
        var issues = RunParserDiagnostics("SELECT 'DELETE FROM' AS msg FROM t");
        Assert.DoesNotContain(issues, i => i.RuleId == "SQL043");
    }

    [Fact]
    public void AllRules_AreDefined()
    {
        Assert.Equal(24, NzLintRules.AllRules.Count);
        var ids = NzLintRules.AllRules.Select(r => r.Id).ToList();
        for (int i = 1; i <= 24; i++)
        {
            var expected = $"NZ{i:D3}";
            Assert.Contains(expected, ids);
        }
    }
}
