using JustyBase.NetezzaSqlParser.Caching;

namespace JustyBase.NetezzaSqlParser.Caching;

/// <summary>
/// Shares parse sessions across completion, lint, hover, and signature help per document.
/// </summary>
public sealed class DocumentParsingCoordinator : IDisposable
{
    private const int MaxDocuments = 16;
    private readonly Dictionary<string, ParsingRuntime> _runtimes = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _lru = new();
    private readonly object _lock = new();
    private bool _disposed;

    public ParsingRuntime GetOrCreate(string documentUri)
    {
        var key = string.IsNullOrWhiteSpace(documentUri) ? "default" : documentUri;
        lock (_lock)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(DocumentParsingCoordinator));
            if (_runtimes.TryGetValue(key, out var runtime))
            {
                Touch(key);
                return runtime;
            }

            runtime = new ParsingRuntime();
            _runtimes[key] = runtime;
            _lru.AddFirst(key);
            EvictIfNeeded();
            return runtime;
        }
    }

    public void Release(string documentUri)
    {
        var key = string.IsNullOrWhiteSpace(documentUri) ? "default" : documentUri;
        lock (_lock)
        {
            if (_runtimes.Remove(key, out var runtime))
            {
                runtime.Dispose();
                _lru.Remove(key);
            }
        }
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
