namespace JustyBase.ImportExport.Import;

/// <summary>Value-vs-type validation issue found during import preview.</summary>
public sealed record ImportValidationError(
    string SheetName,
    int RowNumber,
    int ColumnIndex,
    string ColumnName,
    ImportColumnKind SelectedKind,
    string Value,
    string Message);

/// <summary>Thrown when a sheet fails value-vs-type validation before the load starts.</summary>
public sealed class ImportValidationException : Exception
{
    public ImportValidationException(IReadOnlyList<ImportValidationError> errors)
        : base("Import validation failed.")
    {
        Errors = errors;
    }

    public IReadOnlyList<ImportValidationError> Errors { get; }
}
