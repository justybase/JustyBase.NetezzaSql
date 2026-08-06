using System.Data.Common;

namespace JustyBase.ImportExport.Import;

/// <summary>
/// Executes an <see cref="IImportJob"/> into a target table on a caller-supplied ADO.NET
/// connection. Engines are dialect-specific (external-table pipe, batch INSERT, COPY...);
/// hosts select one per database and keep only connection plumbing.
/// </summary>
public interface IImportEngine
{
    Task ExecuteAsync(
        DbConnection connection,
        IImportJob job,
        string targetTableName,
        ImportEngineOptions options,
        Action<string>? progress,
        CancellationToken cancellationToken = default);
}