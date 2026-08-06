using System.Text;

namespace JustyBase.ImportExport.Import;

/// <summary>
/// Creates concrete <see cref="IImportSource"/> instances for the shared
/// <see cref="TabularImportScanner"/>. Hosts implement this over their native readers
/// (e.g. SpreadSheetTasks Excel readers); the CSV/Excel concern derives from the path.
/// </summary>
public interface IImportSourceFactory
{
    IImportSource OpenSource(string filePath, Encoding? encoding);

    /// <summary>true when the file can only be read through one open source at a time (e.g. xlsb).</summary>
    bool IsExclusiveOpen(string filePath);
}