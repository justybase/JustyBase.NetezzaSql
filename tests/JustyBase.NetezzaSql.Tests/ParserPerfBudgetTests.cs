using System.Diagnostics;
using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.NetezzaSqlParser.Caching;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSql.Tests.Performance;
using Superpower.Model;
using Xunit.Abstractions;

namespace JustyBase.NetezzaSql.Tests;

/// <summary>
/// Performance budgets for the generated ~1 MB fixtures. Budgets are hard gates
/// with deliberate headroom for shared CI runners; measured values are printed
/// through the test output so regressions can be diagnosed from CI logs.
/// These tests are filtered by <c>Category=Performance</c> in fast local runs
/// and executed as an explicit CI gate.
/// </summary>
[Trait("Category", "Performance")]
public sealed class ParserPerfBudgetTests
{
    private const int Warmup = 1;
    private const int Iterations = 2;

    // Calibrated against the generated fixtures; keep ~3-5x headroom over the
    // slowest CI-class runner and tighten only after repeated green runs.
    private static class Budgets
    {
        public const double TokenizeMs = 15_000;
        public const double ParseAllMs = 500;
        public const double WarmParseMs = 50;
    }

    private readonly ITestOutputHelper _output;

    public ParserPerfBudgetTests(ITestOutputHelper output) => _output = output;

    public static IEnumerable<object[]> Fixtures()
    {
        yield return new object[] { PerfFixtureFactory.Ddl, SqlDialect.Netezza };
        yield return new object[] { PerfFixtureFactory.Dml, SqlDialect.Netezza };
        yield return new object[] { PerfFixtureFactory.Complex, SqlDialect.Netezza };
        yield return new object[] { PerfFixtureFactory.Ddl, SqlDialect.Oracle };
        yield return new object[] { PerfFixtureFactory.Ddl, SqlDialect.Db2 };
    }

    public static IEnumerable<object[]> NetezzaFixtures()
    {
        yield return new object[] { PerfFixtureFactory.Ddl, SqlDialect.Netezza };
        yield return new object[] { PerfFixtureFactory.Dml, SqlDialect.Netezza };
        yield return new object[] { PerfFixtureFactory.Complex, SqlDialect.Netezza };
    }

    [Theory]
    [MemberData(nameof(NetezzaFixtures))]
    public void Tokenize_OneMegabyteFixture_StaysWithinBudget(string kind, SqlDialect dialect)
    {
        var sql = PerfFixtureFactory.Get(kind);

        var average = MeasureMilliseconds(() => DialectRuntime.Tokenize(sql, dialect).Count());

        _output.WriteLine($"[perf] tokenize {kind}/{dialect}: {average:F1} ms (budget {Budgets.TokenizeMs} ms)");
        Assert.True(
            average < Budgets.TokenizeMs,
            $"tokenize {kind}/{dialect} averaged {average:F1} ms, above the {Budgets.TokenizeMs} ms budget.");
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void ParseAllStatements_OneMegabyteFixture_StaysWithinBudget(string kind, SqlDialect dialect)
    {
        var sql = PerfFixtureFactory.Get(kind);
        var tokens = DialectRuntime.Tokenize(sql, dialect).ToArray();

        var (statements, errors) = (0, 0);
        var average = MeasureMilliseconds(() => (statements, errors) = ParseAllStatements(tokens, dialect));

        _output.WriteLine(
            $"[perf] parse {kind}/{dialect}: {average:F1} ms (budget {Budgets.ParseAllMs} ms), " +
            $"statements={statements}, errors={errors}");

        Assert.True(errors == 0, $"{kind}/{dialect} fixture drifted: {errors} parser errors across {statements} statements.");
        Assert.True(statements > 100, $"{kind}/{dialect} parsed only {statements} statements.");
        Assert.True(
            average < Budgets.ParseAllMs,
            $"parse {kind}/{dialect} averaged {average:F1} ms, above the {Budgets.ParseAllMs} ms budget.");
    }

    [Theory]
    [MemberData(nameof(NetezzaFixtures))]
    public void WarmParse_OneMegabyteFixture_StaysWithinBudget(string kind, SqlDialect dialect)
    {
        var sql = PerfFixtureFactory.Get(kind);
        using var runtime = new ParsingRuntime(dialect);
        runtime.Parse(sql);

        var average = MeasureMilliseconds(() => runtime.Parse(sql));

        _output.WriteLine($"[perf] warm parse {kind}/{dialect}: {average:F1} ms (budget {Budgets.WarmParseMs} ms)");
        Assert.True(
            average < Budgets.WarmParseMs,
            $"warm parse {kind}/{dialect} averaged {average:F1} ms, above the {Budgets.WarmParseMs} ms budget.");
    }

    [Fact]
    public void OneMegabyteFixture_TriggersDocumentedDegradation()
    {
        var sql = PerfFixtureFactory.Get(PerfFixtureFactory.Complex);
        var lineCount = SqlPerformancePolicy.CountLines(sql);

        Assert.True(sql.Length > SqlPerformancePolicy.LargeScriptCharThreshold);
        Assert.True(SqlPerformancePolicy.ShouldSkipFullParse(lineCount, sql.Length));
        Assert.True(SqlPerformancePolicy.ShouldSkipSemanticClassification(lineCount, sql.Length));
    }

    private static (int Statements, int Errors) ParseAllStatements(Token<NzToken>[] tokens, SqlDialect dialect)
    {
        var parser = DialectRuntime.CreateParser(tokens, dialect);
        var statements = 0;

        while (parser.Position < tokens.Length)
        {
            var positionBefore = parser.Position;
            parser.Parse();
            statements++;

            if (parser.Position <= positionBefore)
                break;
        }

        return (statements, parser.ErrorCount);
    }

    private static double MeasureMilliseconds(Action action)
    {
        for (var i = 0; i < Warmup; i++)
            action();

        var total = 0d;
        for (var i = 0; i < Iterations; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            action();
            stopwatch.Stop();
            total += stopwatch.Elapsed.TotalMilliseconds;
        }

        return total / Iterations;
    }
}
