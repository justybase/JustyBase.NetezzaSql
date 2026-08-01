using System.Diagnostics;
using JustyBase.NetezzaSqlParser.Caching;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Lexer;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed class ParserPerformanceTests
{
    private const string RepresentativeSql =
        "WITH c AS (SELECT id FROM source WHERE status = 'A') " +
        "SELECT c.id FROM c JOIN target t ON t.id = c.id WHERE c.id > 10;";

    [Theory]
    [InlineData(SqlDialect.Netezza)]
    [InlineData(SqlDialect.Oracle)]
    [InlineData(SqlDialect.Db2)]
    public void ParserConstruction_StaysWithinBudget(SqlDialect dialect)
    {
        var tokens = DialectRuntime.Tokenize(RepresentativeSql, dialect).ToArray();
        var stopwatch = Stopwatch.StartNew();

        _ = DialectRuntime.CreateParser(tokens, dialect);

        stopwatch.Stop();
        Console.WriteLine($"[parser-perf] {dialect} construction: {stopwatch.Elapsed.TotalMilliseconds:F2} ms");
        Assert.True(stopwatch.ElapsedMilliseconds < 100,
            $"{dialect} parser construction took {stopwatch.Elapsed.TotalMilliseconds:F2} ms");
    }

    [Theory]
    [InlineData(SqlDialect.Netezza)]
    [InlineData(SqlDialect.Oracle)]
    [InlineData(SqlDialect.Db2)]
    public void WarmParse_StaysWithinBudget(SqlDialect dialect)
    {
        using var runtime = new ParsingRuntime(dialect);
        _ = runtime.Parse(RepresentativeSql);

        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < 100; i++)
            _ = runtime.Parse(RepresentativeSql);
        stopwatch.Stop();

        var averageMs = stopwatch.Elapsed.TotalMilliseconds / 100d;
        Console.WriteLine($"[parser-perf] {dialect} cached parse average: {averageMs:F3} ms");
        Assert.True(averageMs < 10,
            $"{dialect} cached parse average was {averageMs:F3} ms");
    }
}
