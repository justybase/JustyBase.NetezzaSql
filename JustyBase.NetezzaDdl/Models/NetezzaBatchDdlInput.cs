namespace JustyBase.NetezzaDdl.Models;

/// <summary>
/// Objects to render in one deterministic Netezza deployment script.
/// Null or empty collections are ignored; the existing single-object builders
/// remain the source of truth for each individual statement.
/// </summary>
public sealed record NetezzaBatchDdlInput(
    IReadOnlyList<NetezzaTableDdlInput>? Tables = null,
    IReadOnlyList<NetezzaExternalDdlInput>? ExternalTables = null,
    IReadOnlyList<NetezzaViewDdlInput>? Views = null,
    IReadOnlyList<NetezzaProcedureDdlInput>? Procedures = null,
    IReadOnlyList<NetezzaSynonymDdlInput>? Synonyms = null,
    bool RecreateTables = false);

/// <summary>Result of batch DDL generation.</summary>
/// <param name="Sql">The generated deployment script.</param>
/// <param name="SkippedObjects">Names of objects skipped due to missing metadata.</param>
public sealed record NetezzaBatchDdlResult(
    string Sql,
    IReadOnlyList<string> SkippedObjects);
