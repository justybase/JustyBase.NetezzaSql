using BenchmarkDotNet.Attributes;
using JustyBase.NetezzaSql.Tests.Performance;
using JustyBase.NetezzaSqlParser.Caching;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Lexer;
using Superpower.Model;

namespace JustyBase.NetezzaSql.Benchmarks;

/// <summary>
/// Detailed lexer/parser benchmarks for the generated ~1 MB fixtures.
/// Run locally with <c>pwsh .\eng\Run-ParserBenchmarks.ps1</c>; the hard CI
/// gate lives in <c>ParserPerfBudgetTests</c>.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 1, iterationCount: 3)]
public class ParserBenchmarks
{
    [Params(PerfFixtureFactory.Ddl, PerfFixtureFactory.Dml, PerfFixtureFactory.Complex)]
    public string Kind = PerfFixtureFactory.Ddl;

    private string _sql = string.Empty;
    private Token<NzToken>[] _tokens = Array.Empty<Token<NzToken>>();
    private ParsingRuntime _runtime = null!;

    [GlobalSetup]
    public void Setup()
    {
        _sql = PerfFixtureFactory.Get(Kind);
        _tokens = DialectRuntime.Tokenize(_sql, SqlDialect.Netezza).ToArray();
        _runtime = new ParsingRuntime(SqlDialect.Netezza);
        _runtime.Parse(_sql);
    }

    [GlobalCleanup]
    public void Cleanup() => _runtime.Dispose();

    [Benchmark]
    public int Tokenize() => DialectRuntime.Tokenize(_sql, SqlDialect.Netezza).Count();

    [Benchmark]
    public int ParseAllStatements()
    {
        var parser = DialectRuntime.CreateParser(_tokens, SqlDialect.Netezza);
        var statements = 0;

        while (parser.Position < _tokens.Length)
        {
            var positionBefore = parser.Position;
            parser.Parse();
            statements++;

            if (parser.Position <= positionBefore)
                break;
        }

        return statements;
    }

    [Benchmark]
    public ParseResult WarmCachedParse() => _runtime.Parse(_sql);
}
