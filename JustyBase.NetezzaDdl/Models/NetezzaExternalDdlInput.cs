namespace JustyBase.NetezzaDdl.Models;

/// <summary>Input data required to render a <c>CREATE EXTERNAL TABLE</c> statement.</summary>
/// <param name="Database">Owning database name.</param>
/// <param name="Schema">Owning schema name.</param>
/// <param name="TableName">External table name.</param>
/// <param name="Columns">Column definitions.</param>
/// <param name="Options">External table configuration options.</param>
public sealed record NetezzaExternalDdlInput(
    string Database,
    string Schema,
    string TableName,
    IReadOnlyList<NetezzaColumnDdl> Columns,
    NetezzaExternalTableOptions Options);
