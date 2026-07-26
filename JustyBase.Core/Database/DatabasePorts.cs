namespace JustyBase.Core.Database;

public sealed record DatabaseConnectionProfile(
    string Name,
    string Driver,
    string? Database = null,
    string? Host = null);

public interface IDatabaseConnection : IAsyncDisposable
{
    ValueTask OpenAsync(CancellationToken cancellationToken = default);
    ValueTask CloseAsync(CancellationToken cancellationToken = default);
}

public interface IDatabaseConnectionFactory
{
    ValueTask<IDatabaseConnection> OpenAsync(
        DatabaseConnectionProfile profile,
        CancellationToken cancellationToken = default);
}

public sealed record SqlQueryResult(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<object?>> Rows,
    int RecordsAffected = -1);

public interface ISqlQueryExecutor
{
    ValueTask<SqlQueryResult> ExecuteAsync(string sql, CancellationToken cancellationToken = default);
}
