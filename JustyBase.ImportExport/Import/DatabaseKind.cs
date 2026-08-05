namespace JustyBase.ImportExport.Import;

/// <summary>
/// Neutral SQL dialect keys used by the shared import pipeline. Mirrors the host
/// <c>DatabaseTypeEnum</c> (minus the unsupported marker) so DDL rendering and engine
/// dispatch stay library-side.
/// </summary>
public enum DatabaseKind
{
    Netezza,
    NetezzaOdbc,
    Db2,
    MsSql,
    Oracle,
    Sqlite,
    PostgreSql,
    DuckDb,
    MySql
}
