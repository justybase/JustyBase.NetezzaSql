using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Lexer;
using System.Text.RegularExpressions;
using JustyBase.NetezzaSqlParser.Parser;
using Superpower.Model;
using Superpower;

namespace JustyBase.NetezzaSqlParser.Caching;

/// <summary>
/// Document-level parse session with LRU caching.
/// Caches tokenized and parsed results keyed by document content hash.
/// Port of documentParseSession.ts from the reference TypeScript project.
/// </summary>
public sealed class DocumentParseSession : IDisposable
{
    private const int MaxParseEntries = 32;

    // Cache key: contentHash
    // Cache value: (tokens, statements, errors, createdAtMs)
    private readonly Dictionary<string, CachedParseEntry> _parseCache = new();
    private readonly LinkedList<string> _parseLruOrder = new();
    private readonly object _lock = new();
    private int _parseCacheHits;
    private int _parseCacheMisses;
    private bool _disposed;

    public DocumentParseSession()
    {
    }

    /// <summary>
    /// Parse SQL text, using cache if available.
    /// </summary>
    public ParseResult GetOrParse(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);

        var contentHash = StatementIndexBuilder.SimpleHash(sql);

        lock (_lock)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(DocumentParseSession));

            if (_parseCache.TryGetValue(contentHash, out var cached))
            {
                _parseCacheHits++;
                TouchEntry(contentHash);
                return cached.Result;
            }
        }

        // Cache miss — parse outside lock
        var result = ParseSql(sql);

        lock (_lock)
        {
            if (_disposed) return result;

            _parseCacheMisses++;

            // Evict if at capacity
            if (_parseCache.Count >= MaxParseEntries)
            {
                var oldest = _parseLruOrder.Last!.Value;
                _parseCache.Remove(oldest);
                _parseLruOrder.RemoveLast();
            }

            _parseCache[contentHash] = new CachedParseEntry(result, Environment.TickCount64);
            _parseLruOrder.AddFirst(contentHash);
        }

        return result;
    }

    /// <summary>
    /// Invalidate a specific SQL content from cache.
    /// </summary>
    public void Invalidate(string sql)
    {
        var contentHash = StatementIndexBuilder.SimpleHash(sql);
        lock (_lock)
        {
            if (_parseCache.Remove(contentHash))
            {
                _parseLruOrder.Remove(contentHash);
            }
        }
    }

    /// <summary>
    /// Clear all cached entries.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _parseCache.Clear();
            _parseLruOrder.Clear();
            _parseCacheHits = 0;
            _parseCacheMisses = 0;
        }
    }

    /// <summary>
    /// Get cache hit/miss statistics.
    /// </summary>
    public (int hits, int misses) GetStats()
    {
        lock (_lock)
        {
            return (_parseCacheHits, _parseCacheMisses);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            _parseCache.Clear();
            _parseLruOrder.Clear();
        }
    }

    private void TouchEntry(string key)
    {
        var node = _parseLruOrder.Find(key);
        if (node is not null && node != _parseLruOrder.First)
        {
            _parseLruOrder.Remove(node);
            _parseLruOrder.AddFirst(node);
        }
    }

    /// <summary>
    /// Full parse: tokenize + parse all statements.
    /// </summary>
    internal static ParseResult ParseSql(string sql)
    {
        if (string.IsNullOrEmpty(sql))
            return new ParseResult([], [], true);

        try
        {
            // NZPLSQL loop labels are optional structural markers; remove them
            // before tokenization so the core grammar can parse the labelled
            // LOOP/FOR statement identically to its unlabelled form.
            sql = Regex.Replace(sql, @"<<\s*[A-Za-z_][A-Za-z0-9_]*\s*>>", "", RegexOptions.CultureInvariant);
            var tokens = NzLexer.Tokenize(sql).ToArray();
            var parser = new NzSqlParser(tokens);
            var statements = new List<Statement>();
            var errors = new List<ValidationError>();

            while (true)
            {
                var errorsBefore = parser.Errors.Count;
                var stmt = parser.Parse();

                for (int i = errorsBefore; i < parser.Errors.Count; i++)
                {
                    errors.Add(parser.Errors[i]);
                }

                if (stmt is null) break;
                statements.Add(stmt);
            }

            return new ParseResult(statements, errors, errors.Count == 0);
        }
        catch (Superpower.ParseException ex)
        {
            return new ParseResult([], [new ValidationError(
                $"Lexer error: {ex.Message}", "error",
                new SourcePosition(1, 1, 0), "LEX001")], false);
        }
        catch (Exception ex)
        {
            return new ParseResult([], [new ValidationError(
                $"Unexpected parser error: {ex.Message}", "error",
                new SourcePosition(0, 0, 0), "PARSE000")], false);
        }
    }
}

/// <summary>
/// Result of parsing a SQL document.
/// </summary>
public readonly record struct ParseResult(
    IReadOnlyList<Statement> Statements,
    IReadOnlyList<ValidationError> Errors,
    bool Valid
);

internal sealed record CachedParseEntry(ParseResult Result, long CreatedAtMs);
