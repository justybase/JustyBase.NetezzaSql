namespace JustyBase.ImportExport.Import;

/// <summary>Stage/rows message streamed by the import engines (mirror of the Legacy contracts).</summary>
public sealed record ImportProgress(
    string Stage,
    long RowsRead = 0,
    long RowsImported = 0,
    long RowsSkipped = 0,
    string? Message = null,
    bool IsCompleted = false,
    ImportOutcome? Outcome = null,
    string? ErrorMessage = null);

/// <summary>Uniform result of an import engine run (mirror of the vscode <c>ImportResult</c>).</summary>
public sealed record ImportOutcome(
    bool Success,
    string Message,
    long RowsProcessed = 0,
    long RowsInserted = 0,
    string? TargetTable = null,
    IReadOnlyList<string>? Warnings = null);
