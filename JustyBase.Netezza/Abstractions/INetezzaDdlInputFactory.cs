using JustyBase.Netezza.Models;
using JustyBase.NetezzaDdl.Models;

namespace JustyBase.Netezza.Abstractions;

/// <summary>
/// Converts neutral Netezza metadata models into the shared DDL package input models
/// used by <c>JustyBase.NetezzaDdl</c> to generate SQL DDL statements.
/// </summary>
public interface INetezzaDdlInputFactory
{
    /// <summary>Builds DDL input for a regular table.</summary>
    NetezzaTableDdlInput BuildTable(
        NetezzaSchemaTable table,
        IReadOnlyList<string>? distributeColumns = null,
        IReadOnlyList<string>? organizeColumns = null,
        IReadOnlyList<NetezzaKeyDdl>? keys = null,
        string? overrideTableName = null,
        string? middleCode = null,
        string? endingCode = null,
        string? tableOwner = null);

    /// <summary>Builds DDL input for an external table.</summary>
    NetezzaExternalDdlInput BuildExternal(NetezzaSchemaTable table, NetezzaExternalTableOptions options);

    /// <summary>Builds DDL input for a procedure from its definition.</summary>
    NetezzaProcedureDdlInput BuildProcedure(NetezzaProcedureDefinition procedure);

    /// <summary>
    /// Builds DDL input for a procedure by parsing the catalog-style
    /// <paramref name="signature"/> (e.g. <c>PUBLIC.CALCULATE_TOTAL(INTEGER)</c>).
    /// </summary>
    NetezzaProcedureDdlInput BuildProcedureFromSignature(
        string database,
        string schema,
        string signature,
        string returns,
        string source,
        bool executeAsOwner = false,
        string? description = null);
}
