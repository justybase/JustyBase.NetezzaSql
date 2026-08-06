using System.Data;
using System.Data.Common;
using System.Text;

namespace JustyBase.ImportExport.Import;

/// <summary>
/// Generic parameterized batch-INSERT engine over any ADO.NET provider. Used as the fallback
/// for databases without a native bulk/COPY path; the job reader is streamed and rows are
/// bound as provider parameters in <see cref="BatchSize"/>-row batches.
/// </summary>
public sealed class BatchInsertEngine(int batchSize = 1000) : IImportEngine
{
    public int BatchSize { get; } = batchSize > 0 ? batchSize : 1;

    public async Task ExecuteAsync(
        DbConnection connection,
        IImportJob job,
        string targetTableName,
        ImportEngineOptions options,
        Action<string>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(job);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetTableName);
        ArgumentNullException.ThrowIfNull(options);

        IReadOnlyList<IImportColumn> columns = job.Columns;
        if (columns.Count == 0)
        {
            return;
        }

        string baseSql = BuildInsertStatement(targetTableName, columns, BatchSize);
        long inserted = 0;
        using var cmd = connection.CreateCommand();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var values = new object?[columns.Count * BatchSize];
            int count = 0;
            for (; count < BatchSize; count++)
            {
                if (!job.AsReader.Read())
                {
                    break;
                }

                for (int i = 0; i < columns.Count; i++)
                {
                    values[count * columns.Count + i] = job.AsReader.IsDBNull(i)
                        ? DBNull.Value
                        : job.AsReader.GetValue(i);
                }
            }

            if (count == 0)
            {
                break;
            }

            if (count < BatchSize)
            {
                cmd.CommandText = BuildInsertStatement(targetTableName, columns, count);
            }

            cmd.Parameters.Clear();
            for (int i = 0; i < count * columns.Count; i++)
            {
                var parameter = cmd.CreateParameter();
                parameter.ParameterName = $"p{i}";
                parameter.Value = values[i] ?? DBNull.Value;
                cmd.Parameters.Add(parameter);
            }

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            inserted += count;
            progress?.Invoke($"{inserted:N0} rows inserted");

            if (count < BatchSize)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Builds <c>INSERT INTO target (col1, col2) VALUES (@p0, @p1), (@p2, @p3)</c> for
    /// <paramref name="rowCount"/> value groups. Pure so dialects can share/adjust it.
    /// </summary>
    public static string BuildInsertStatement(string targetTableName, IReadOnlyList<IImportColumn> columns, int rowCount = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetTableName);
        ArgumentNullException.ThrowIfNull(columns);
        if (columns.Count == 0)
        {
            throw new ArgumentException("At least one column is required.", nameof(columns));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rowCount);

        var sb = new StringBuilder("INSERT INTO ").Append(targetTableName).Append(" (");
        sb.Append(string.Join(", ", columns.Select(static c => c.Name)));
        sb.Append(") VALUES ");

        int parameterIndex = 0;
        for (int row = 0; row < rowCount; row++)
        {
            if (row > 0)
            {
                sb.Append(", ");
            }

            sb.Append('(');
            for (int col = 0; col < columns.Count; col++)
            {
                if (col > 0)
                {
                    sb.Append(", ");
                }

                sb.Append('@').Append('p').Append(parameterIndex++);
            }

            sb.Append(')');
        }

        return sb.ToString();
    }
}