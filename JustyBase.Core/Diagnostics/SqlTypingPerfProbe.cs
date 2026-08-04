using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;

namespace JustyBase.Core.Diagnostics;

/// <summary>
/// Environment-gated SQL typing UX probe shared by all JustyBase editor hosts
/// (Avalonia SqlEditor, WinForms FCTB, LSP). Single canonical implementation —
/// no per-host copies.
/// Enable with env <c>JUSTYBASE_SQL_TYPING_PERF=1</c> (optional path via
/// <c>JUSTYBASE_SQL_TYPING_PERF_LOG</c>). Writes NDJSON lines to
/// %LocalAppData%\JustyBase\perf\sql-typing-perf-*.ndjson (AutoFlush so a FlaUI
/// driver can wait on lines between keystrokes) and emits a session summary on
/// process exit.
/// </summary>
public sealed class SqlTypingPerfProbe
{
    public const string EnvEnable = "JUSTYBASE_SQL_TYPING_PERF";
    public const string EnvLogPath = "JUSTYBASE_SQL_TYPING_PERF_LOG";

    // Budgets aligned with uxPerfThresholds.ts (editor UX targets).
    public const int DocChangeBudgetMs = 50;
    public const int TypingBurstBudgetMs = 80;
    public const int HighlightBudgetMs = 50;
    public const int SemanticBudgetMs = 50;
    public const int ChangeToTokensBudgetMs = 200;
    public const int ExtLintBudgetMs = 100;
    public const int AutocompleteBudgetMs = 80;
    public const int InterKeySlowMs = 80;
    public const int InterKeyGapMaxMs = 1_000;
    public const int DocChangeSampleEvery = 25;

    // Mirrors SqlPerformancePolicy.LargeScript* so Core stays parser-agnostic.
    public const int LargeScriptLineThreshold = 500;
    public const int LargeScriptCharThreshold = 150_000;

    public static SqlTypingPerfProbe Instance { get; } = new();

    private readonly object _lock = new();
    private readonly Dictionary<string, long> _lastDocChangeAtMs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _docChangeCounters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TypingBurst> _bursts = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, OpStats> _stats = new(StringComparer.Ordinal);
    private StreamWriter? _writer;
    private string? _logPath;
    private int _uiThreadId = -1;
    private bool _enabled;
    private bool _initialized;

    public bool Enabled
    {
        get
        {
            EnsureInitialized();
            return _enabled;
        }
        set
        {
            EnsureInitialized();
            _enabled = value;
        }
    }

    public string? LogFilePath
    {
        get
        {
            EnsureInitialized();
            return _logPath;
        }
    }

    public void EnsureInitialized()
    {
        if (_initialized)
            return;

        lock (_lock)
        {
            if (_initialized)
                return;

            string? flag = Environment.GetEnvironmentVariable(EnvEnable);
            _enabled = string.Equals(flag, "1", StringComparison.Ordinal)
                       || string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(flag, "yes", StringComparison.OrdinalIgnoreCase);

            if (_enabled)
            {
                string? path = Environment.GetEnvironmentVariable(EnvLogPath);
                if (string.IsNullOrWhiteSpace(path))
                {
                    string dir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "JustyBase",
                        "perf");
                    Directory.CreateDirectory(dir);
                    path = Path.Combine(dir, $"sql-typing-perf-{DateTime.Now:yyyyMMdd-HHmmss}.ndjson");
                }

                try
                {
                    _writer = new StreamWriter(path, append: true, Encoding.UTF8)
                    {
                        // Visible immediately — FlaUI waits on NDJSON lines between keystrokes.
                        AutoFlush = true
                    };
                    _logPath = path;
                    _uiThreadId = Environment.CurrentManagedThreadId;
                    Trace.WriteLine($"[SqlTypingPerf] enabled log={path}");
                    AppDomain.CurrentDomain.ProcessExit += (_, _) =>
                    {
                        try { WriteSessionSummary(); } catch { /* ignore */ }
                        try { _writer?.Dispose(); } catch { /* ignore */ }
                    };
                }
                catch (Exception ex)
                {
                    _enabled = false;
                    Trace.WriteLine($"[SqlTypingPerf] init failed: {ex.GetType().Name}: {ex.Message}");
                }
            }

            _initialized = true;
        }
    }

    public void MarkDocChange(string documentKey, int chars, int lines, int? changedChars = null)
    {
        if (!Enabled)
            return;

        long now = Environment.TickCount64;
        long? interKeyMs;
        bool idleGap;
        int count;
        lock (_lock)
        {
            idleGap = false;
            interKeyMs = null;
            if (_lastDocChangeAtMs.TryGetValue(documentKey, out long previous))
            {
                long gap = now - previous;
                if (gap > InterKeyGapMaxMs)
                {
                    idleGap = true;
                    FlushTypingBurst_NoLock(documentKey);
                }
                else
                {
                    interKeyMs = gap;
                }
            }

            _lastDocChangeAtMs[documentKey] = now;
            count = _docChangeCounters.TryGetValue(documentKey, out int existing) ? existing + 1 : 1;
            _docChangeCounters[documentKey] = count;

            if (!_bursts.TryGetValue(documentKey, out TypingBurst? burst) || idleGap)
            {
                burst = new TypingBurst();
                _bursts[documentKey] = burst;
            }

            burst.Keystrokes++;
            burst.Chars = chars;
            burst.Lines = lines;
            if (interKeyMs is long sample)
            {
                burst.InterKeySum += sample;
                burst.InterKeyMax = Math.Max(burst.InterKeyMax, sample);
                burst.InterKeySamples++;
            }
        }

        bool isLarge = IsLargeScriptDocument(lines, chars);
        bool typingLagSample = interKeyMs is long lag && lag >= InterKeySlowMs;
        bool shouldSample = count % DocChangeSampleEvery == 0 || typingLagSample;
        if (shouldSample)
        {
            Emit(
                "editor.doc_change",
                "sample",
                interKeyMs,
                documentKey,
                chars,
                lines,
                changedChars,
                meta: $"changeCount={count};charsDelta={changedChars?.ToString(CultureInfo.InvariantCulture) ?? "null"};isLarge={isLarge};interKeyMs={interKeyMs?.ToString(CultureInfo.InvariantCulture) ?? "null"};idleGap={(idleGap ? "1" : "0")}");
        }
    }

    public IDisposable Measure(
        string op,
        string phase = "end",
        string? documentKey = null,
        int chars = 0,
        int lines = 0,
        int? changedChars = null,
        string? meta = null)
        => new Scope(this, op, phase, documentKey, chars, lines, changedChars, meta);

    public void Emit(
        string op,
        string phase,
        long? durationMs,
        string? documentKey = null,
        int chars = 0,
        int lines = 0,
        int? changedChars = null,
        string? meta = null)
    {
        if (!Enabled)
            return;

        bool slow = IsSlow(op, durationMs);
        int threadId = Environment.CurrentManagedThreadId;
        bool isUi = _uiThreadId < 0 || threadId == _uiThreadId;

        var sb = new StringBuilder(256);
        sb.Append("{\"ts\":\"").Append(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)).Append('"');
        sb.Append(",\"op\":\"").Append(Escape(op)).Append('"');
        sb.Append(",\"phase\":\"").Append(Escape(phase)).Append('"');
        if (durationMs is long ms)
            sb.Append(",\"durationMs\":").Append(ms.ToString(CultureInfo.InvariantCulture));
        sb.Append(",\"slow\":").Append(slow ? "true" : "false");
        if (!string.IsNullOrEmpty(documentKey))
            sb.Append(",\"doc\":\"").Append(Escape(documentKey)).Append('"');
        if (chars > 0 || lines > 0)
        {
            sb.Append(",\"chars\":").Append(chars.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"lines\":").Append(lines.ToString(CultureInfo.InvariantCulture));
        }
        if (changedChars is int cc)
            sb.Append(",\"changedChars\":").Append(cc.ToString(CultureInfo.InvariantCulture));
        sb.Append(",\"threadId\":").Append(threadId.ToString(CultureInfo.InvariantCulture));
        sb.Append(",\"isUiThread\":").Append(isUi ? "true" : "false");
        if (!string.IsNullOrEmpty(meta))
            sb.Append(",\"meta\":\"").Append(Escape(meta)).Append('"');
        sb.Append('}');

        string line = sb.ToString();
        if (slow || string.Equals(phase, "end", StringComparison.Ordinal) || string.Equals(phase, "sample", StringComparison.Ordinal))
            Trace.WriteLine("[SqlTypingPerf] " + line);

        StreamWriter? writer = _writer;
        if (writer is not null)
        {
            try
            {
                lock (_lock)
                    writer.WriteLine(line);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[SqlTypingPerf] log write failed: {ex.GetType().Name}");
            }
        }

        if (string.Equals(phase, "end", StringComparison.Ordinal))
            RecordStat(op, durationMs);
    }

    public static bool IsSlow(string op, long? durationMs)
    {
        if (durationMs is not long ms)
            return false;

        int budget = op switch
        {
            "editor.doc_change" => DocChangeBudgetMs,
            "editor.typing_burst" => TypingBurstBudgetMs,
            "editor.highlight" => HighlightBudgetMs,
            "editor.semantic_tokens" => SemanticBudgetMs,
            "editor.change_to_tokens" => ChangeToTokensBudgetMs,
            "editor.ext_lint" => ExtLintBudgetMs,
            "editor.autocomplete" => AutocompleteBudgetMs,
            "editor.comment_scan" => HighlightBudgetMs,
            _ => HighlightBudgetMs
        };
        return ms >= budget;
    }

    /// <summary>
    /// Writes per-op statistics (count, sum, max, median, p95) as NDJSON
    /// session_summary lines. Called automatically on process exit.
    /// </summary>
    public void WriteSessionSummary()
    {
        StreamWriter? writer = _writer;
        if (!Enabled || writer is null)
            return;

        lock (_lock)
        {
            foreach (var pair in _stats.OrderByDescending(p => p.Value.MaxMs))
            {
                OpStats s = pair.Value;
                double p95 = Percentile(s.Samples, 0.95);
                double median = Percentile(s.Samples, 0.50);
                writer.WriteLine(
                    $"{{\"ts\":\"{DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)}\",\"op\":\"session_summary\",\"phase\":\"end\",\"opName\":{Json(pair.Key)},\"count\":{s.Count},\"sumMs\":{s.SumMs.ToString(CultureInfo.InvariantCulture)},\"maxMs\":{s.MaxMs.ToString(CultureInfo.InvariantCulture)},\"medianMs\":{median.ToString("0.###", CultureInfo.InvariantCulture)},\"p95Ms\":{p95.ToString("0.###", CultureInfo.InvariantCulture)}}}");
            }
        }
    }

    private void FlushTypingBurst_NoLock(string documentKey)
    {
        if (!_bursts.TryGetValue(documentKey, out TypingBurst? burst) || burst.Keystrokes == 0)
            return;

        long? avg = burst.InterKeySamples > 0
            ? burst.InterKeySum / burst.InterKeySamples
            : null;
        Emit(
            "editor.typing_burst",
            "end",
            burst.InterKeyMax > 0 ? burst.InterKeyMax : avg,
            documentKey,
            burst.Chars,
            burst.Lines,
            meta: $"keystrokes={burst.Keystrokes};interKeyAvg={avg?.ToString(CultureInfo.InvariantCulture) ?? "null"};interKeyMax={burst.InterKeyMax}");
        _bursts.Remove(documentKey);
    }

    private void RecordStat(string op, long? durationMs)
    {
        if (durationMs is not long ms)
            return;

        OpStats stats = _stats.GetOrAdd(op, static _ => new OpStats());
        lock (stats)
        {
            stats.Count++;
            stats.SumMs += ms;
            if (ms > stats.MaxMs)
                stats.MaxMs = ms;
            if (stats.Samples.Count < 4096)
                stats.Samples.Add(ms);
        }
    }

    private static bool IsLargeScriptDocument(int lineCount, int textLength) =>
        lineCount > LargeScriptLineThreshold || textLength > LargeScriptCharThreshold;

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);

    private static string Json(string value) => "\"" + Escape(value) + "\"";

    private static double Percentile(List<long> samples, double p)
    {
        if (samples.Count == 0)
            return 0;

        var sorted = samples.OrderBy(x => x).ToArray();
        double idx = (sorted.Length - 1) * p;
        int lo = (int)Math.Floor(idx);
        int hi = (int)Math.Ceiling(idx);
        if (lo == hi)
            return sorted[lo];
        double w = idx - lo;
        return sorted[lo] * (1 - w) + sorted[hi] * w;
    }

    private sealed class TypingBurst
    {
        public int Keystrokes;
        public long InterKeySum;
        public long InterKeyMax;
        public int InterKeySamples;
        public int Chars;
        public int Lines;
    }

    private sealed class OpStats
    {
        public int Count;
        public long SumMs;
        public long MaxMs;
        public List<long> Samples { get; } = new();
    }

    private sealed class Scope : IDisposable
    {
        private readonly SqlTypingPerfProbe _probe;
        private readonly string _op;
        private readonly string _phase;
        private readonly string? _documentKey;
        private readonly int _chars;
        private readonly int _lines;
        private readonly int? _changedChars;
        private readonly string? _meta;
        private readonly long _started;
        private int _disposed;

        public Scope(SqlTypingPerfProbe probe, string op, string phase, string? documentKey, int chars, int lines, int? changedChars, string? meta)
        {
            _probe = probe;
            _op = op;
            _phase = phase;
            _documentKey = documentKey;
            _chars = chars;
            _lines = lines;
            _changedChars = changedChars;
            _meta = meta;
            _started = Environment.TickCount64;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
                return;

            _probe.Emit(_op, _phase, Environment.TickCount64 - _started, _documentKey, _chars, _lines, _changedChars, _meta);
        }
    }
}
