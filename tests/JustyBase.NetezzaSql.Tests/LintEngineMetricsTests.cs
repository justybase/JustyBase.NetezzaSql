using JustyBase.NetezzaSqlParser.Linter;

namespace JustyBase.Tests.NetezzaSqlParser;

/// <summary>
/// Tests for LintEngineMetrics — verifies correct tracking of cache hits/misses,
/// cheap/expensive run timing, snapshot consistency, and thread safety.
/// </summary>
public sealed class LintEngineMetricsTests
{
    [Fact]
    public void InitialState_AllCountersAreZero()
    {
        var m = new LintEngineMetrics();
        var s = m.Snapshot();

        Assert.Equal(0, s.CheapRunCount);
        Assert.Equal(0, s.CheapTotalTimeMs);
        Assert.Equal(0, s.CheapAvgTimeMs);
        Assert.Equal(0, s.ExpensiveRunCount);
        Assert.Equal(0, s.ExpensiveTotalTimeMs);
        Assert.Equal(0, s.ExpensiveAvgTimeMs);
        Assert.Equal(0, s.CacheHitCount);
        Assert.Equal(0, s.CacheMissCount);
        Assert.Equal(0, s.CacheHitRatio);
    }

    [Fact]
    public void RecordCheapRun_IncreasesCountAndTime()
    {
        var m = new LintEngineMetrics();

        m.RecordCheapRun(TimeSpan.FromMilliseconds(10));
        m.RecordCheapRun(TimeSpan.FromMilliseconds(20));

        var s = m.Snapshot();
        Assert.Equal(2, s.CheapRunCount);
        Assert.Equal(30, s.CheapTotalTimeMs, 1); // 10 + 20
        Assert.Equal(15, s.CheapAvgTimeMs, 1);   // 30 / 2
    }

    [Fact]
    public void RecordExpensiveRun_TracksCacheHit()
    {
        var m = new LintEngineMetrics();

        m.RecordExpensiveRun(TimeSpan.FromMilliseconds(50), cacheHit: true);
        m.RecordExpensiveRun(TimeSpan.FromMilliseconds(100), cacheHit: false);
        m.RecordExpensiveRun(TimeSpan.FromMilliseconds(30), cacheHit: true);

        var s = m.Snapshot();
        Assert.Equal(3, s.ExpensiveRunCount);
        Assert.Equal(2, s.CacheHitCount);
        Assert.Equal(1, s.CacheMissCount);
        Assert.Equal(180, s.ExpensiveTotalTimeMs, 1);
        Assert.Equal(60, s.ExpensiveAvgTimeMs, 1);
        Assert.Equal(2.0 / 3.0, s.CacheHitRatio, 4);
    }

    [Fact]
    public void Snapshot_ReturnsConsistentSnapshot()
    {
        var m = new LintEngineMetrics();

        m.RecordCheapRun(TimeSpan.FromMilliseconds(5));
        m.RecordCheapRun(TimeSpan.FromMilliseconds(15));
        m.RecordExpensiveRun(TimeSpan.FromMilliseconds(20), cacheHit: true);

        var s = m.Snapshot();

        // Verify all values are consistent
        Assert.Equal(2, s.CheapRunCount);
        Assert.Equal(20, s.CheapTotalTimeMs, 1);
        Assert.Equal(10, s.CheapAvgTimeMs, 1);
        Assert.Equal(1, s.ExpensiveRunCount);
        Assert.Equal(20, s.ExpensiveTotalTimeMs, 1);
        Assert.Equal(20, s.ExpensiveAvgTimeMs, 1);
        Assert.Equal(1, s.CacheHitCount);
        Assert.Equal(0, s.CacheMissCount);
        Assert.Equal(1.0, s.CacheHitRatio, 4);
    }

    [Fact]
    public void Reset_ClearsAllCounters()
    {
        var m = new LintEngineMetrics();

        m.RecordCheapRun(TimeSpan.FromMilliseconds(10));
        m.RecordExpensiveRun(TimeSpan.FromMilliseconds(20), cacheHit: true);
        m.Reset();

        var s = m.Snapshot();
        Assert.Equal(0, s.CheapRunCount);
        Assert.Equal(0, s.CheapTotalTimeMs);
        Assert.Equal(0, s.ExpensiveRunCount);
        Assert.Equal(0, s.CacheHitCount);
        Assert.Equal(0, s.CacheMissCount);
    }

    [Fact]
    public void RecordExpensiveRun_AllCacheMisses_HitRatioIsZero()
    {
        var m = new LintEngineMetrics();

        m.RecordExpensiveRun(TimeSpan.FromMilliseconds(10), cacheHit: false);
        m.RecordExpensiveRun(TimeSpan.FromMilliseconds(20), cacheHit: false);

        var s = m.Snapshot();
        Assert.Equal(2, s.CacheMissCount);
        Assert.Equal(0, s.CacheHitCount);
        Assert.Equal(0.0, s.CacheHitRatio, 4);
    }

    [Fact]
    public void RecordExpensiveRun_AllCacheHits_HitRatioIsOne()
    {
        var m = new LintEngineMetrics();

        m.RecordExpensiveRun(TimeSpan.FromMilliseconds(10), cacheHit: true);
        m.RecordExpensiveRun(TimeSpan.FromMilliseconds(20), cacheHit: true);
        m.RecordExpensiveRun(TimeSpan.FromMilliseconds(30), cacheHit: true);

        var s = m.Snapshot();
        Assert.Equal(3, s.CacheHitCount);
        Assert.Equal(0, s.CacheMissCount);
        Assert.Equal(1.0, s.CacheHitRatio, 4);
    }

    [Fact]
    public void NoRuns_AvgTimeIsZero()
    {
        var m = new LintEngineMetrics();
        var s = m.Snapshot();

        Assert.Equal(0, s.CheapAvgTimeMs);
        Assert.Equal(0, s.ExpensiveAvgTimeMs);
    }
}
