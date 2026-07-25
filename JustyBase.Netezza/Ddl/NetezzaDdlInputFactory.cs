using JustyBase.Netezza.Abstractions;
using JustyBase.Netezza.Models;
using JustyBase.NetezzaDdl.Models;

namespace JustyBase.Netezza.Ddl;

/// <summary>Converts neutral metadata into the shared Netezza DDL input models.</summary>
public static class NetezzaDdlInputFactory
{
    /// <summary>Default singleton instance that implements <see cref="INetezzaDdlInputFactory"/>.</summary>
    public static INetezzaDdlInputFactory Default { get; } = new DefaultNetezzaDdlInputFactory();

    /// <summary>Builds DDL input for a regular table.</summary>
    public static NetezzaTableDdlInput BuildTable(
        NetezzaSchemaTable table,
        IReadOnlyList<string>? distributeColumns = null,
        IReadOnlyList<string>? organizeColumns = null,
        IReadOnlyList<NetezzaKeyDdl>? keys = null,
        string? overrideTableName = null,
        string? middleCode = null,
        string? endingCode = null,
        string? tableOwner = null)
        => Default.BuildTable(table, distributeColumns, organizeColumns, keys, overrideTableName, middleCode, endingCode, tableOwner);

    /// <summary>Builds DDL input for an external table.</summary>
    public static NetezzaExternalDdlInput BuildExternal(
        NetezzaSchemaTable table,
        NetezzaExternalTableOptions options)
        => Default.BuildExternal(table, options);

    /// <summary>Builds DDL input for a procedure from its definition.</summary>
    public static NetezzaProcedureDdlInput BuildProcedure(NetezzaProcedureDefinition procedure)
        => Default.BuildProcedure(procedure);

    /// <summary>
    /// Builds DDL input for a procedure by parsing the catalog-style
    /// <paramref name="signature"/> (e.g. <c>PUBLIC.CALCULATE_TOTAL(INTEGER)</c>).
    /// </summary>
    public static NetezzaProcedureDdlInput BuildProcedureFromSignature(
        string database,
        string schema,
        string signature,
        string returns,
        string source,
        bool executeAsOwner = false,
        string? description = null)
        => Default.BuildProcedureFromSignature(database, schema, signature, returns, source, executeAsOwner, description);
}

internal sealed class DefaultNetezzaDdlInputFactory : INetezzaDdlInputFactory
{
    public NetezzaTableDdlInput BuildTable(
        NetezzaSchemaTable table,
        IReadOnlyList<string>? distributeColumns = null,
        IReadOnlyList<string>? organizeColumns = null,
        IReadOnlyList<NetezzaKeyDdl>? keys = null,
        string? overrideTableName = null,
        string? middleCode = null,
        string? endingCode = null,
        string? tableOwner = null)
    {
        ArgumentNullException.ThrowIfNull(table);

        var columns = table.Columns?.Select(NetezzaColumnCatalogMapper.ToColumnDdl).ToArray() ?? [];

        return new NetezzaTableDdlInput(
            table.Database ?? string.Empty,
            table.Schema ?? string.Empty,
            table.Name,
            columns,
            distributeColumns,
            organizeColumns,
            keys,
            table.Description,
            TableOwner: tableOwner,
            OverrideTableName: overrideTableName,
            MiddleCode: middleCode,
            EndingCode: endingCode);
    }

    public NetezzaExternalDdlInput BuildExternal(
        NetezzaSchemaTable table,
        NetezzaExternalTableOptions options)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(options);

        var columns = table.Columns?.Select(NetezzaColumnCatalogMapper.ToColumnDdl).ToArray() ?? [];

        return new NetezzaExternalDdlInput(
            table.Database ?? string.Empty,
            table.Schema ?? string.Empty,
            table.Name,
            columns,
            options);
    }

    public NetezzaProcedureDdlInput BuildProcedure(NetezzaProcedureDefinition procedure)
    {
        ArgumentNullException.ThrowIfNull(procedure);

        return new NetezzaProcedureDdlInput(
            procedure.Database,
            procedure.Schema,
            procedure.Name,
            procedure.Returns,
            procedure.Source,
            procedure.Arguments,
            procedure.ExecuteAsOwner,
            procedure.Description);
    }

    public NetezzaProcedureDdlInput BuildProcedureFromSignature(
        string database,
        string schema,
        string signature,
        string returns,
        string source,
        bool executeAsOwner = false,
        string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signature);
        int argumentStart = signature.IndexOf('(');
        string name = argumentStart >= 0 ? signature[..argumentStart] : signature;
        int qualification = name.LastIndexOf('.');
        if (qualification >= 0)
            name = name[(qualification + 1)..];

        string? arguments = argumentStart >= 0 ? signature[argumentStart..] : null;
        return BuildProcedure(new NetezzaProcedureDefinition(
            database,
            schema,
            name,
            returns,
            source,
            arguments,
            executeAsOwner,
            description));
    }
}
