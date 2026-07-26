namespace JustyBase.Core.Execution;

public sealed record SqlExecutionRequest(
    IReadOnlyList<string> Statements,
    string? ConnectionName = null,
    string? DatabaseName = null,
    bool ContinueOnError = false);

public abstract record SqlExecutionEvent(int StatementIndex, string Sql)
{
    public sealed record Started(int Index, string Text) : SqlExecutionEvent(Index, Text);
    public sealed record Batch(int Index, string Text, IReadOnlyList<IReadOnlyList<object?>> Rows, int RecordsAffected = -1)
        : SqlExecutionEvent(Index, Text);
    public sealed record Completed(int Index, string Text, int RecordsAffected = -1) : SqlExecutionEvent(Index, Text);
    public sealed record Failed(int Index, string Text, Exception Error) : SqlExecutionEvent(Index, Text);
}

public interface ISqlExecutionBackend
{
    IAsyncEnumerable<SqlExecutionEvent> ExecuteAsync(
        int statementIndex,
        string sql,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Shared batching/error/cancellation lifecycle. Hosts only translate events
/// into their grid and log presenters.
/// Status: scaffold — Avalonia and Legacy still use local execution stacks.
/// See docs/shared-core-status.md.
/// </summary>
public sealed class SqlExecutionRunner(ISqlExecutionBackend backend)
{
    public async IAsyncEnumerable<SqlExecutionEvent> RunAsync(
        SqlExecutionRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(backend);

        for (int index = 0; index < request.Statements.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string sql = request.Statements[index];
            if (string.IsNullOrWhiteSpace(sql))
                continue;

            yield return new SqlExecutionEvent.Started(index, sql);
            List<SqlExecutionEvent>? backendEvents = null;
            Exception? error = null;
            try
            {
                backendEvents = [];
                await foreach (var item in backend.ExecuteAsync(index, sql, cancellationToken).WithCancellation(cancellationToken))
                    backendEvents.Add(item);
            }
            catch (Exception caught) when (caught is not OperationCanceledException)
            {
                error = caught;
            }

            if (error is not null)
            {
                yield return new SqlExecutionEvent.Failed(index, sql, error);
                if (!request.ContinueOnError)
                    yield break;
                continue;
            }

            foreach (var item in backendEvents!)
                yield return item;
            yield return new SqlExecutionEvent.Completed(index, sql);
        }
    }
}
