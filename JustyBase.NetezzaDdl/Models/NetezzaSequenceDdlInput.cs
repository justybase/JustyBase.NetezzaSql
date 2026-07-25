namespace JustyBase.NetezzaDdl.Models;

/// <summary>Input for rendering CREATE SEQUENCE DDL from catalog metadata.</summary>
public sealed record NetezzaSequenceDdlInput(
    string Database,
    string Schema,
    string SequenceName,
    string DataType,
    string StartWith,
    string IncrementBy,
    string? MinValue = null,
    string? MaxValue = null,
    bool Cycle = false);
