using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(JustyBase.NetezzaSql.Benchmarks.ParserBenchmarks).Assembly).Run(args);
