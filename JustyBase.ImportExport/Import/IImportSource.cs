using System.Data;

namespace JustyBase.ImportExport.Import;

/// <summary>
/// Spread-sheet-neutral raw row source consumed by <see cref="TabularImportScanner"/>.
/// Implementations are host-backed when they wrap native Excel readers (SpreadSheetTasks)
/// and shared when they wrap <see cref="CsvRowReader"/>. Reading is single-pass: the
/// underlying reader is repositioned per sheet by setting <see cref="ActualSheetName"/>.
/// </summary>
public interface IImportSource : IDisposable
{
    string? FilePath { get; }

    bool IsCsvSource { get; }

    /// <summary>true when a second reader cannot be opened while this source is open (e.g. xlsb).</summary>
    bool IsExclusiveOpen { get; }

    string? ActualSheetName { get; set; }

    bool TreatAllColumnsAsText { get; set; }

    int FieldCount { get; }

    IReadOnlyList<string> GetSheetNames();

    string? GetName(int column);

    /// <summary>Advances to the next data row; header handling is source-specific.</summary>
    bool Read();

    /// <summary>Canonical cell text for the type analyzer; null skips the cell.</summary>
    string? GetCellText(int column);

    /// <summary>Raw cell length used for NVARCHAR sizing.</summary>
    int GetRawLength(int column);

    /// <summary>0..1 read progress for progress reporting (CSV); 0 otherwise.</summary>
    double ReadProgress { get; }

    /// <summary>
    /// Creates a streaming typed reader over the current sheet honoring the selected kinds.
    /// The returned reader must not outlive this source unless the caller guarantees it.
    /// </summary>
    IDataReader CreateTypedReader(IReadOnlyList<ImportColumnKind> kinds, IReadOnlyList<string> normalizedHeaders);
}
