using System.Data;

namespace JustyBase.ImportExport.Import;

/// <summary>
/// Default <see cref="IImportJob"/> implementation used by the shared XML/clipboard
/// pipeline. Hosts keep their own adapter over this interface for the CSV/Excel path.
/// </summary>
public class ImportJob : IImportJob
{
    public ImportJob(
        IDataReader reader,
        IReadOnlyList<IImportColumn> columns,
        long rowsCount = -1,
        IReadOnlyList<string[]>? previewRows = null,
        string? sourceSheetName = null)
    {
        AsReader = reader ?? throw new ArgumentNullException(nameof(reader));
        Columns = columns ?? throw new ArgumentNullException(nameof(columns));
        ColumnHeadersNames = columns.Select(static c => c.Name).ToArray();
        RowsCount = rowsCount;
        PreviewRows = previewRows;
        SourceSheetName = sourceSheetName;
    }

    protected ImportJob()
    {
    }

    public IDataReader AsReader { get; set; } = null!;

    public IReadOnlyList<string> ColumnHeadersNames { get; protected set; } = [];

    public IReadOnlyList<IImportColumn> Columns { get; protected set; } = [];

    public IReadOnlyList<string[]>? PreviewRows { get; }

    public long RowsCount { get; protected set; }

    public string? SourceSheetName { get; init; }

    public string[] ReturnHeadersWithDataTypes(DatabaseKind databaseKind)
        => Columns.Select(c => $"{c.Name} {c.RenderDdl(databaseKind)}").ToArray();
}
