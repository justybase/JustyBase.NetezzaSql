using JustyBase.NetezzaDdl;
using System.Data.Common;
using System.Diagnostics;
using System.Text;

namespace JustyBase.ImportExport.Import;

/// <summary>
/// Netezza EXTERNAL TABLE / named-pipe import engine. Creates the random-distribution table,
/// serves the job reader over a named pipe and runs the EXTERNAL USING insert with the ambient
/// (or explicit) <see cref="ImportUsingOptions"/>.
/// </summary>
public sealed class NetezzaExternalTableImportEngine : IImportEngine
{
    private const char DefaultColumnSeparator = '\t';
    private const char DefaultEscapeChar = '\\';

    /// <summary>How long to wait for the driver to flush the load log after the INSERT returns.</summary>
    private static readonly TimeSpan LoadLogTimeout = TimeSpan.FromSeconds(5);

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
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetTableName);

        var usingOptions = options.UsingOptions ?? ImportUsingOptionsContext.Current ?? ImportUsingOptions.Default;
        char columnSeparator = string.IsNullOrEmpty(usingOptions.Delimiter)
            ? DefaultColumnSeparator
            : usingOptions.Delimiter[0];

        Encoding pipeEncoding;
        try
        {
            pipeEncoding = Encoding.GetEncoding(usingOptions.EncodingName);
        }
        catch
        {
            pipeEncoding = Encoding.UTF8;
        }

        string serverName = NetezzaPipeImportExecutor.CreatePipeName("JDE");
        string[] headersWithDataType = job.ReturnHeadersWithDataTypes(DatabaseKind.Netezza);
        bool isLineReader = job is IXmlImportJob;

        // The pipe streams a DATE column date-only and a TIMESTAMP column always with the full
        // time part — both formats must match the destination column declared in headersWithDataType.
        bool[]? dateOnlyColumns = job.Columns is { Count: > 0 } columns
            ? BuildDateOnlyColumns(columns)
            : null;

        // Cancelled when the database step fails before the driver ever connected to the
        // pipe — otherwise the pipe waiter would block forever (no client, no data).
        using var pipeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var pipeServer = NetezzaPipeImportExecutor.ServeDataReaderAsync(
            job.AsReader,
            serverName,
            progress,
            preparedStringsMode: isLineReader,
            delimiter: columnSeparator,
            encoding: pipeEncoding,
            rowsCount: job.RowsCount,
            dateOnlyColumns: dateOnlyColumns,
            cancellationToken: pipeCts.Token);

        await Task.Delay(50).ConfigureAwait(false);
        progress?.Invoke("transfer to database started");
        try
        {
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var cmd = connection.CreateCommand();
                bool intoExisting = options.TargetColumnNames is { Count: > 0 };
                if (!intoExisting)
                {
                    cmd.CommandText = NetezzaImportSql.CreateRandomDistributionTable(targetTableName, headersWithDataType);
                    cmd.ExecuteNonQuery();
                    progress?.Invoke($" {targetTableName} created");
                }

                string sep2 = columnSeparator == '\t' ? "\\t" : columnSeparator.ToString();
                string encodingName = string.IsNullOrWhiteSpace(usingOptions.EncodingName) ? "utf-8" : usingOptions.EncodingName;
                cmd.CommandText = NetezzaImportEngine.BuildInsertSql(
                    targetTableName,
                    serverName,
                    headersWithDataType,
                    new NetezzaImportUsingOptions
                    {
                        RemoteSource = options.RemoteSource,
                        Delimiter = sep2,
                        SkipRows = 1,
                        NullValue = "",
                        EncodingName = encodingName,
                        EscapeChar = DefaultEscapeChar.ToString(),
                        TimeStyle = "24HOUR",
                        // The typed pipe writes booleans as 1/0 (TypeCode.Boolean path).
                        BoolStyle = "1_0",
                        MaxErrors = 0,
                        LogDirectory = options.TempLogDirectory,
                        MaxRows = usingOptions.MaxRows is > 0 ? usingOptions.MaxRows : null
                    },
                    insertTargetColumns: intoExisting ? options.TargetColumnNames : null);
                cmd.ExecuteNonQuery();

                if (!string.IsNullOrWhiteSpace(options.TempLogDirectory))
                {
                    // The driver flushes the load log (.nzlog/.nzbad) to LOGDIR when the load ends.
                    // A load can "complete" with every row rejected (e.g. a format mismatch), so the
                    // log must be consulted instead of trusting the INSERT alone. Surface a clear
                    // message whenever bad records exist or nothing was loaded.
                    LoadDiagnostics? diagnostics = ReadLoadDiagnostics(options.TempLogDirectory, targetTableName);
                    if (diagnostics is not null && (diagnostics.BadRecords > 0 || diagnostics.LoadedRecords == 0))
                    {
                        progress?.Invoke(diagnostics.BadRecords > 0
                            ? $"[ERROR] Netezza rejected {diagnostics.BadRecords:N0} row(s) for '{targetTableName}' (loaded {diagnostics.LoadedRecords:N0}); see {diagnostics.LogFilePath}"
                            : $"[ERROR] Netezza loaded 0 rows for '{targetTableName}'; see {diagnostics.LogFilePath}");
                    }
                    else
                    {
                        var badFilePath = Directory.EnumerateFiles(options.TempLogDirectory, $"{targetTableName}*.nzbad").FirstOrDefault();
                        if (badFilePath is not null)
                            progress?.Invoke($"[ERROR] {badFilePath} created");
                    }
                }
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            progress?.Invoke($"[ERROR] {ex.Message}");
            // Abort the pipe so ServeDataReaderAsync does not stay blocked on
            // WaitForConnection if the failure happened before the driver connected.
            pipeCts.Cancel();
            try
            {
                await pipeServer.ConfigureAwait(false);
            }
            catch
            {
                // pipe aborted on purpose — the database failure is the real error.
            }

            throw;
        }

        await pipeServer.ConfigureAwait(false);
    }

    private static bool[] BuildDateOnlyColumns(IReadOnlyList<IImportColumn> columns)
    {
        var result = new bool[columns.Count];
        for (int i = 0; i < columns.Count; i++)
        {
            result[i] = columns[i].Kind == ImportColumnKind.Date;
        }

        return result;
    }

    /// <summary>Summary of a Netezza external-load run, parsed from the driver's .nzlog file.</summary>
    private sealed record LoadDiagnostics(string LogFilePath, long BadRecords, long LoadedRecords);

    private static LoadDiagnostics? ReadLoadDiagnostics(string logDirectory, string tableName)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < LoadLogTimeout.TotalMilliseconds)
        {
            string? logFilePath = Directory.EnumerateFiles(logDirectory, $"{tableName}*.nzlog").FirstOrDefault();
            if (logFilePath is not null)
            {
                try
                {
                    return ParseLoadDiagnostics(logFilePath);
                }
                catch (IOException)
                {
                    // The driver may still be flushing the log — retry briefly.
                }
            }

            Thread.Sleep(100);
        }

        return null;
    }

    private static LoadDiagnostics? ParseLoadDiagnostics(string logFilePath)
    {
        long badRecords = -1;
        long loadedRecords = -1;
        foreach (string line in File.ReadLines(logFilePath))
        {
            string trimmed = line.Trim();
            if (badRecords < 0 && TryReadStat(trimmed, "number of bad records:", out long badValue))
            {
                badRecords = badValue;
            }
            else if (loadedRecords < 0 && TryReadStat(trimmed, "number of records loaded:", out long loadedValue))
            {
                loadedRecords = loadedValue;
            }

            if (badRecords >= 0 && loadedRecords >= 0)
            {
                break;
            }
        }

        if (badRecords < 0 && loadedRecords < 0)
        {
            return null;
        }

        return new LoadDiagnostics(logFilePath, Math.Max(0, badRecords), Math.Max(0, loadedRecords));
    }

    private static bool TryReadStat(string line, string prefix, out long value)
    {
        value = 0;
        if (!line.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        return long.TryParse(line.AsSpan(prefix.Length).Trim(), out value);
    }
}