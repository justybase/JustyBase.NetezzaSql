using System.Diagnostics;
using JustyBase.NetezzaSqlParser.Authoring;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed class SqlTypingResponsivenessTests
{
    private const int TypingEvents = 200;

    [Fact]
    public void ClassifyFull_SmallDocument_EmitsSemanticRoles()
    {
        var classifier = new NzSemanticTokenClassifier(SqlTestHelpers.CreateStandardMockSchema());
        const string sql = "SELECT e.employee_id FROM employees e WHERE e.status = 'A';";

        var spans = classifier.ClassifyFull(sql, "small-semantic", SqlPerformancePolicy.CountLines(sql));

        Assert.Contains(spans, span => span.Kind == SemanticTokenKind.Table);
        Assert.Contains(spans, span => span.Kind == SemanticTokenKind.Column);
    }

    [Fact]
    public void ClassifyLex_BigFixture_ReturnsTokens()
    {
        var classifier = new NzSemanticTokenClassifier(SqlTestHelpers.CreateStandardMockSchema());
        string sql = SqlPerfFixtureLoader.LoadBigSql();

        var spans = classifier.ClassifyLex(sql, "big-fixture", SqlPerformancePolicy.CountLines(sql));

        Assert.NotEmpty(spans);
        Assert.Contains(spans, span => span.Kind == SemanticTokenKind.Keyword);
    }

    [Fact]
    public void TypingStream_LexOnly_BigFixture_StaysWithinBudget()
    {
        var classifier = new NzSemanticTokenClassifier(SqlTestHelpers.CreateStandardMockSchema());
        string sql = SqlPerfFixtureLoader.LoadBigSql();

        var (maxMs, avgMs) = MeasureTypingStream(classifier, sql, "typing-big");

        Assert.True(maxMs < 80, $"Max event took {maxMs}ms");
        Assert.True(avgMs < 40, $"Avg event took {avgMs:F2}ms");
    }

    [Fact]
    public void TypingStream_LexOnly_HugeSynthetic_StaysWithinBudget()
    {
        var classifier = new NzSemanticTokenClassifier(SqlTestHelpers.CreateStandardMockSchema());
        string sql = BuildHugeSyntheticSql(SqlPerformancePolicy.CheapLintOnlyCharLimit + 1_000);

        var (maxMs, avgMs) = MeasureTypingStream(classifier, sql, "typing-huge");

        Assert.True(maxMs < 150, $"Max event took {maxMs}ms");
        Assert.True(avgMs < 80, $"Avg event took {avgMs:F2}ms");
    }

    [Fact]
    public void Classify_CacheHit_RepeatedDocumentVersion_IsFast()
    {
        var classifier = new NzSemanticTokenClassifier(SqlTestHelpers.CreateStandardMockSchema());
        const string sql = "SELECT 1 FROM employees WHERE status = 'A';";
        _ = classifier.Classify(sql, "cache-hit", SqlPerformancePolicy.CountLines(sql));

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 500; i++)
            _ = classifier.Classify(sql, "cache-hit", 1);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 100, $"Cache-hit loop took {sw.ElapsedMilliseconds}ms");
    }

    private static (long MaxMs, double AvgMs) MeasureTypingStream(NzSemanticTokenClassifier classifier, string sql, string docUri)
    {
        long maxMs = 0;
        long sumMs = 0;
        int lineCount = SqlPerformancePolicy.CountLines(sql);
        string current = sql;
        _ = classifier.ClassifyLex(current, docUri, lineCount);
        for (int i = 0; i < TypingEvents; i++)
        {
            char suffix = (char)('a' + (i % 26));
            current = current + suffix;
            var sw = Stopwatch.StartNew();
            _ = classifier.ClassifyLex(current, docUri, lineCount);
            sw.Stop();
            maxMs = Math.Max(maxMs, sw.ElapsedMilliseconds);
            sumMs += sw.ElapsedMilliseconds;
        }

        return (maxMs, sumMs / (double)TypingEvents);
    }

    private static string BuildHugeSyntheticSql(int minChars)
    {
        const string statement = "SELECT col1, col2 FROM employees WHERE status = 'A';\n";
        var text = statement;
        while (text.Length < minChars)
            text += statement;
        return text;
    }

    private static class SqlPerfFixtureLoader
    {
        public static string LoadBigSql()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "BIG.SQL");
            Assert.True(File.Exists(path), $"Missing BIG.SQL fixture at {path}");
            return File.ReadAllText(path);
        }
    }
}
