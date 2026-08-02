namespace JustyBase.NetezzaSqlParser.Authoring;

/// <summary>
/// Dialect-neutral SQL authoring metadata: built-in functions, data types and
/// completion keywords. Implemented by NetezzaSqlAuthoringCatalog (default) and
/// OracleSqlCatalog; injected into completion, hover, signature help and
/// alter-table services so their suggestions follow the active dialect.
/// Port of DatabaseSqlAuthoring from src/sql/authoring/types.ts (reference
/// TypeScript project).
/// </summary>
public interface ISqlAuthoringCatalog
{
    /// <summary>Built-in functions offered by the dialect.</summary>
    IReadOnlyList<NetezzaBuiltinFunction> BuiltinFunctions { get; }

    /// <summary>Accepted data type names of the dialect.</summary>
    IReadOnlyList<NetezzaDataTypeSpec> DataTypes { get; }

    /// <summary>Flat list of all data type names (aliases included).</summary>
    IReadOnlyList<string> DataTypeNames { get; }

    /// <summary>Dialect-specific completion keywords (top-level context).</summary>
    IReadOnlyList<string> CompletionKeywords { get; }

    /// <summary>Dialect keywords shown by hover for non-identifier words.</summary>
    IReadOnlyList<string> Keywords { get; }

    bool TryGetFunction(string name, out NetezzaBuiltinFunction function);
    bool TryGetDataType(string name, out NetezzaDataTypeSpec type);
}

/// <summary>
/// Netezza authoring catalog. Adapter over the static NetezzaSqlCatalog so the
/// shared services can take ISqlAuthoringCatalog without changing existing call
/// sites (default = Netezza).
/// </summary>
public sealed class NetezzaSqlAuthoringCatalog : ISqlAuthoringCatalog
{
    public static NetezzaSqlAuthoringCatalog Instance { get; } = new();

    public IReadOnlyList<NetezzaBuiltinFunction> BuiltinFunctions => NetezzaSqlCatalog.BuiltinFunctions;
    public IReadOnlyList<NetezzaDataTypeSpec> DataTypes => NetezzaSqlCatalog.DataTypes;
    public IReadOnlyList<string> DataTypeNames => NetezzaSqlCatalog.DataTypeNames;
    public IReadOnlyList<string> CompletionKeywords => NetezzaSqlCatalog.CompletionKeywords;
    public IReadOnlyList<string> Keywords => NetezzaSqlCatalog.NetezzaKeywords;
    public SqlFormatterProfile FormatterProfile => NetezzaSqlCatalog.FormatterProfile;

    public bool TryGetFunction(string name, out NetezzaBuiltinFunction function) =>
        NetezzaSqlCatalog.TryGetFunction(name, out function);

    public bool TryGetDataType(string name, out NetezzaDataTypeSpec type) =>
        NetezzaSqlCatalog.TryGetDataType(name, out type);
}
