using System.Diagnostics;
using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.NetezzaSqlParser.Caching;
using JustyBase.NetezzaSqlParser.Linter;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed class SqlPerformancePolicyTests
{
    [Fact]
    public void IsLargeScript_UsesCharThreshold()
    {
        Assert.False(SqlPerformancePolicy.IsLargeScript(SqlPerformancePolicy.LargeScriptCharThreshold));
        Assert.True(SqlPerformancePolicy.IsLargeScript(SqlPerformancePolicy.LargeScriptCharThreshold + 1));
    }

    [Fact]
    public void IsLargeScriptDocument_UsesLineOrCharThreshold()
    {
        Assert.True(SqlPerformancePolicy.IsLargeScriptDocument(SqlPerformancePolicy.LargeScriptLineThreshold + 1, 10));
        Assert.True(SqlPerformancePolicy.IsLargeScriptDocument(10, SqlPerformancePolicy.LargeScriptCharThreshold + 1));
        Assert.False(SqlPerformancePolicy.IsLargeScriptDocument(10, 10));
    }

    [Fact]
    public void GetLintDebounceMs_UsesExtendedDelayForLargeScripts()
    {
        Assert.Equal(
            SqlPerformancePolicy.DefaultLintDebounceMs,
            SqlPerformancePolicy.GetLintDebounceMs(10, 100));
        Assert.Equal(
            SqlPerformancePolicy.LargeScriptLintDebounceMs,
            SqlPerformancePolicy.GetLintDebounceMs(SqlPerformancePolicy.LargeDiagnosticsLineThreshold + 1, 100));
        Assert.Equal(
            SqlPerformancePolicy.LargeScriptLintDebounceMs,
            SqlPerformancePolicy.GetLintDebounceMs(10, SqlPerformancePolicy.LargeScriptCharThreshold + 1));
    }

    [Fact]
    public void GetSemanticDebounceMs_UsesExtendedDelayForLargeScripts()
    {
        Assert.Equal(
            SqlPerformancePolicy.DefaultSemanticDebounceMs,
            SqlPerformancePolicy.GetSemanticDebounceMs(10, 100));
        Assert.Equal(
            SqlPerformancePolicy.LargeScriptSemanticDebounceMs,
            SqlPerformancePolicy.GetSemanticDebounceMs(SqlPerformancePolicy.LargeScriptLineThreshold + 1, 100));
    }

    [Fact]
    public void CountLines_HandlesAllNewlineStyles()
    {
        Assert.Equal(0, SqlPerformancePolicy.CountLines(null));
        Assert.Equal(0, SqlPerformancePolicy.CountLines(""));
        Assert.Equal(1, SqlPerformancePolicy.CountLines("select 1"));
        Assert.Equal(3, SqlPerformancePolicy.CountLines("a\nb\nc"));
        Assert.Equal(3, SqlPerformancePolicy.CountLines("a\r\nb\r\nc"));
        Assert.Equal(3, SqlPerformancePolicy.CountLines("a\rb\rc"));
    }

    [Fact]
    public void ShouldUseIncrementalValidation_RequiresMinorityDirty()
    {
        Assert.False(SqlPerformancePolicy.ShouldUseIncrementalValidation(10, 0));
        Assert.True(SqlPerformancePolicy.ShouldUseIncrementalValidation(10, 5));
        Assert.False(SqlPerformancePolicy.ShouldUseIncrementalValidation(10, 6));
    }

    [Fact]
    public void ExceedsLineThreshold_StopsEarly()
    {
        string sql = string.Join("\n", Enumerable.Range(0, 5_000).Select(i => $"-- {i}"));
        Assert.True(SqlPerformancePolicy.ExceedsLineThreshold(sql, 3_000));
        Assert.False(SqlPerformancePolicy.ExceedsLineThreshold("a\nb\nc", 10));
    }

    [Fact]
    public void GetLintDebounceMs_String_DoesNotRequireFullLineScanForLargeByChars()
    {
        string sql = new string('x', SqlPerformancePolicy.LargeScriptCharThreshold + 10);
        Assert.Equal(
            SqlPerformancePolicy.LargeScriptLintDebounceMs,
            SqlPerformancePolicy.GetLintDebounceMs(sql));
    }

    [Fact]
    public void ShouldSkipDeepAutocompleteScan_ForLargeScripts()
    {
        Assert.True(SqlPerformancePolicy.ShouldSkipDeepAutocompleteScan(10, SqlPerformancePolicy.LargeScriptCharThreshold + 1));
        Assert.True(SqlPerformancePolicy.ShouldSkipDeepAutocompleteScan(SqlPerformancePolicy.HugeScriptLineThreshold + 1, 100));
        Assert.False(SqlPerformancePolicy.ShouldSkipDeepAutocompleteScan(10, 100));
    }

    [Fact]
    public void ShouldSkipLiveLint_OnlyForHugeScripts()
    {
        Assert.True(SqlPerformancePolicy.ShouldSkipLiveLint(
            SqlPerformancePolicy.HugeScriptLineThreshold + 1, 100));
        Assert.True(SqlPerformancePolicy.ShouldSkipLiveLint(
            10, SqlPerformancePolicy.CheapLintOnlyCharLimit + 1));
        Assert.False(SqlPerformancePolicy.ShouldSkipLiveLint(
            SqlPerformancePolicy.LargeScriptLineThreshold + 1, 100));
        Assert.False(SqlPerformancePolicy.ShouldSkipLiveLint(10, 100));
    }

    [Fact]
    public void ShouldSkipLint_OnlyLiveOnHugeScripts()
    {
        int hugeLines = SqlPerformancePolicy.HugeScriptLineThreshold + 1;
        Assert.True(SqlPerformancePolicy.ShouldSkipLint(SqlLintInvocation.Live, hugeLines, 100));
        Assert.False(SqlPerformancePolicy.ShouldSkipLint(SqlLintInvocation.Save, hugeLines, 100));
        Assert.False(SqlPerformancePolicy.ShouldSkipLint(SqlLintInvocation.Manual, hugeLines, 100));
        Assert.False(SqlPerformancePolicy.ShouldSkipLint(
            SqlLintInvocation.Live,
            SqlPerformancePolicy.LargeScriptLineThreshold + 1,
            100));
    }

    [Fact]
    public void ShouldPublishEmptyDiagnosticsWhileTyping_MatchesSkipLiveLint()
    {
        Assert.Equal(
            SqlPerformancePolicy.ShouldSkipLiveLint(SqlPerformancePolicy.HugeScriptLineThreshold + 1, 50),
            SqlPerformancePolicy.ShouldPublishEmptyDiagnosticsWhileTyping(
                SqlPerformancePolicy.HugeScriptLineThreshold + 1, 50));
    }

    [Fact]
    public void ShouldUseLexOnlySemanticClassification_UsesCharThresholdOnly()
    {
        Assert.False(SqlPerformancePolicy.ShouldUseLexOnlySemanticClassification(SqlPerformancePolicy.SemanticFullParseCharLimit));
        Assert.True(SqlPerformancePolicy.ShouldUseLexOnlySemanticClassification(SqlPerformancePolicy.SemanticFullParseCharLimit + 1));
    }

    [Fact]
    public void ShouldSkipSemanticClassification_OnlyForExtremeSize()
    {
        Assert.False(SqlPerformancePolicy.ShouldSkipSemanticClassification(
            SqlPerformancePolicy.HugeScriptLineThreshold + 10_000,
            SqlPerformancePolicy.SemanticFullParseCharLimit + 10));
        Assert.True(SqlPerformancePolicy.ShouldSkipSemanticClassification(
            10,
            SqlPerformancePolicy.CheapLintOnlyCharLimit + 1));
    }

    [Fact]
    public void GetSemanticClassificationMode_ReturnsExpectedMode()
    {
        Assert.Equal(
            SemanticClassificationMode.FullImmediate,
            SqlPerformancePolicy.GetSemanticClassificationMode(100, 10_000));
        Assert.Equal(
            SemanticClassificationMode.ProgressiveFull,
            SqlPerformancePolicy.GetSemanticClassificationMode(SqlPerformancePolicy.LargeScriptLineThreshold + 1, 10_000));
        Assert.Equal(
            SemanticClassificationMode.LexOnly,
            SqlPerformancePolicy.GetSemanticClassificationMode(100, SqlPerformancePolicy.SemanticFullParseCharLimit + 1));
    }

    [Fact]
    public void IsSlow_UsesUxPerfBudgets()
    {
        Assert.False(SqlTypingPerfProbe.IsSlow("editor.highlight", 49));
        Assert.True(SqlTypingPerfProbe.IsSlow("editor.highlight", 50));
        Assert.True(SqlTypingPerfProbe.IsSlow("editor.doc_change", 80));
        Assert.True(SqlTypingPerfProbe.IsSlow("editor.ext_lint", 100));
    }
}

public sealed class IncrementalValidationHelpersTests
{
    [Fact]
    public void ShouldUseIncrementalValidation_FalseWhenNoPrevious()
    {
        var next = StatementIndexBuilder.BuildIndex("SELECT 1; SELECT 2;");
        var dirty = next.Statements.Select(s => s.Index).ToArray();
        Assert.False(StatementIndexBuilder.ShouldUseIncrementalValidation(null, next, dirty));
    }

    [Fact]
    public void ShouldUseIncrementalValidation_TrueWhenMinorityDirty()
    {
        var previous = StatementIndexBuilder.BuildIndex("SELECT 1; SELECT 2; SELECT 3; SELECT 4;");
        var next = StatementIndexBuilder.BuildIndex("SELECT 9; SELECT 2; SELECT 3; SELECT 4;");
        var diff = StatementIndexBuilder.DiffIndexes(previous, next);
        Assert.True(StatementIndexBuilder.ShouldUseIncrementalValidation(previous, next, diff.DirtyIndices));
    }

    [Fact]
    public void CollectCachedStatementDiagnostics_SkipsDirtyStatements()
    {
        using var session = new DocumentValidationSession();
        const string uri = "file:///incremental.sql";
        var index = StatementIndexBuilder.BuildIndex("SELECT 1; SELECT 2; SELECT 3;");
        session.CommitDocumentIndex(uri, index);

        foreach (var statement in index.Statements)
        {
            session.StoreStatementDiagnostics(uri, statement, [
                new LintIssue("T", $"issue-{statement.Index}", LintSeverity.Warning, 0, 1, 1, 1)
            ]);
        }

        var cached = session.CollectCachedStatementDiagnostics(uri, index, dirtyIndices: [1]);
        Assert.Equal(2, cached.Count);
        Assert.True(cached.ContainsKey(0));
        Assert.True(cached.ContainsKey(2));
        Assert.False(cached.ContainsKey(1));
    }
}

public sealed class LargeScriptTypingPerformanceTests
{
    [Fact]
    public void BuildIndex_HundredsOfCtes_CompletesWithinBudget()
    {
        string sql = BuildManyCteScript(250);
        var sw = Stopwatch.StartNew();
        var index = StatementIndexBuilder.BuildIndex(sql);
        sw.Stop();

        Assert.True(index.Statements.Count >= 1);
        Assert.True(sw.ElapsedMilliseconds < 750, $"BuildIndex took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void DiffIndexes_SingleStatementEdit_MarksMinorityDirty()
    {
        string baseSql = BuildManyStatementScript(40);
        var previous = StatementIndexBuilder.BuildIndex(baseSql);
        string edited = previous.Statements[0].Sql.Replace("SELECT 0", "SELECT 99", StringComparison.Ordinal)
            + ";"
            + string.Join(";", previous.Statements.Skip(1).Select(s => s.Sql));
        var next = StatementIndexBuilder.BuildIndex(edited);
        var diff = StatementIndexBuilder.DiffIndexes(previous, next);

        Assert.True(diff.DirtyIndices.Count > 0);
        Assert.True(StatementIndexBuilder.ShouldUseIncrementalValidation(previous, next, diff.DirtyIndices));
        Assert.True(diff.DirtyIndices.Count <= next.Statements.Count / 2);
    }

    [Fact]
    public void RunCheapRules_LargeScript_StaysWithinBudget()
    {
        using var engine = new LintEngine();
        string sql = BuildManyCteScript(200);
        var sw = Stopwatch.StartNew();
        var issues = engine.RunCheapRules(sql);
        sw.Stop();

        Assert.NotNull(issues);
        Assert.True(sw.ElapsedMilliseconds < 1_500, $"RunCheapRules took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void RunFullLint_HugeScript_UsesCheapPathOnly()
    {
        using var engine = new LintEngine();
        // Force the cheap-only gate via line count without needing a schema.
        string sql = string.Join("\n", Enumerable.Range(0, SqlPerformancePolicy.HugeScriptLineThreshold + 50)
            .Select(i => $"-- line {i}"));
        var result = engine.RunFullLint(new LintConfig(sql, Schema: null, DocumentUri: "huge.sql"));
        Assert.False(result.UsedCache);
        Assert.Equal(0, result.ParserErrorCount);
        Assert.Equal(0, result.VisitorErrorCount);
    }

    private static string BuildManyCteScript(int cteCount)
    {
        var parts = new List<string>(cteCount + 1) { "WITH" };
        for (int i = 0; i < cteCount; i++)
        {
            string comma = i == cteCount - 1 ? string.Empty : ",";
            parts.Add($"cte{i} AS (SELECT {i} AS id FROM _v_dual){comma}");
        }

        parts.Add("SELECT * FROM cte0;");
        return string.Join("\n", parts);
    }

    private static string BuildManyStatementScript(int statementCount) =>
        string.Join(";\n", Enumerable.Range(0, statementCount).Select(i => $"SELECT {i}"));
}
