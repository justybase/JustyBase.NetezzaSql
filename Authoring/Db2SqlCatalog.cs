namespace JustyBase.NetezzaSqlParser.Authoring;

/// <summary>
/// Db2 LUW SQL authoring catalog. Port of
/// extensions/db2/src/sql/authoring.ts (completion keywords, type specs,
/// function signatures) from the reference TypeScript project.
/// </summary>
public sealed class Db2SqlCatalog : ISqlAuthoringCatalog
{
    private static IReadOnlyList<NetezzaDataTypeSpec> Db2DataTypes { get; } =
    [
        new("SMALLINT", ["SMALLINT"]),
        new("INTEGER", ["INTEGER", "INT"]),
        new("BIGINT", ["BIGINT"]),
        new("DECIMAL", ["DECIMAL"], 1, 2),
        new("NUMERIC", ["NUMERIC"], 1, 2),
        new("DECFLOAT", ["DECFLOAT"], 0, 1),
        new("REAL", ["REAL"]),
        new("DOUBLE", ["DOUBLE"]),
        new("CHAR", ["CHAR", "CHARACTER"], 1, 1, true),
        new("VARCHAR", ["VARCHAR", "CHARACTER VARYING"], 1, 1, true),
        new("GRAPHIC", ["GRAPHIC"], 1, 1, true),
        new("VARGRAPHIC", ["VARGRAPHIC"], 1, 1, true),
        new("CLOB", ["CLOB"], 0, 1),
        new("BLOB", ["BLOB"], 0, 1),
        new("XML", ["XML"]),
        new("DATE", ["DATE"]),
        new("TIME", ["TIME"]),
        new("TIMESTAMP", ["TIMESTAMP"], 0, 1),
    ];

    public static Db2SqlCatalog Instance { get; } = new();

    private Db2SqlCatalog()
    {
    }

    private static SqlSignatureParameterInfo Parameter(string label, string documentation)
        => new(label, documentation);

    private static NetezzaFunctionSignature Signature(
        string label,
        string documentation,
        params SqlSignatureParameterInfo[] parameters)
        => new(label, documentation, parameters);

    private static NetezzaBuiltinFunction Function(
        string name,
        NetezzaFunctionCategory category,
        params NetezzaFunctionSignature[] signatures)
        => new(name, category, signatures);

    public IReadOnlyList<NetezzaBuiltinFunction> BuiltinFunctions { get; } =
    [
        Function("COUNT", NetezzaFunctionCategory.Aggregate,
            Signature("COUNT(expression)", "Returns the number of non-null values for the expression.", Parameter("expression", "Expression or *."))),
        Function("COALESCE", NetezzaFunctionCategory.Conversion,
            Signature("COALESCE(value1, value2, ...)", "Returns the first non-null argument.", Parameter("value1", "First value."), Parameter("value2", "Second value."))),
        Function("CONCAT", NetezzaFunctionCategory.String,
            Signature("CONCAT(left, right)", "Concatenates two string expressions.", Parameter("left", "Left string."), Parameter("right", "Right string."))),
        Function("VARCHAR", NetezzaFunctionCategory.Conversion,
            Signature("VARCHAR(expression, length?)", "Casts or truncates an expression to VARCHAR.", Parameter("expression", "Value to cast."), Parameter("length", "Optional length."))),
    ];

    public IReadOnlyList<NetezzaBuiltinFunction> ValidationBuiltinFunctions { get; } =
        new[]
        {
            "ABS", "AVG", "CAST", "CEIL", "CEILING", "CHAR", "COALESCE", "CONCAT",
            "COUNT", "CURRENT DATE", "CURRENT TIME", "CURRENT TIMESTAMP", "CURRENT USER",
            "DECIMAL", "FLOOR", "HEX", "INTEGER", "LENGTH", "LOCATE", "LOWER", "LTRIM",
            "MAX", "MIN", "MOD", "NULLIF", "POSSTR", "ROUND", "RTRIM", "SUBSTR", "SUM",
            "TRIM", "UPPER", "VARCHAR", "VALUE", "XMLSERIALIZE",
        }.Select(name => new NetezzaBuiltinFunction(name, NetezzaFunctionCategory.System,
            [new NetezzaFunctionSignature($"{name}(...)", "Db2 built-in function.", [])])).ToArray();

    public IReadOnlyList<NetezzaDataTypeSpec> DataTypes => Db2DataTypes;

    public IReadOnlyList<string> DataTypeNames { get; } =
        Db2DataTypes.SelectMany(t => t.Aliases).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public IReadOnlyList<string> CompletionKeywords { get; } =
    [
        "SELECT", "FROM", "WHERE", "INSERT", "UPDATE", "DELETE", "MERGE", "CALL",
        "CREATE", "ALTER", "DROP", "TABLE", "VIEW", "PROCEDURE", "FUNCTION", "SEQUENCE",
        "TRIGGER", "ALIAS", "INDEX", "VALUES", "IDENTITY", "GENERATED", "ALWAYS",
        "BY DEFAULT", "FETCH FIRST", "OPTIMIZE FOR", "FOR READ ONLY", "FOR UPDATE",
        "WITH UR", "WITH CS", "WITH RS", "WITH RR", "FINAL TABLE",
        "DECLARE GLOBAL TEMPORARY", "ORGANIZE BY", "DATA CAPTURE", "LANGUAGE SQL",
        "ORDER BY", "GROUP BY", "HAVING", "UNION", "INTERSECT", "EXCEPT",
        "CURRENT SCHEMA", "CURRENT SERVER", "CURRENT DATE", "CURRENT TIME",
        "CURRENT TIMESTAMP", "CURRENT USER", "NICKNAME",
    ];

    public IReadOnlyList<string> Keywords { get; } =
    [
        "SELECT", "FROM", "WHERE", "GROUP", "BY", "ORDER", "FETCH", "FIRST",
        "NEXT", "ROWS", "ROW", "ONLY", "INSERT", "UPDATE", "DELETE", "MERGE",
        "INTO", "VALUES", "SET", "WITH", "JOIN", "INNER", "LEFT", "RIGHT", "FULL",
        "OUTER", "CROSS", "ON", "AND", "OR", "NOT", "NULL", "AS", "CREATE", "ALTER",
        "DROP", "TABLE", "VIEW", "PROCEDURE", "FUNCTION", "ALIAS", "NICKNAME",
        "OPTIMIZE", "FOR", "READ", "UR", "CS", "RS", "RR", "FINAL", "DECLARE",
        "GLOBAL", "TEMPORARY", "LANGUAGE", "SQL", "IDENTITY", "GENERATED",
    ];

    public bool TryGetFunction(string name, out NetezzaBuiltinFunction function)
    {
        function = BuiltinFunctions.FirstOrDefault(f =>
            string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))!;
        return function is not null;
    }

    public bool TryGetDataType(string name, out NetezzaDataTypeSpec type)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            type = null!;
            return false;
        }

        var normalized = name.Trim().ToUpperInvariant();
        var baseName = RegexStripParams(normalized);
        if (baseName is "CHARACTER VARYING" or "CHARACTER")
            baseName = "VARCHAR";
        if (baseName == "INT")
            baseName = "INTEGER";

        type = DataTypes.FirstOrDefault(t =>
            t.Aliases.Any(a => string.Equals(a, baseName, StringComparison.OrdinalIgnoreCase))
            || string.Equals(t.CanonicalName, baseName, StringComparison.OrdinalIgnoreCase))!;
        return type is not null;
    }

    private static string RegexStripParams(string normalized)
    {
        var paren = normalized.IndexOf('(');
        return paren < 0 ? normalized : normalized[..paren].Trim();
    }
}
