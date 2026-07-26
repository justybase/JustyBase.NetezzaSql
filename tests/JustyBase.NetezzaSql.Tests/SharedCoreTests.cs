using JustyBase.Core.Risk;
using JustyBase.Core.Scripting;
using JustyBase.Core.Credentials;
using JustyBase.Core.Execution;
using JustyBase.Core.Grid;
using JustyBase.Core.Schema;

namespace JustyBase.NetezzaSql.Tests;

public sealed class SharedCoreTests
{
    [Fact]
    public void RiskAnalyzer_detects_all_Netezza_run_gates_but_ignores_literals_and_comments()
    {
        var risks = new SqlRiskAnalysisService().Analyze("""
            UPDATE T SET C = 'WHERE';
            -- DELETE FROM T WHERE ID = 1
            SELECT * INTO BACKUP FROM T;
            CREATE TEMP TABLE X (ID INT);
            """, "NetezzaSQL");

        Assert.Contains(risks, risk => risk.Kind == SqlRiskKind.UnsafeUpdateDelete && risk.IsBlocking);
        Assert.Contains(risks, risk => risk.Kind == SqlRiskKind.SelectInto && risk.IsBlocking);
        Assert.Contains(risks, risk => risk.Kind == SqlRiskKind.MissingDistribute && risk.IsBlocking);
    }

    [Fact]
    public void AvaloniaDialect_normalizes_legacy_directives_and_collects_sleep()
    {
        var result = new AvaloniaScriptDialect().Process(new ScriptPreprocessRequest(
            "___sleep 25\n__SessionVar__ $name = 'Ada';\nSELECT '&name', &name;",
            NormalizeLegacyDirectives: true));

        Assert.Equal(TimeSpan.FromMilliseconds(25), Assert.Single(result.Delays));
        Assert.Equal("'Ada'", result.Variables["name"]);
        Assert.Contains("SELECT 'Ada', 'Ada'", result.ProcessedSql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecutionRunner_batches_and_continues_after_backend_failure()
    {
        var runner = new SqlExecutionRunner(new TestBackend());
        var events = new List<SqlExecutionEvent>();
        await foreach (var item in runner.RunAsync(new SqlExecutionRequest(["SELECT 1", "SELECT failure"], ContinueOnError: true)))
            events.Add(item);

        Assert.Equal(2, events.OfType<SqlExecutionEvent.Started>().Count());
        Assert.Contains(events, item => item is SqlExecutionEvent.Batch);
        Assert.Single(events.OfType<SqlExecutionEvent.Failed>());
        Assert.Single(events.OfType<SqlExecutionEvent.Completed>());
    }

    [Fact]
    public async Task Dual_credentials_migrate_legacy_profile_once()
    {
        var primary = new InMemoryCredentialStore();
        var legacy = new InMemoryCredentialStore();
        await legacy.WriteAsync(new CredentialProfile("dev", "user", "secret"));
        var store = new DualCredentialStore(primary, legacy);

        Assert.True(await store.MigrateAsync("dev"));
        Assert.False(await store.MigrateAsync("dev"));
        Assert.Equal("user", (await store.ReadAsync("dev"))!.UserName);
    }

    [Fact]
    public void Schema_cache_merges_objects_until_ttl_and_cell_stats_are_numeric()
    {
        var cache = new SchemaCache(TimeSpan.FromMinutes(1));
        cache.Merge(new SchemaSnapshot("DB", "PUBLIC", ["A"], DateTimeOffset.UtcNow));
        var merged = cache.Merge(new SchemaSnapshot("DB", "PUBLIC", ["B"], DateTimeOffset.UtcNow));
        Assert.Equal(["A", "B"], merged.Objects);
        Assert.True(cache.TryGet("DB", "PUBLIC", out _));

        CellStats stats = CellStatsCalculator.Calculate([1, 2.5m, null, DBNull.Value]);
        Assert.Equal(4, stats.Count);
        Assert.Equal(2, stats.NumericCount);
        Assert.Equal(2, stats.DistinctCount);
        Assert.Equal(3.5m, stats.Sum);
        Assert.Equal(1m, stats.Minimum);
        Assert.Equal(2.5m, stats.Maximum);
    }

    private sealed class TestBackend : ISqlExecutionBackend
    {
        public async IAsyncEnumerable<SqlExecutionEvent> ExecuteAsync(
            int statementIndex,
            string sql,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (sql.Contains("failure", StringComparison.Ordinal))
                throw new InvalidOperationException("synthetic failure");

            yield return new SqlExecutionEvent.Batch(statementIndex, sql, [[1]]);
            await Task.CompletedTask;
        }
    }
}
