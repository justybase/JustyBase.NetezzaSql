namespace JustyBase.NetezzaDdl.Models;

/// <summary>Metadata required to render a Netezza NZPLSQL procedure definition.</summary>
public sealed record NetezzaProcedureDdlInput(
    string Database,
    string Schema,
    string ProcedureName,
    string Returns,
    string ProcedureSource,
    string? Arguments = null,
    bool ExecuteAsOwner = false,
    string? Description = null);
