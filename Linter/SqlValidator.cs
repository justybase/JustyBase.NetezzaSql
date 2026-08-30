using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.NetezzaSqlParser.Linter;

/// <summary>
/// Public engine-facing validation API. This is the C# counterpart of the
/// reference validator facade: callers provide SQL, a schema provider and an
/// optional document identity, while parsing, semantic validation and
/// statement-level caching remain behind one stable entry point.
/// </summary>
public sealed class SqlValidator : IDisposable
{
    private readonly LintEngine _engine;
    private bool _disposed;

    public SqlValidator(ISchemaProvider? schemaProvider = null,
        QualityRuleRegistry? registry = null)
    {
        SchemaProvider = schemaProvider;
        _engine = registry is null ? new LintEngine() : new LintEngine(registry);
    }

    public ISchemaProvider? SchemaProvider { get; }

    /// <summary>
    /// Runs cheap lint rules and, when schema metadata is available, the full
    /// parser and semantic validation pipeline.
    /// </summary>
    public LintResult Validate(string sql, string? documentUri = null,
        int? metadataEpoch = null, CancellationToken cancellationToken = default,
        string? activeDatabase = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _engine.RunFullLint(new LintConfig(
            Sql: sql,
            Schema: SchemaProvider,
            DocumentUri: ScopedDocumentUri(documentUri, activeDatabase),
            MetadataEpoch: metadataEpoch,
            CancellationToken: cancellationToken,
            ActiveDatabase: activeDatabase));
    }

    public LintResult Validate(string sql, ISchemaProvider schemaProvider,
        string? documentUri = null, int? metadataEpoch = null,
        CancellationToken cancellationToken = default, string? activeDatabase = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _engine.RunFullLint(new LintConfig(
            Sql: sql,
            Schema: schemaProvider,
            DocumentUri: ScopedDocumentUri(documentUri, activeDatabase),
            MetadataEpoch: metadataEpoch,
            CancellationToken: cancellationToken,
            ActiveDatabase: activeDatabase));
    }

    /// <summary>
    /// Runs validation for a document while retaining the engine's incremental
    /// caches for the supplied document URI.
    /// </summary>
    public LintResult ValidateIncremental(string sql, string documentUri,
        int? metadataEpoch = null, CancellationToken cancellationToken = default,
        string? activeDatabase = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _engine.RunIncrementalLint(new LintConfig(
            Sql: sql,
            Schema: SchemaProvider,
            DocumentUri: ScopedDocumentUri(documentUri, activeDatabase),
            MetadataEpoch: metadataEpoch,
            CancellationToken: cancellationToken,
            ActiveDatabase: activeDatabase));
    }

    /// <summary>Gets the rule registry used by this validator.</summary>
    public QualityRuleRegistry Registry => _engine.Registry;

    private static string? ScopedDocumentUri(string? documentUri, string? activeDatabase) =>
        string.IsNullOrWhiteSpace(activeDatabase)
            ? documentUri
            : $"{documentUri}|database:{activeDatabase}";

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _engine.Dispose();
    }
}
