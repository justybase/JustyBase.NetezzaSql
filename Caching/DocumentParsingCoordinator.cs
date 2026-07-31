using JustyBase.NetezzaSqlParser.Dialects;

namespace JustyBase.NetezzaSqlParser.Caching;

/// <summary>
/// Shares parse sessions across completion, lint, hover, and signature help per document.
/// Cache keys include both URI and dialect so switching dialects never reuses a stale runtime.
/// </summary>
public sealed class DocumentParsingCoordinator : IDisposable
{
    private const int MaxDocuments = 16;
    private readonly Dictionary<string, ParsingRuntime> _runtimes = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _lru = new();
    private readonly object _lock = new();
    private bool _disposed;

    public ParsingRuntime GetOrCreate(string documentUri, SqlDialect dialect = SqlDialect.Netezza)
    {
        var key = MakeKey(documentUri, dialect);
        lock (_lock)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(DocumentParsingCoordinator));
            if (_runtimes.TryGetValue(key, out var runtime))
            {
                Touch(key);
                return runtime;
            }

            runtime = new ParsingRuntime(dialect);
            _runtimes[key] = runtime;
            _lru.AddFirst(key);
            EvictIfNeeded();
            return runtime;
        }
    }

    /// <summary>
    /// Dispose and drop every cached runtime for <paramref name="documentUri"/>,
    /// regardless of dialect.
    /// </summary>
    public void Release(string documentUri)
    {
        var prefix = UriPrefix(documentUri);
        lock (_lock)
        {
            var toRemove = _runtimes.Keys
                .Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
                .ToList();
            foreach (var key in toRemove)
            {
                if (_runtimes.Remove(key, out var runtime))
                {
                    runtime.Dispose();
                    _lru.Remove(key);
                }
            }
        }
    }

    /// <summary>
    /// Dispose and drop all cached parse runtimes. Used when the active dialect
    /// changes so stale sessions are not reused for the new dialect.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            if (_disposed) return;
            foreach (var runtime in _runtimes.Values)
                runtime.Dispose();
            _runtimes.Clear();
            _lru.Clear();
        }
    }

    internal static string MakeKey(string documentUri, SqlDialect dialect)
    {
        var uri = string.IsNullOrWhiteSpace(documentUri) ? "default" : documentUri;
        return uri + "\0" + dialect;
    }

    private static string UriPrefix(string documentUri)
    {
        var uri = string.IsNullOrWhiteSpace(documentUri) ? "default" : documentUri;
        return uri + "\0";
    }

    private void Touch(string key)
    {
        _lru.Remove(key);
        _lru.AddFirst(key);
    }

    private void EvictIfNeeded()
    {
        while (_runtimes.Count > MaxDocuments && _lru.Last is not null)
        {
            var oldest = _lru.Last.Value;
            _lru.RemoveLast();
            if (_runtimes.Remove(oldest, out var runtime))
                runtime.Dispose();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var runtime in _runtimes.Values)
                runtime.Dispose();
            _runtimes.Clear();
            _lru.Clear();
        }
    }
}
