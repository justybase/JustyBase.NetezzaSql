using System.Data;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text;

namespace JustyBase.ImportExport.Import;

/// <summary>
/// Shared file-import orchestrator. Owns source opening, per-sheet type scanning (via the
/// shared <see cref="ImportTypeAnalyzer"/>), validation against the selected type plan and
/// streaming job creation. Hosts plug in their readers through <see cref="IImportSourceFactory"/>
/// and keep only UI state and the per-sheet override plan.
/// </summary>
public sealed class TabularImportScanner
{
    private const int TimeoutInSec = 4 * 60 * 60;
    private const int ProgressEveryRows = 50_000;
    private const int PreviewRowCount = 5;

    private readonly IImportSourceFactory _factory;
    private readonly SemaphoreSlim _detectionGate = new(1, 1);
    private IImportSource? _liveSource;

    public TabularImportScanner(IImportSourceFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public string? FilePath { get; set; }

    public Encoding? SourceEncoding { get; set; }

    public bool TreatAllColumnsAsText { get; set; }

    public Action<string>? StandardMessageAction { get; set; }

    public IReadOnlyList<string> SheetNames { get; private set; } = [];

    /// <summary>Opens the configured file and collects its sheet names.</summary>
    public bool OpenSource()
    {
        string filePath = FilePath ?? throw new InvalidOperationException("An import file path must be configured before initialization.");
        DisposeSource();
        try
        {
            _liveSource = _factory.OpenSource(filePath, SourceEncoding);
            SheetNames = _liveSource.GetSheetNames().ToList();
            return true;
        }
        catch
        {
            _liveSource?.Dispose();
            _liveSource = null;
            SheetNames = [];
            return false;
        }
    }

    public void DisposeSource()
    {
        _liveSource?.Dispose();
        _liveSource = null;
        SheetNames = [];
    }

    /// <summary>
    /// Scans one sheet and returns the detection result. Scans are serialized per instance
    /// (the live source is not thread-safe).
    /// </summary>
    public async Task<SheetScanResult?> ScanSheetAsync(
        string sheetName,
        Action<string>? messageAction = null,
        CancellationToken cancellationToken = default)
    {
        IImportSource? source = _liveSource;
        if (source is null || string.IsNullOrWhiteSpace(FilePath))
        {
            return null;
        }

        await _detectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            source.ActualSheetName = sheetName;
            source.TreatAllColumnsAsText = TreatAllColumnsAsText;
            return await Task.Run(
                () => ScanSource(source, sheetName, messageAction, TimeoutInSec),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _detectionGate.Release();
        }
    }

    /// <summary>
    /// Validates every selected sheet with the exact source and selected type plan that will
    /// be used by import. For exclusive sources (e.g. xlsb) the live source is used and then
    /// reopened; otherwise a fresh source is opened so a successful validation never consumes
    /// the subsequent import.
    /// </summary>
    public async Task<IReadOnlyList<ImportValidationError>> ValidateSelectedSheetsAsync(
        IReadOnlyList<string>? sheetNames,
        Func<string, SheetPlan> planFor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(planFor);
        string[] selected = (sheetNames ?? SheetNames).Distinct(StringComparer.Ordinal).ToArray();
        if (selected.Length == 0 || _liveSource is null || string.IsNullOrWhiteSpace(FilePath))
        {
            return [];
        }

        if (_factory.IsExclusiveOpen(FilePath))
        {
            IImportSource live = _liveSource;
            await _detectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                List<ImportValidationError>? validatedErrors = null;
                ExceptionDispatchInfo? validationException = null;
                Exception? reopenException = null;
                try
                {
                    validatedErrors = await Task.Run(
                        () => ValidateWithSource(live, selected, planFor),
                        cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    validationException = ExceptionDispatchInfo.Capture(ex);
                }
                finally
                {
                    // The exclusive source keeps a single reader. It must be closed and
                    // reopened while the gate is still held, including when validation fails.
                    DisposeSource();
                    try
                    {
                        if (!OpenSource())
                        {
                            reopenException = new IOException("The import source could not be reopened after validation.");
                        }
                    }
                    catch (Exception ex)
                    {
                        reopenException = ex;
                    }
                }

                if (reopenException is not null)
                {
                    if (validationException is not null)
                    {
                        throw new AggregateException(
                            "The XLSB reader could not be restored after validation.",
                            validationException.SourceException,
                            reopenException);
                    }

                    ExceptionDispatchInfo.Capture(reopenException).Throw();
                }

                validationException?.Throw();
                return validatedErrors ?? [];
            }
            finally
            {
                _detectionGate.Release();
            }
        }

        return await Task.Run(
            () =>
            {
                using IImportSource validationSource = _factory.OpenSource(FilePath!, SourceEncoding);
                return ValidateWithSource(validationSource, selected, planFor);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds the streaming import jobs for the selected sheets, mirroring the host's single-pass
    /// reader choreography (Excel reuses the live reader, CSV reopens a fresh source per sheet).
    /// </summary>
    public async IAsyncEnumerable<IImportJob> CreateJobs(
        IReadOnlyList<string> selectedSheets,
        Func<string, SheetPlan> planFor)
    {
        ArgumentNullException.ThrowIfNull(selectedSheets);
        ArgumentNullException.ThrowIfNull(planFor);

        IImportSource? live = _liveSource;
        string? filePath = FilePath;
        if (live is null || string.IsNullOrWhiteSpace(filePath))
        {
            yield break;
        }

        // Excel jobs stream from the live reader (positioned per sheet via ActualSheetName),
        // matching the host single-pass choreography; CSV jobs reopen a fresh source per sheet.
        bool reuseLive = live.IsCsvSource == false;
        IImportSource? freshSource = null;
        try
        {
            foreach (string sheet in selectedSheets)
            {
                SheetPlan plan = planFor(sheet);

                IImportSource source;
                if (reuseLive)
                {
                    source = live;
                }
                else
                {
                    freshSource?.Dispose();
                    freshSource = _factory.OpenSource(filePath, SourceEncoding);
                    source = freshSource;
                }

                source.ActualSheetName = sheet;
                source.TreatAllColumnsAsText = TreatAllColumnsAsText;
                if (!source.IsCsvSource)
                {
                    source.Read(); // skip headers
                }

                IDataReader reader = source.CreateTypedReader(Kinds(plan), Headers(plan));
                yield return new ImportJob(reader, plan.Columns, plan.RowsCount, plan.PreviewRows, sheet);
            }
        }
        finally
        {
            freshSource?.Dispose();
            if (live.IsCsvSource)
            {
                // CSV jobs stream from fresh per-sheet sources; the initial live reader is
                // exhausted by the scan and is replaced by the last fresh source.
                DisposeSource();
            }
        }
    }

    /// <summary>Builds a destination table name for the sheet at <paramref name="index"/> (0 = bare name).</summary>
    public static string BuildTableName(DatabaseKind databaseKind, string? schemaName, string tableMask, int index)
    {
        string tmp = index == 0 ? string.Empty : $"_{index}";
        return databaseKind == DatabaseKind.Oracle || string.IsNullOrEmpty(schemaName)
            ? $"{tableMask}{tmp}"
            : $"{schemaName}.{tableMask}{tmp}";
    }

    /// <summary>
    /// Synchronous scan entry used by hosts that already hold an open reader (e.g. benchmarks).
    /// Applies the exact detection loops (CSV raw tokens + progress, Excel canonical cells).
    /// </summary>
    public static SheetScanResult ScanSource(
        IImportSource source,
        string sheetName,
        Action<string>? messageAction = null,
        long timeoutInSec = TimeoutInSec)
    {
        ArgumentNullException.ThrowIfNull(source);

        source.ActualSheetName = sheetName;
        if (!source.IsCsvSource)
        {
            source.Read(); // skip headers
        }

        int columnCount = source.FieldCount;
        var originalHeaders = new string[columnCount];
        var normalizedHeaders = new string[columnCount];
        var rawValueLengths = new int[columnCount];
        for (int i = 0; i < columnCount; i++)
        {
            originalHeaders[i] = source.GetName(i) ?? string.Empty;
            normalizedHeaders[i] = ImportNameHelper.NormalizeDbColumnName(originalHeaders[i]);
        }

        var analyzer = new ImportTypeAnalyzer(columnCount, inferBoolean: true);
        bool treatAllColumnsAsText = source.TreatAllColumnsAsText;
        var previewRows = new List<string[]>();
        long rowsCount = -1;
        var timestampBeforeLongLoop = Stopwatch.GetTimestamp();

        if (source.IsCsvSource)
        {
            Stopwatch messageStopwatch = Stopwatch.StartNew();
            while (source.Read())
            {
                rowsCount++;
                for (int columnIndex = 0; columnIndex < columnCount; columnIndex++)
                {
                    rawValueLengths[columnIndex] = Math.Max(rawValueLengths[columnIndex], source.GetRawLength(columnIndex));

                    string? cell = source.GetCellText(columnIndex);
                    if (cell is not null)
                    {
                        analyzer.AddValue(columnIndex, cell, treatAllColumnsAsText: treatAllColumnsAsText);
                    }

                    if (rowsCount < PreviewRowCount)
                    {
                        if (columnIndex == 0)
                        {
                            previewRows.Add(new string[columnCount]);
                        }

                        previewRows[(int)rowsCount][columnIndex] = cell ?? string.Empty;
                    }
                }

                if (rowsCount > 0 && rowsCount % ProgressEveryRows == 0 && messageStopwatch.ElapsedMilliseconds > 1_000)
                {
                    messageAction?.Invoke($"{source.ReadProgress:P1} / ({rowsCount:N0} rows) analysed");
                    if (timeoutInSec != -1 && messageStopwatch.Elapsed.Seconds > timeoutInSec && messageStopwatch.Elapsed.Seconds >= 10)
                    {
                        messageAction?.Invoke($"analysed stopped ! (timout of {timeoutInSec:N0} sec)");
                        rowsCount = -1;
                        break;
                    }

                    messageStopwatch.Restart();
                }
            }
        }
        else
        {
            while (source.Read())
            {
                for (int columnIndex = 0; columnIndex < columnCount; columnIndex++)
                {
                    rawValueLengths[columnIndex] = Math.Max(rawValueLengths[columnIndex], source.GetRawLength(columnIndex));

                    string? cell = source.GetCellText(columnIndex);
                    if (cell is not null)
                    {
                        analyzer.AddValue(columnIndex, cell, treatAllColumnsAsText: treatAllColumnsAsText);
                    }
                }
            }
        }

        long elapsedMs = Stopwatch.GetElapsedTime(timestampBeforeLongLoop).Milliseconds;
        messageAction?.Invoke($"type analysis took {elapsedMs} ms");

        IReadOnlyList<DetectedImportColumnType> detected = analyzer.Choose(originalHeaders);
        return new SheetScanResult(
            sheetName,
            originalHeaders,
            normalizedHeaders,
            rawValueLengths,
            previewRows,
            detected,
            rowsCount,
            TimeSpan.FromMilliseconds(elapsedMs));
    }

    private List<ImportValidationError> ValidateWithSource(
        IImportSource source,
        IReadOnlyList<string> selected,
        Func<string, SheetPlan> planFor)
    {
        var result = new List<ImportValidationError>();
        foreach (string sheet in selected)
        {
            SheetPlan plan = planFor(sheet);

            source.ActualSheetName = sheet;
            source.TreatAllColumnsAsText = TreatAllColumnsAsText;
            if (!source.IsCsvSource)
            {
                source.Read();
            }

            using IDataReader reader = source.CreateTypedReader(Kinds(plan), Headers(plan));
            int rowNumber = 1; // row one is the header
            while (reader.Read())
            {
                rowNumber++;
                for (int column = 0; column < reader.FieldCount; column++)
                {
                    if (reader.IsDBNull(column))
                    {
                        continue;
                    }

                    try
                    {
                        _ = reader.GetValue(column);
                    }
                    catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException or ArgumentException)
                    {
                        result.Add(new ImportValidationError(
                            sheet,
                            rowNumber,
                            column,
                            reader.GetName(column),
                            plan.Columns[column].Kind,
                            GetSourceValue(reader, column),
                            ex.Message));
                    }
                }
            }
        }

        return result;
    }

    private static string GetSourceValue(System.Data.IDataReader reader, int column)
    {
        try
        {
            return reader.GetString(column);
        }
        catch
        {
            return Convert.ToString(reader[column], System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        }
    }

    private static ImportColumnKind[] Kinds(SheetPlan plan)
        => plan.Columns.Select(static c => c.Kind).ToArray();

    private static string[] Headers(SheetPlan plan)
        => plan.Columns.Select(static c => c.Name).ToArray();
}
