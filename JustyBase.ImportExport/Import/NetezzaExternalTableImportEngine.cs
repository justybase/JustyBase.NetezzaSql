using JustyBase.NetezzaDdl;
using System.Data.Common;
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

        var pipeServer = NetezzaPipeImportExecutor.ServeDataReaderAsync(
            job.AsReader,
            serverName,
            progress,
            preparedStringsMode: isLineReader,
            delimiter: columnSeparator,
            encoding: pipeEncoding,
            rowsCount: job.RowsCount);

        await Task.Delay(50).ConfigureAwait(false);
        progress?.Invoke("transfer to database started");
        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
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

                var badFilePath = Directory.EnumerateFiles(options.TempLogDirectory ?? string.Empty, $"{targetTableName}*.nzbad").FirstOrDefault();
                if (badFilePath is not null)
                    progress?.Invoke($"[ERROR] {badFilePath} created");
            }
            catch (Exception ex)
            {
                progress?.Invoke($"[ERROR] {ex.Message}");
            }
        }).ConfigureAwait(false);

        await pipeServer.ConfigureAwait(false);
    }
}