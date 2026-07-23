namespace JustyBase.NetezzaDdl.Models;

/// <summary>Metadata required to render a Netezza synonym definition.</summary>
public sealed record NetezzaSynonymDdlInput(
    string Database,
    string Schema,
    string SynonymName,
    string ReferencedObject,
    string? Owner = null,
    string? Description = null);
