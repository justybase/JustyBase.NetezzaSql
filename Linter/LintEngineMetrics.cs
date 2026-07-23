namespace JustyBase.NetezzaSqlParser.Linter;

/// <summary>
/// Snapshot of LintEngine performance metrics at a point in time.
/// </summary>
public readonly record struct LintMetricsSnapshot(
    long CheapRunCount,
    double CheapTotalTimeMs,
    double CheapAvgTimeMs,
    long ExpensiveRunCount,
    double ExpensiveTotalTimeMs,
    double ExpensiveAvgTimeMs,
    long CacheHitCount,
    long CacheMissCount,
    double CacheHitRatio
);

/// <summary>
/// Thread-safe performance metrics collector for LintEngine.
/// Tracks cache hit/miss counts and execution times for cheap vs. expensive analysis.
/// Use <see cref="Snapshot"/> to get a consistent point-in-time view.
/// Use <see cref="Reset"/> to clear all counters (e.g., when attaching to a new editor).
/// </summary>
public sealed class LintEngineMetrics
{
    private readonly object _lock = new();
    private long _cheapRunCount;
    private double _cheapTotalTimeMs;
    private long _expensiveRunCount;
    private double _expensiveTotalTimeMs;
    private long _cacheHitCount;
    private long _cacheMissCount;

    /// <summary>
    /// Record a completed cheap-rule run and its duration.
    /// </summary>
    public void RecordCheapRun(TimeSpan elapsed)
    {
        lock (_lock)
        {
            _cheapRunCount++;
            _cheapTotalTimeMs += elapsed.TotalMilliseconds;
        }
    }

    /// <summary>
    /// Record a completed expensive-analysis run, its duration, and whether it used cached results.
    /// </summary>
    public void RecordExpensiveRun(TimeSpan elapsed, bool cacheHit)
    {
        lock (_lock)
        {
            _expensiveRunCount++;
            _expensiveTotalTimeMs += elapsed.TotalMilliseconds;
            if (cacheHit)
                _cacheHitCount++;
            else
                _cacheMissCount++;
        }
    }

    /// <summary>
    /// Take a consistent point-in-time snapshot of all metrics.
    /// </summary>
    public LintMetricsSnapshot Snapshot()
    {
        lock (_lock)
        {
            var totalCalls = _cacheHitCount + _cacheMissCount;
            return new LintMetricsSnapshot(
                CheapRunCount: _cheapRunCount,
                CheapTotalTimeMs: _cheapTotalTimeMs,
                CheapAvgTimeMs: _cheapRunCount > 0
                    ? _cheapTotalTimeMs / _cheapRunCount
                    : 0,
                ExpensiveRunCount: _expensiveRunCount,
                ExpensiveTotalTimeMs: _expensiveTotalTimeMs,
                ExpensiveAvgTimeMs: _expensiveRunCount > 0
                    ? _expensiveTotalTimeMs / _expensiveRunCount
                    : 0,
                CacheHitCount: _cacheHitCount,
                CacheMissCount: _cacheMissCount,
                CacheHitRatio: totalCalls > 0
                    ? (double)_cacheHitCount / totalCalls
                    : 0
            );
        }
    }

    /// <summary>
    /// Clear all counters. Thread-safe.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _cheapRunCount = 0;
            _cheapTotalTimeMs = 0;
            _expensiveRunCount = 0;
            _expensiveTotalTimeMs = 0;
            _cacheHitCount = 0;
            _cacheMissCount = 0;
        }
    }
}
