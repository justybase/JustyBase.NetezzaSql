using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace JustyBase.NetezzaSqlParser.Authoring;

/// <summary>
/// Temporary SQL typing UX probe modeled on justybase-vscode-private uxPerfSession.
/// Enable with env <c>JUSTYBASE_SQL_TYPING_PERF=1</c> (optional path via <c>JUSTYBASE_SQL_TYPING_PERF_LOG</c>).
/// </summary>
public sealed class SqlTypingPerfProbe
{
    public const string EnvEnable = "JUSTYBASE_SQL_TYPING_PERF";
    public const string EnvLogPath = "JUSTYBASE_SQL_TYPING_PERF_LOG";

    // Budgets aligned with uxPerfThresholds.ts
    public const int DocChangeBudgetMs = 50;
    public const int TypingBurstBudgetMs = 80;
    public const int HighlightBudgetMs = 50;
    public const int SemanticBudgetMs = 50;
    public const int ChangeToTokensBudgetMs = 200;
    public const int ExtLintBudgetMs = 100;
    public const int AutocompleteBudgetMs = 80;
    public const int InterKeySlowMs = 80;
    public const int InterKeyGapMaxMs = 1_000;
    public const int TypingBurstIdleMs = 300;
    public const int DocChangeSampleEvery = 25;

    public static SqlTypingPerfProbe Instance { get; } = new();

    private readonly object _lock = new();
    private readonly Dictionary<string, long> _lastDocChangeAtMs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _docChangeCounters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TypingBurst> _bursts = new(StringComparer.Ordinal);
    private string? _logPath;
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

            _logPath = Environment.GetEnvironmentVariable(EnvLogPath);
            if (_enabled && string.IsNullOrWhiteSpace(_logPath))
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "JustyBase",
                    "perf");
                Directory.CreateDirectory(dir);
                _logPath = Path.Combine(dir, $"sql-typing-perf-{DateTime.Now:yyyyMMdd-HHmmss}.ndjson");
            }

            _initialized = true;
            if (_enabled)
            {
                Trace.WriteLine($"[SqlTypingPerf] enabled log={_logPath}");
            }
        }
    }

    public void MarkDocChange(string documentKey, int chars, int lines, int? charsDelta = null)
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

        bool isLarge = SqlPerformancePolicy.IsLargeScriptDocument(lines, chars);
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
                meta: $"changeCount={count};charsDelta={charsDelta?.ToString(CultureInfo.InvariantCulture) ?? "null"};isLarge={isLarge};interKeyMs={interKeyMs?.ToString(CultureInfo.InvariantCulture) ?? "null"};idleGap={(idleGap ? "1" : "0")}");
        }
    }

    public IDisposable Measure(string op, string phase, string? documentKey = null, int chars = 0, int lines = 0, string? meta = null)
        => new Scope(this, op, phase, documentKey, chars, lines, meta);

    public void Emit(string op, string phase, long? durationMs, string? documentKey = null, int chars = 0, int lines = 0, string? meta = null)
    {
        if (!Enabled)
            return;

        bool slow = IsSlow(op, durationMs);
        var sb = new StringBuilder(192);
        sb.Append("{\"ts\":\"").Append(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)).Append('"');
        sb.Append(",\"op\":\"").Append(op).Append('"');
        sb.Append(",\"phase\":\"").Append(phase).Append('"');
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
        if (!string.IsNullOrEmpty(meta))
            sb.Append(",\"meta\":\"").Append(Escape(meta)).Append('"');
        sb.Append('}');

        string line = sb.ToString();
        if (slow || string.Equals(phase, "end", StringComparison.Ordinal) || string.Equals(phase, "sample", StringComparison.Ordinal))
            Trace.WriteLine("[SqlTypingPerf] " + line);

        string? path = _logPath;
        if (!string.IsNullOrWhiteSpace(path))
        {
            try
            {
                File.AppendAllText(path, line + Environment.NewLine);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[SqlTypingPerf] log write failed: {ex.GetType().Name}");
            }
        }
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

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);

    private sealed class TypingBurst
    {
        public int Keystrokes;
        public long InterKeySum;
        public long InterKeyMax;
        public int InterKeySamples;
        public int Chars;
        public int Lines;
    }

    private sealed class Scope : IDisposable
    {
        private readonly SqlTypingPerfProbe _probe;
        private readonly string _op;
        private readonly string _phase;
        private readonly string? _documentKey;
        private readonly int _chars;
        private readonly int _lines;
        private readonly string? _meta;
        private readonly long _started;

        public Scope(SqlTypingPerfProbe probe, string op, string phase, string? documentKey, int chars, int lines, string? meta)
        {
            _probe = probe;
            _op = op;
            _phase = phase;
            _documentKey = documentKey;
            _chars = chars;
            _lines = lines;
            _meta = meta;
            _started = Environment.TickCount64;
        }

        public void Dispose()
        {
            long elapsed = Environment.TickCount64 - _started;
            _probe.Emit(_op, _phase, elapsed, _documentKey, _chars, _lines, _meta);
        }
    }
}
