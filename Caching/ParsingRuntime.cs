using JustyBase.NetezzaSqlParser.Ast;

namespace JustyBase.NetezzaSqlParser.Caching;

/// <summary>
/// Parser runtime with parse-result caching.
/// Thin wrapper around DocumentParseSession that caches tokenized + parsed AST
/// keyed by content hash, so unchanged SQL avoids re-tokenization and re-parsing.
/// Port of parsingRuntime.ts from the reference TypeScript project.
/// </summary>
public sealed class ParsingRuntime : IDisposable
{
    private readonly DocumentParseSession _parseSession;
    private int _activeSessions;
    private bool _disposed;

    public ParsingRuntime()
    {
        _parseSession = new DocumentParseSession();
    }

    public DocumentParseSession ParseSession => _parseSession;

    /// <summary>
    /// Parse SQL text using cached results when possible.
    /// Cache hit avoids re-tokenization and re-parsing entirely.
    /// </summary>
    public ParseResult Parse(string sql)
    {
        Interlocked.Increment(ref _activeSessions);
        try
        {
            return _parseSession.GetOrParse(sql);
        }
        finally
        {
            Interlocked.Decrement(ref _activeSessions);
        }
    }

    /// <summary>
    /// Invalidate a specific SQL content from cache.
    /// </summary>
    public void Invalidate(string sql)
    {
        _parseSession.Invalidate(sql);
    }

    /// <summary>
    /// Clear all caches.
    /// </summary>
    public void Clear()
    {
        _parseSession.Clear();
    }

    /// <summary>
    /// Get cache hit/miss statistics for performance monitoring.
    /// </summary>
    public (int hits, int misses) GetStats()
    {
        return _parseSession.GetStats();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _parseSession.Dispose();
        }
    }
}
