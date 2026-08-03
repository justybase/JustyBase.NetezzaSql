namespace JustyBase.NetezzaSqlParser.Authoring;

/// <summary>PostgreSQL authoring metadata based on the private dialect profile.</summary>
public sealed class PostgreSqlSqlCatalog : ISqlAuthoringCatalog
{
    private static readonly IReadOnlyList<NetezzaDataTypeSpec> PostgreSqlTypes =
    [
        new("SMALLINT", ["SMALLINT"]), new("INTEGER", ["INTEGER", "INT"]),
        new("BIGINT", ["BIGINT"]), new("NUMERIC", ["NUMERIC", "DECIMAL"], 1, 2),
        new("REAL", ["REAL"]), new("DOUBLE PRECISION", ["DOUBLE PRECISION"]),
        new("BOOLEAN", ["BOOLEAN", "BOOL"]), new("SERIAL", ["SERIAL"]),
        new("BIGSERIAL", ["BIGSERIAL"]), new("SMALLSERIAL", ["SMALLSERIAL"]),
        new("CHAR", ["CHAR"], 1, 1, true), new("VARCHAR", ["VARCHAR"], 1, 1, true),
        new("CHARACTER VARYING", ["CHARACTER VARYING"], 1, 1, true),
        new("TEXT", ["TEXT"]), new("DATE", ["DATE"]), new("TIME", ["TIME"]),
        new("TIMESTAMP", ["TIMESTAMP"], 0, 1),
        new("TIMESTAMPTZ", ["TIMESTAMPTZ", "TIMESTAMP WITH TIME ZONE"], 0, 1),
        new("JSON", ["JSON"]), new("JSONB", ["JSONB"]), new("UUID", ["UUID"]),
        new("BYTEA", ["BYTEA"]), new("XML", ["XML"]),
    ];

    private static NetezzaBuiltinFunction Function(string name, NetezzaFunctionCategory category, string label) =>
        new(name, category, [new NetezzaFunctionSignature(label, "PostgreSQL built-in function.", [])]);

    private static readonly IReadOnlyList<NetezzaBuiltinFunction> PostgreSqlFunctions =
    [
        Function("ARRAY_AGG", NetezzaFunctionCategory.Aggregate, "ARRAY_AGG(expression)"),
        Function("DATE_TRUNC", NetezzaFunctionCategory.DateTime, "DATE_TRUNC(precision, source)"),
        Function("GENERATE_SERIES", NetezzaFunctionCategory.System, "GENERATE_SERIES(start, stop [, step])"),
        Function("JSONB_AGG", NetezzaFunctionCategory.Aggregate, "JSONB_AGG(expression)"),
        Function("JSONB_BUILD_OBJECT", NetezzaFunctionCategory.Conversion, "JSONB_BUILD_OBJECT(key, value, ...)"),
        Function("JSON_BUILD_OBJECT", NetezzaFunctionCategory.Conversion, "JSON_BUILD_OBJECT(key, value, ...)"),
        Function("NOW", NetezzaFunctionCategory.DateTime, "NOW()"),
        Function("STRING_AGG", NetezzaFunctionCategory.Aggregate, "STRING_AGG(expression, delimiter)"),
        Function("REGEXP_REPLACE", NetezzaFunctionCategory.String, "REGEXP_REPLACE(string, pattern, replacement)"),
    ];

    public static PostgreSqlSqlCatalog Instance { get; } = new();
    public IReadOnlyList<NetezzaBuiltinFunction> BuiltinFunctions { get; } =
        SqlAuthoringCatalogComposer.MergeFunctions(AnsiSqlCatalog.BuiltinFunctions, PostgreSqlFunctions);
    public IReadOnlyList<NetezzaDataTypeSpec> DataTypes { get; } =
        SqlAuthoringCatalogComposer.MergeTypes(AnsiSqlCatalog.DataTypes, PostgreSqlTypes);
    public IReadOnlyList<string> DataTypeNames { get; } =
        SqlAuthoringCatalogComposer.MergeTypes(AnsiSqlCatalog.DataTypes, PostgreSqlTypes)
            .SelectMany(t => t.Aliases).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    public IReadOnlyList<string> CompletionKeywords { get; } =
        SqlAuthoringCatalogComposer.MergeValues(AnsiSqlCatalog.CompletionKeywords,
            ["DISTINCT ON", "LATERAL", "RETURNING", "ON CONFLICT", "DO NOTHING", "DO UPDATE", "ARRAY",
             "MATERIALIZED VIEW", "VACUUM", "ANALYZE", "INDEX", "TRIGGER"]);
    public IReadOnlyList<string> Keywords { get; } =
        SqlAuthoringCatalogComposer.MergeValues(AnsiSqlCatalog.Keywords,
            ["DISTINCT ON", "LATERAL", "RETURNING", "ON CONFLICT", "CONFLICT", "ARRAY", "JSONB"]);
    public SqlFormatterProfile FormatterProfile { get; } =
        SqlAuthoringCatalogComposer.MergeFormatterProfiles(AnsiSqlCatalog.FormatterProfile,
            new SqlFormatterProfile(["GROUP BY", "HAVING", "ORDER BY", "LIMIT", "OFFSET", "RETURNING"]));

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
