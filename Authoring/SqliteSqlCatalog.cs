namespace JustyBase.NetezzaSqlParser.Authoring;

/// <summary>
/// SQLite authoring metadata based on the private dialect profile and the
/// SQLite core function reference (JSON1, date/time, math, string, aggregate
/// and window functions).
/// </summary>
public sealed class SqliteSqlCatalog : ISqlAuthoringCatalog
{
    private static readonly IReadOnlyList<NetezzaDataTypeSpec> SqliteTypes =
    [
        new("INTEGER", ["INTEGER", "INT"]),
        new("BIGINT", ["BIGINT"]),
        new("SMALLINT", ["SMALLINT"]),
        new("MEDIUMINT", ["MEDIUMINT", "INT8", "INT2"]),
        new("REAL", ["REAL", "FLOAT", "DOUBLE", "DOUBLE PRECISION"]),
        new("NUMERIC", ["NUMERIC", "DECIMAL", "DEC"]),
        new("TEXT", ["TEXT", "VARCHAR", "CHAR", "CLOB", "CHARACTER", "CHARACTER VARYING"]),
        new("BLOB", ["BLOB"]),
        new("BOOLEAN", ["BOOLEAN"]),
        new("DATE", ["DATE"]),
        new("DATETIME", ["DATETIME"]),
        new("TIME", ["TIME"]),
        new("TIMESTAMP", ["TIMESTAMP"]),
    ];

    private static NetezzaBuiltinFunction Function(string name, NetezzaFunctionCategory category, string label) =>
        new(name, category, [new NetezzaFunctionSignature(label, "SQLite built-in function.", [])]);

    private static readonly IReadOnlyList<NetezzaBuiltinFunction> SqliteFunctions =
    [
        Function("CHANGES", NetezzaFunctionCategory.System, "CHANGES()"),
        Function("CHAR", NetezzaFunctionCategory.String, "CHAR(value1, value2, ...)"),
        Function("DATE", NetezzaFunctionCategory.DateTime, "DATE(timestring [, modifier, ...])"),
        Function("DATETIME", NetezzaFunctionCategory.DateTime, "DATETIME(timestring [, modifier, ...])"),
        Function("FORMAT", NetezzaFunctionCategory.String, "FORMAT(format, value1, value2, ...)"),
        Function("GLOB", NetezzaFunctionCategory.String, "GLOB(pattern, string)"),
        Function("GROUP_CONCAT", NetezzaFunctionCategory.Aggregate, "GROUP_CONCAT(expression [, separator])"),
        Function("HEX", NetezzaFunctionCategory.Conversion, "HEX(blob)"),
        Function("IFNULL", NetezzaFunctionCategory.Conversion, "IFNULL(value1, value2)"),
        Function("IIF", NetezzaFunctionCategory.Conversion, "IIF(condition, trueValue, falseValue)"),
        Function("INSTR", NetezzaFunctionCategory.String, "INSTR(string, substring)"),
        Function("JSON", NetezzaFunctionCategory.Conversion, "JSON(value)"),
        Function("JSON_ARRAY", NetezzaFunctionCategory.Conversion, "JSON_ARRAY(value1, value2, ...)"),
        Function("JSON_ARRAY_LENGTH", NetezzaFunctionCategory.Conversion, "JSON_ARRAY_LENGTH(json [, path])"),
        Function("JSON_EXTRACT", NetezzaFunctionCategory.Conversion, "JSON_EXTRACT(json, path1, path2, ...)"),
        Function("JSON_INSERT", NetezzaFunctionCategory.Conversion, "JSON_INSERT(json, path, value, ...)"),
        Function("JSON_OBJECT", NetezzaFunctionCategory.Conversion, "JSON_OBJECT(key1, value1, ...)"),
        Function("JSON_PATCH", NetezzaFunctionCategory.Conversion, "JSON_PATCH(json, patch)"),
        Function("JSON_QUOTE", NetezzaFunctionCategory.Conversion, "JSON_QUOTE(value)"),
        Function("JSON_REMOVE", NetezzaFunctionCategory.Conversion, "JSON_REMOVE(json, path1, path2, ...)"),
        Function("JSON_REPLACE", NetezzaFunctionCategory.Conversion, "JSON_REPLACE(json, path, value, ...)"),
        Function("JSON_SET", NetezzaFunctionCategory.Conversion, "JSON_SET(json, path, value, ...)"),
        Function("JSON_TYPE", NetezzaFunctionCategory.Conversion, "JSON_TYPE(json [, path])"),
        Function("JSON_VALID", NetezzaFunctionCategory.Conversion, "JSON_VALID(json)"),
        Function("JULIANDAY", NetezzaFunctionCategory.DateTime, "JULIANDAY(timestring [, modifier, ...])"),
        Function("LAST_INSERT_ROWID", NetezzaFunctionCategory.System, "LAST_INSERT_ROWID()"),
        Function("LIKELIHOOD", NetezzaFunctionCategory.System, "LIKELIHOOD(value, probability)"),
        Function("LN", NetezzaFunctionCategory.Numeric, "LN(value)"),
        Function("LOG", NetezzaFunctionCategory.Numeric, "LOG(base, value)"),
        Function("LOG10", NetezzaFunctionCategory.Numeric, "LOG10(value)"),
        Function("LOG2", NetezzaFunctionCategory.Numeric, "LOG2(value)"),
        Function("LTRIM", NetezzaFunctionCategory.String, "LTRIM(string [, characters])"),
        Function("OCTET_LENGTH", NetezzaFunctionCategory.String, "OCTET_LENGTH(string)"),
        Function("PRINTF", NetezzaFunctionCategory.String, "PRINTF(format, value1, value2, ...)"),
        Function("QUOTE", NetezzaFunctionCategory.Conversion, "QUOTE(value)"),
        Function("RANDOM", NetezzaFunctionCategory.Numeric, "RANDOM()"),
        Function("RANDOMBLOB", NetezzaFunctionCategory.Numeric, "RANDOMBLOB(size)"),
        Function("RTRIM", NetezzaFunctionCategory.String, "RTRIM(string [, characters])"),
        Function("SIGN", NetezzaFunctionCategory.Numeric, "SIGN(value)"),
        Function("SOUNDEX", NetezzaFunctionCategory.String, "SOUNDEX(string)"),
        Function("STRFTIME", NetezzaFunctionCategory.DateTime, "STRFTIME(format, timestring [, modifier, ...])"),
        Function("SUBSTR", NetezzaFunctionCategory.String, "SUBSTR(string, start [, length])"),
        Function("TOTAL", NetezzaFunctionCategory.Aggregate, "TOTAL(expression)"),
        Function("TOTAL_CHANGES", NetezzaFunctionCategory.System, "TOTAL_CHANGES()"),
        Function("TRIM", NetezzaFunctionCategory.String, "TRIM(string [, characters])"),
        Function("UNICODE", NetezzaFunctionCategory.String, "UNICODE(string)"),
        Function("UNIXEPOCH", NetezzaFunctionCategory.DateTime, "UNIXEPOCH(timestring [, modifier, ...])"),
        Function("UNIX_TIMESTAMP", NetezzaFunctionCategory.DateTime, "UNIX_TIMESTAMP(timestring [, modifier, ...])"),
        Function("ZEROBLOB", NetezzaFunctionCategory.String, "ZEROBLOB(size)"),
    ];

    public static SqliteSqlCatalog Instance { get; } = new();
    public IReadOnlyList<NetezzaBuiltinFunction> BuiltinFunctions { get; } =
        SqlAuthoringCatalogComposer.MergeFunctions(AnsiSqlCatalog.BuiltinFunctions, SqliteFunctions);
    public IReadOnlyList<NetezzaDataTypeSpec> DataTypes { get; } =
        SqlAuthoringCatalogComposer.MergeTypes(AnsiSqlCatalog.DataTypes, SqliteTypes);
    public IReadOnlyList<string> DataTypeNames { get; } =
        SqlAuthoringCatalogComposer.MergeTypes(AnsiSqlCatalog.DataTypes, SqliteTypes)
            .SelectMany(t => t.Aliases).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    public IReadOnlyList<string> CompletionKeywords { get; } =
        SqlAuthoringCatalogComposer.MergeValues(AnsiSqlCatalog.CompletionKeywords,
            ["ATTACH", "DETACH", "AUTOINCREMENT", "COLLATE", "CONFLICT", "EXPLAIN",
             "GENERATED", "INDEX", "PRAGMA", "REINDEX", "RETURNING", "SAVEPOINT",
             "RELEASE", "STRICT", "TRIGGER", "VACUUM", "VIRTUAL", "WINDOW", "WITHOUT",
             "ROWID", "ON CONFLICT", "DO NOTHING", "DO UPDATE", "LIMIT", "USING"]);
    public IReadOnlyList<string> Keywords { get; } =
        SqlAuthoringCatalogComposer.MergeValues(AnsiSqlCatalog.Keywords,
            ["ATTACH", "DETACH", "AUTOINCREMENT", "COLLATE", "CONFLICT", "DO", "GENERATED",
             "NOTHING", "PRAGMA", "RETURNING", "SAVEPOINT", "STRICT", "VACUUM", "VIRTUAL",
             "WINDOW", "WITHOUT", "ROWID"]);
    public SqlFormatterProfile FormatterProfile { get; } =
        SqlAuthoringCatalogComposer.MergeFormatterProfiles(AnsiSqlCatalog.FormatterProfile,
            new SqlFormatterProfile(["LIMIT", "RETURNING", "WINDOW"]));

    public bool TryGetFunction(string name, out NetezzaBuiltinFunction function)
    {
        function = BuiltinFunctions.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))!;
        return function is not null;
    }

    public bool TryGetDataType(string name, out NetezzaDataTypeSpec type)
    {
        var normalized = name.Trim().ToUpperInvariant();
        type = DataTypes.FirstOrDefault(t => t.Aliases.Any(a => string.Equals(a, normalized, StringComparison.OrdinalIgnoreCase)))!;
        return type is not null;
    }
}
