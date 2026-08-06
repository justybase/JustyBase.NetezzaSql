namespace JustyBase.ImportExport.Import;

/// <summary>Result of the shared type scan for one sheet (detection output, no host type-plan state).</summary>
public sealed record SheetScanResult(
    string SheetName,
    string[] OriginalHeaders,
    string[] NormalizedHeaders,
    int[] RawValueLengths,
    IReadOnlyList<string[]> PreviewRows,
    IReadOnlyList<DetectedImportColumnType> DetectedTypes,
    long RowsCount,
    TimeSpan ScanDuration);
