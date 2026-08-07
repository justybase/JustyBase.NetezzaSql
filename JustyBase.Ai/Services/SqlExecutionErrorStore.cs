namespace JustyBase.Ai.Services;

public sealed record SqlExecutionError(
    DateTime Timestamp,
    string Message,
    string? DocumentTitle,
    string? ConnectionName,
    string? DatabaseName);

/// <summary>
/// Keeps the latest error from the SQL execution pipeline separate from the general application log.
/// This prevents import, file-search and schema-search failures from being presented as SQL errors to AI.
/// </summary>
public sealed class SqlExecutionErrorStore
{
    private readonly object _gate = new();
    private SqlExecutionError? _lastError;

    public SqlExecutionError? LastError
    {
        get
        {
            lock (_gate)
                return _lastError;
        }
    }

    public void Record(
        Exception exception,
        string? documentTitle = null,
        string? connectionName = null,
        string? databaseName = null)
    {
        Record(exception.Message, documentTitle, connectionName, databaseName);
    }

    public void Record(
        string message,
        string? documentTitle = null,
        string? connectionName = null,
        string? databaseName = null)
    {
        lock (_gate)
        {
            _lastError = new SqlExecutionError(
                DateTime.Now,
                string.IsNullOrWhiteSpace(message) ? "Unknown SQL execution error." : message,
                documentTitle,
                connectionName,
                databaseName);
        }
    }

    public void Clear()
    {
        lock (_gate)
            _lastError = null;
    }
}
