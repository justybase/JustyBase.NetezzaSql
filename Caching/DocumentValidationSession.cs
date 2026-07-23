using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Linter;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.NetezzaSqlParser.Caching;

/// <summary>
/// Per-document validation session with statement-level diagnostics caching.
/// Tracks which statements have been validated and caches their diagnostics
/// so unchanged statements don't need re-validation.
/// Port of documentValidationSession.ts from the reference TypeScript project.
/// </summary>
public sealed class DocumentValidationSession : IDisposable
{
    private const int MaxDocuments = 16;
    private const int MaxDiagnosticEntriesPerDocument = 512;

    private readonly Dictionary<string, DocumentDiagnosticsCacheEntry> _documents = new();
    private readonly object _lock = new();
    private bool _disposed;

    public DocumentValidationSession()
    {
    }

    /// <summary>
    /// Prepare a document for validation: build statement index and diff against previous state.
    /// Returns the diff so the caller knows which statements need re-validation.
    /// </summary>
    public DocumentValidationState PrepareDocument(string documentUri, string sql)
    {
        StatementIndex? previousIndex;
        lock (_lock)
        {
            previousIndex = GetOrCreateDocumentCache(documentUri).StatementIndex;
        }

        var nextIndex = StatementIndexBuilder.BuildIndex(sql);
        var diff = StatementIndexBuilder.DiffIndexes(previousIndex, nextIndex);

        return new DocumentValidationState(
            PreviousIndex: previousIndex,
            NextIndex: nextIndex,
            Diff: diff
        );
    }

    /// <summary>
    /// Commit the new statement index after validation completes.
    /// </summary>
    public void CommitDocumentIndex(string documentUri, StatementIndex index)
    {
        lock (_lock)
        {
            if (_disposed) return;
            var cache = GetOrCreateDocumentCache(documentUri);
            cache.StatementIndex = index;
            cache.CreatedAtMs = Environment.TickCount64;
            EvictDocumentsIfNeeded();
        }
    }

    /// <summary>
    /// Notify the session that metadata (schema) has changed.
    /// Invalidates all cached diagnostics for this document.
    /// </summary>
    public void SyncMetadataEpoch(string documentUri, int metadataEpoch)
    {
        lock (_lock)
        {
            if (_disposed) return;
            var cache = GetOrCreateDocumentCache(documentUri);
            if (cache.MetadataEpoch is not null && cache.MetadataEpoch != metadataEpoch)
            {
                cache.DiagnosticsByStatement.Clear();
            }
            cache.MetadataEpoch = metadataEpoch;
        }
    }

    /// <summary>
    /// Get cached diagnostics for a specific statement, if still valid.
    /// </summary>
    public IReadOnlyList<LintIssue>? GetCachedDiagnostics(
        string documentUri,
        StatementBoundary statement,
        int? metadataEpoch = null)
    {
        lock (_lock)
        {
            if (_disposed) return null;
            if (!_documents.TryGetValue(documentUri, out var cache))
                return null;

            if (metadataEpoch is not null &&
                cache.MetadataEpoch is not null &&
                cache.MetadataEpoch != metadataEpoch)
            {
                return null;
            }

            if (cache.DiagnosticsByStatement.TryGetValue(statement.Index, out var cached))
            {
                if (cached.StatementHash == statement.ContentHash)
                {
                    cached.LastAccessMs = Environment.TickCount64;
                    return cached.Diagnostics;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Store diagnostics for a specific statement.
    /// </summary>
    public void StoreStatementDiagnostics(
        string documentUri,
        StatementBoundary statement,
        IReadOnlyList<LintIssue> diagnostics,
        int? metadataEpoch = null)
    {
        lock (_lock)
        {
            if (_disposed) return;
            var cache = GetOrCreateDocumentCache(documentUri);
            if (metadataEpoch is not null)
            {
                cache.MetadataEpoch = metadataEpoch;
            }

            if (cache.DiagnosticsByStatement.Count >= MaxDiagnosticEntriesPerDocument)
            {
                // Evict oldest entry
                var oldest = cache.DiagnosticsByStatement
                    .OrderBy(kvp => kvp.Value.LastAccessMs)
                    .First();
                cache.DiagnosticsByStatement.Remove(oldest.Key);
            }

            cache.DiagnosticsByStatement[statement.Index] = new CachedStatementDiagnostics(
                statement.ContentHash,
                diagnostics,
                Environment.TickCount64
            );
        }
    }

    /// <summary>
    /// Invalidate all cached diagnostics for a document.
    /// </summary>
    public void InvalidateDocument(string documentUri)
    {
        lock (_lock)
        {
            if (_documents.TryGetValue(documentUri, out var cache))
            {
                cache.DiagnosticsByStatement.Clear();
            }
        }
    }

    /// <summary>
    /// Remove a document from the session entirely.
    /// </summary>
    public void RemoveDocument(string documentUri)
    {
        lock (_lock)
        {
            _documents.Remove(documentUri);
        }
    }

    /// <summary>
    /// Clear all documents and cached data.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _documents.Clear();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            _documents.Clear();
        }
    }

    private DocumentDiagnosticsCacheEntry GetOrCreateDocumentCache(string documentUri)
    {
        if (!_documents.TryGetValue(documentUri, out var cache))
        {
            cache = new DocumentDiagnosticsCacheEntry
            {
                DiagnosticsByStatement = new Dictionary<int, CachedStatementDiagnostics>(),
                CreatedAtMs = Environment.TickCount64
            };
            _documents[documentUri] = cache;
        }
        return cache;
    }

    private void EvictDocumentsIfNeeded()
    {
        if (_documents.Count <= MaxDocuments)
            return;

        var oldest = _documents
            .OrderBy(kvp => kvp.Value.CreatedAtMs)
            .Take(_documents.Count - MaxDocuments)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in oldest)
        {
            _documents.Remove(key);
        }
    }
}

/// <summary>
/// State returned when preparing a document for validation.
/// </summary>
public readonly record struct DocumentValidationState(
    StatementIndex? PreviousIndex,
    StatementIndex NextIndex,
    StatementIndexDiff Diff
);

internal sealed class DocumentDiagnosticsCacheEntry
{
    public StatementIndex? StatementIndex { get; set; }
    public Dictionary<int, CachedStatementDiagnostics> DiagnosticsByStatement { get; set; } = new();
    public int? MetadataEpoch { get; set; }
    public long CreatedAtMs { get; set; }
}

internal sealed class CachedStatementDiagnostics
{
    public string StatementHash { get; }
    public IReadOnlyList<LintIssue> Diagnostics { get; }
    public long LastAccessMs { get; set; }

    public CachedStatementDiagnostics(string statementHash, IReadOnlyList<LintIssue> diagnostics, long lastAccessMs)
    {
        StatementHash = statementHash;
        Diagnostics = diagnostics;
        LastAccessMs = lastAccessMs;
    }
}
