namespace JustyBase.NetezzaSqlParser.Authoring;

/// <summary>MySQL 8 authoring metadata based on the private reference dialect.</summary>
public sealed class MySqlSqlCatalog : ISqlAuthoringCatalog
{
    private static readonly IReadOnlyList<NetezzaDataTypeSpec> MySqlTypes =
    [
        new("TINYINT", ["TINYINT"]), new("SMALLINT", ["SMALLINT"]),
        new("MEDIUMINT", ["MEDIUMINT"]), new("INT", ["INT", "INTEGER"]),
        new("BIGINT", ["BIGINT"]), new("DECIMAL", ["DECIMAL", "NUMERIC"], 1, 2),
        new("FLOAT", ["FLOAT"]), new("DOUBLE", ["DOUBLE"]), new("BIT", ["BIT"]),
        new("CHAR", ["CHAR"], 1, 1, true), new("VARCHAR", ["VARCHAR"], 1, 1, true),
        new("BINARY", ["BINARY"]), new("VARBINARY", ["VARBINARY"]),
        new("TINYTEXT", ["TINYTEXT"]), new("TEXT", ["TEXT"]),
        new("MEDIUMTEXT", ["MEDIUMTEXT"]), new("LONGTEXT", ["LONGTEXT"]),
        new("TINYBLOB", ["TINYBLOB"]), new("BLOB", ["BLOB"]),
        new("MEDIUMBLOB", ["MEDIUMBLOB"]), new("LONGBLOB", ["LONGBLOB"]),
        new("DATE", ["DATE"]), new("TIME", ["TIME"]), new("DATETIME", ["DATETIME"]),
        new("TIMESTAMP", ["TIMESTAMP"]), new("YEAR", ["YEAR"]),
        new("JSON", ["JSON"]), new("ENUM", ["ENUM"]), new("SET", ["SET"]),
        new("BOOLEAN", ["BOOLEAN", "BOOL"]), new("REAL", ["REAL"]),
        new("GEOMETRY", ["GEOMETRY"]), new("POINT", ["POINT"]),
    ];

    private static IReadOnlyList<NetezzaBuiltinFunction> MySqlFunctions =>
    [
        Function("IF", NetezzaFunctionCategory.Conversion, "IF(condition, true_value, false_value)"),
        Function("IFNULL", NetezzaFunctionCategory.Conversion, "IFNULL(expression, replacement)"),
        Function("NOW", NetezzaFunctionCategory.DateTime, "NOW()"),
        Function("CURDATE", NetezzaFunctionCategory.DateTime, "CURDATE()"),
        Function("CURTIME", NetezzaFunctionCategory.DateTime, "CURTIME()"),
        Function("DATE_FORMAT", NetezzaFunctionCategory.DateTime, "DATE_FORMAT(date, format)"),
        Function("STR_TO_DATE", NetezzaFunctionCategory.DateTime, "STR_TO_DATE(string, format)"),
        Function("GROUP_CONCAT", NetezzaFunctionCategory.Aggregate, "GROUP_CONCAT(expression)"),
        Function("JSON_ARRAY", NetezzaFunctionCategory.Conversion, "JSON_ARRAY(value, ...)"),
        Function("JSON_EXTRACT", NetezzaFunctionCategory.Conversion, "JSON_EXTRACT(json_doc, path)"),
        Function("JSON_OBJECT", NetezzaFunctionCategory.Conversion, "JSON_OBJECT(key, value, ...)"),
        Function("JSON_UNQUOTE", NetezzaFunctionCategory.Conversion, "JSON_UNQUOTE(json_value)"),
        Function("FIND_IN_SET", NetezzaFunctionCategory.String, "FIND_IN_SET(string, string_list)"),
        Function("LAST_INSERT_ID", NetezzaFunctionCategory.System, "LAST_INSERT_ID()"),
        Function("UUID", NetezzaFunctionCategory.System, "UUID()"),
        Function("VERSION", NetezzaFunctionCategory.System, "VERSION()"),
    ];

    private static NetezzaBuiltinFunction Function(string name, NetezzaFunctionCategory category, string label) =>
        new(name, category, [new NetezzaFunctionSignature(label, "MySQL 8 built-in function.", [])]);

    public static MySqlSqlCatalog Instance { get; } = new();

    public IReadOnlyList<NetezzaBuiltinFunction> BuiltinFunctions { get; } =
        SqlAuthoringCatalogComposer.MergeFunctions(AnsiSqlCatalog.BuiltinFunctions, MySqlFunctions);

    public IReadOnlyList<NetezzaDataTypeSpec> DataTypes { get; } =
        SqlAuthoringCatalogComposer.MergeTypes(AnsiSqlCatalog.DataTypes, MySqlTypes);

    public IReadOnlyList<string> DataTypeNames { get; } =
        SqlAuthoringCatalogComposer.MergeTypes(AnsiSqlCatalog.DataTypes, MySqlTypes)
            .SelectMany(t => t.Aliases).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public IReadOnlyList<string> CompletionKeywords { get; } =
        SqlAuthoringCatalogComposer.MergeValues(AnsiSqlCatalog.CompletionKeywords,
            ["ENGINE", "AUTO_INCREMENT", "ON DUPLICATE KEY UPDATE", "CHARSET", "COLLATE", "JSON", "SET", "ENUM"]);

    public IReadOnlyList<string> Keywords { get; } =
        SqlAuthoringCatalogComposer.MergeValues(AnsiSqlCatalog.Keywords,
            ["INSERT IGNORE", "ON DUPLICATE KEY UPDATE", "ENGINE", "AUTO_INCREMENT", "CHARSET", "COLLATE", "JSON"]);

    public SqlFormatterProfile FormatterProfile { get; } =
        SqlAuthoringCatalogComposer.MergeFormatterProfiles(AnsiSqlCatalog.FormatterProfile,
            new SqlFormatterProfile(["GROUP BY", "ORDER BY", "LIMIT", "OFFSET", "RETURNING"]));

    public bool TryGetFunction(string name, out NetezzaBuiltinFunction function)
    {
        function = BuiltinFunctions.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))!;
        return function is not null;
    }

    public bool TryGetDataType(string name, out NetezzaDataTypeSpec type)
    {
        var normalized = name.Trim().ToUpperInvariant();
        var paren = normalized.IndexOf('(');
        if (paren >= 0) normalized = normalized[..paren].Trim();
        type = DataTypes.FirstOrDefault(t => t.Aliases.Any(a => string.Equals(a, normalized, StringComparison.OrdinalIgnoreCase)))!;
        return type is not null;
    }
}
