namespace JustyBase.ImportExport.Import;

/// <summary>
/// Selected per-sheet import plan consumed by validation and job building. Hosts build it
/// from their UI type-plan (the per-sheet override state) right before each use.
/// </summary>
public sealed record SheetPlan(
    string SheetName,
    IReadOnlyList<IImportColumn> Columns,
    IReadOnlyList<string[]>? PreviewRows,
    long RowsCount);
