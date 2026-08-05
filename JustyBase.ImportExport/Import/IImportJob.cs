using System.Data;

namespace JustyBase.ImportExport.Import;

/// <summary>
/// Neutral import job contract consumed by the shared orchestrator and by the host
/// plugin seam (<c>DbSpecificImportPart</c>). The reader streams typed values; the
/// columns carry the detected (or user-overridden) target types.
/// </summary>
public interface IImportJob
{
    IDataReader AsReader { get; set; }

    IReadOnlyList<string> ColumnHeadersNames { get; }

    IReadOnlyList<IImportColumn> Columns { get; }

    IReadOnlyList<string[]>? PreviewRows { get; }

    long RowsCount { get; }

    string? SourceSheetName { get; }

    /// <summary>Renders <c>"NAME TYPE"</c> column definitions for CREATE TABLE.</summary>
    string[] ReturnHeadersWithDataTypes(DatabaseKind databaseKind);
}
