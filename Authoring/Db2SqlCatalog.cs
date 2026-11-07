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
        new("FLOAT", ["FLOAT"], 0, 1),
        new("CHAR", ["CHAR", "CHARACTER"], 1, 1, true),
        new("VARCHAR", ["VARCHAR", "CHARACTER VARYING"], 1, 1, true),
        new("GRAPHIC", ["GRAPHIC"], 1, 1, true),
        new("VARGRAPHIC", ["VARGRAPHIC"], 1, 1, true),
        new("BINARY", ["BINARY"], 1, 1, true),
        new("VARBINARY", ["VARBINARY"], 1, 1, true),
        new("CLOB", ["CLOB"], 0, 1),
        new("DBCLOB", ["DBCLOB"], 0, 1),
        new("BLOB", ["BLOB"], 0, 1),
        new("ROWID", ["ROWID"]),
        new("BOOLEAN", ["BOOLEAN"]),
        new("XML", ["XML"]),
        new("DATE", ["DATE"]),
        new("TIME", ["TIME"]),
        new("TIMESTAMP", ["TIMESTAMP"], 0, 1),
    ];

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

    /// <summary>
    /// Db2 LUW built-ins. The list mirrors the live-verified surface in the
    /// Rust/Tauri edition (SYSIBM.ROUTINES on LUW) instead of inheriting the
    /// Netezza function list.
    /// </summary>
    private static readonly (string Name, NetezzaFunctionCategory Category)[] Db2FunctionSpecs =
    [
        ("ABS", NetezzaFunctionCategory.Numeric),
        ("AVG", NetezzaFunctionCategory.Aggregate),
        ("BTRIM", NetezzaFunctionCategory.String),
        ("CHAR", NetezzaFunctionCategory.Conversion),
        ("COALESCE", NetezzaFunctionCategory.Conversion),
        ("CONCAT", NetezzaFunctionCategory.String),
        ("COUNT", NetezzaFunctionCategory.Aggregate),
        ("DAYNAME", NetezzaFunctionCategory.DateTime),
        ("DAYOFWEEK", NetezzaFunctionCategory.DateTime),
        ("DAYOFYEAR", NetezzaFunctionCategory.DateTime),
        ("DECODE", NetezzaFunctionCategory.Conversion),
        ("DIGITS", NetezzaFunctionCategory.String),
        ("FLOOR", NetezzaFunctionCategory.Numeric),
        ("GREATEST", NetezzaFunctionCategory.Numeric),
        ("HEX", NetezzaFunctionCategory.String),
        ("IFNULL", NetezzaFunctionCategory.Conversion),
        ("INSTR", NetezzaFunctionCategory.String),
        ("ISNULL", NetezzaFunctionCategory.Conversion),
        ("JSON_VALUE", NetezzaFunctionCategory.System),
        ("LAG", NetezzaFunctionCategory.Window),
        ("LAST_DAY", NetezzaFunctionCategory.DateTime),
        ("LAST_VALUE", NetezzaFunctionCategory.Window),
        ("LEAD", NetezzaFunctionCategory.Window),
        ("LEAST", NetezzaFunctionCategory.Numeric),
        ("LENGTH", NetezzaFunctionCategory.String),
        ("LISTAGG", NetezzaFunctionCategory.Aggregate),
        ("LOCATE", NetezzaFunctionCategory.String),
        ("LTRIM", NetezzaFunctionCategory.String),
        ("MAX", NetezzaFunctionCategory.Aggregate),
        ("MEDIAN", NetezzaFunctionCategory.Aggregate),
        ("MIN", NetezzaFunctionCategory.Aggregate),
        ("MOD", NetezzaFunctionCategory.Numeric),
        ("MONTHNAME", NetezzaFunctionCategory.DateTime),
        ("NEXT_DAY", NetezzaFunctionCategory.DateTime),
        ("NTH_VALUE", NetezzaFunctionCategory.Window),
        ("NTILE", NetezzaFunctionCategory.Window),
        ("NULLIF", NetezzaFunctionCategory.Conversion),
        ("NVL", NetezzaFunctionCategory.Conversion),
        ("PERCENTILE_CONT", NetezzaFunctionCategory.Aggregate),
        ("PERCENTILE_DISC", NetezzaFunctionCategory.Aggregate),
        ("POSSTR", NetezzaFunctionCategory.String),
        ("POWER", NetezzaFunctionCategory.Numeric),
        ("RAND", NetezzaFunctionCategory.Numeric),
        ("RANK", NetezzaFunctionCategory.Window),
        ("REGEXP_COUNT", NetezzaFunctionCategory.String),
        ("REGEXP_EXTRACT", NetezzaFunctionCategory.String),
        ("REGEXP_INSTR", NetezzaFunctionCategory.String),
        ("REGEXP_LIKE", NetezzaFunctionCategory.String),
        ("REGEXP_REPLACE", NetezzaFunctionCategory.String),
        ("REGEXP_SUBSTR", NetezzaFunctionCategory.String),
        ("ROUND", NetezzaFunctionCategory.Numeric),
        ("ROW_NUMBER", NetezzaFunctionCategory.Window),
        ("RTRIM", NetezzaFunctionCategory.String),
        ("SQRT", NetezzaFunctionCategory.Numeric),
        ("STRPOS", NetezzaFunctionCategory.String),
        ("SUBSTR", NetezzaFunctionCategory.String),
        ("SUM", NetezzaFunctionCategory.Aggregate),
        ("TIMESTAMP_FORMAT", NetezzaFunctionCategory.DateTime),
        ("TO_CLOB", NetezzaFunctionCategory.Conversion),
        ("TRIM", NetezzaFunctionCategory.String),
        ("UNNEST", NetezzaFunctionCategory.System),
        ("VALUE", NetezzaFunctionCategory.Conversion),
        ("VARCHAR", NetezzaFunctionCategory.Conversion),
        ("VARCHAR_FORMAT", NetezzaFunctionCategory.String),
    ];

    private static readonly Dictionary<string, string> Db2SignatureLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["COALESCE"] = "COALESCE(value, fallback)",
        ["NVL"] = "NVL(value, fallback)",
        ["SUBSTR"] = "SUBSTR(value, start, length)",
        ["TRIM"] = "TRIM(value)",
        ["LISTAGG"] = "LISTAGG(expression, separator)",
        ["VARCHAR_FORMAT"] = "VARCHAR_FORMAT(timestamp, format)",
        ["TIMESTAMP_FORMAT"] = "TIMESTAMP_FORMAT(string, format)",
        ["LOCATE"] = "LOCATE(search, string, start?)",
        ["POSSTR"] = "POSSTR(source, search)",
        ["REGEXP_REPLACE"] = "REGEXP_REPLACE(source, pattern, replacement)",
        ["REGEXP_LIKE"] = "REGEXP_LIKE(source, pattern)",
        ["COUNT"] = "COUNT(expression)",
        ["CONCAT"] = "CONCAT(left, right)",
        ["VARCHAR"] = "VARCHAR(expression, length?)",
    };

    private static IReadOnlyList<NetezzaBuiltinFunction> Db2BuiltinFunctions { get; } =
        Db2FunctionSpecs
            .Select(spec => Function(
                spec.Name,
                spec.Category,
                Signature(
                    Db2SignatureLabels.TryGetValue(spec.Name, out var label) ? label : $"{spec.Name}(...)",
                    "Db2 LUW built-in function.",
                    [])))
            .ToArray();

    /// <summary>
    /// Base-list names Db2 rejects or that have a different Db2 spelling. Kept
    /// separate from the positive list so shared completion contexts that add
    /// Netezza keywords cannot leak them into Db2 documents.
    /// </summary>
    public IReadOnlyList<string> DeniedCompletionKeywords { get; } =
    [
        // Live-verified Db2 LUW deny-list (mirrors sql-lang-db2 in the Rust edition)
        // plus Netezza clauses/keywords that the shared SqlContext still adds.
        "LIMIT", "ILIKE", "ELSIF", "VERBOSE", "TEMP", "TEMPORARY", "EXEC", "LAST",
        "GROOM", "GENERATE", "DISTRIBUTE", "DISTRIBUTE ON", "ORGANIZE", "ORGANIZE ON",
        "REFTABLE", "VARARGS", "NZPLSQL", "RECLAIM", "BACKUPSET", "EXPRESS",
        "PLANTEXT", "PLANGRAPH", "DISTRIBUTION", "STATISTICS", "SYNONYM",
        "SHOW", "BEGIN_PROC", "END_PROC", "NULLS", "SET PRIVILEGES TO",
        "MODIFY COLUMN", "RENAME TO", "BYTEINT", "VARBYTE", "TIMESTAMPTZ",
        "BPCHAR", "TEXT", "FLOAT4", "FLOAT8",
    ];

    public IReadOnlyList<string> DeniedFunctionNames { get; } =
    [
        "SPLIT_PART", "JUSTIFY_DAYS", "JUSTIFY_HOURS", "JUSTIFY_INTERVAL", "ISFINITE",
        "REVERSE", "OCTET_LENGTH", "PI", "ASCII", "CHR",
    ];

    public static Db2SqlCatalog Instance { get; } = new();

    public IReadOnlyList<NetezzaBuiltinFunction> BuiltinFunctions { get; } =
        SqlAuthoringCatalogComposer.MergeFunctions(
            AnsiSqlCatalog.BuiltinFunctions,
            Db2BuiltinFunctions);

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

    public IReadOnlyList<NetezzaDataTypeSpec> DataTypes { get; } =
        SqlAuthoringCatalogComposer.MergeTypes(AnsiSqlCatalog.DataTypes, Db2DataTypes);

    public IReadOnlyList<string> DataTypeNames { get; } =
        SqlAuthoringCatalogComposer.MergeTypes(AnsiSqlCatalog.DataTypes, Db2DataTypes)
            .SelectMany(t => t.Aliases).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public IReadOnlyList<string> CompletionKeywords { get; } =
        SqlAuthoringCatalogComposer.MergeValues(AnsiSqlCatalog.CompletionKeywords, [
        "SELECT", "FROM", "WHERE", "INSERT", "UPDATE", "DELETE", "MERGE", "CALL",
        "CREATE", "ALTER", "DROP", "TABLE", "VIEW", "PROCEDURE", "FUNCTION", "SEQUENCE",
        "TRIGGER", "ALIAS", "INDEX", "VALUES", "IDENTITY", "GENERATED", "ALWAYS",
        "BY DEFAULT", "FETCH FIRST", "OPTIMIZE FOR", "FOR READ ONLY", "FOR UPDATE",
        "WITH UR", "WITH CS", "WITH RS", "WITH RR", "FINAL TABLE",
        "DECLARE GLOBAL TEMPORARY", "ORGANIZE BY", "DATA CAPTURE", "LANGUAGE SQL",
        "ORDER BY", "GROUP BY", "HAVING", "UNION", "INTERSECT", "EXCEPT",
        "CURRENT SCHEMA", "CURRENT SERVER", "CURRENT DATE", "CURRENT TIME",
        "CURRENT TIMESTAMP", "CURRENT USER", "NICKNAME",
        "ALIAS", "CREATE ALIAS", "MATERIALIZED QUERY TABLE", "RUNSTATS", "RUNSTATS ON TABLE",
        "REORG", "REORG TABLE", "SET INTEGRITY", "ADMIN_CMD", "INPLACE",
        "ALLOW NO ACCESS", "ALLOW READ ACCESS", "ALLOW WRITE ACCESS",
    ]);

    public IReadOnlyList<string> Keywords { get; } =
        SqlAuthoringCatalogComposer.MergeValues(AnsiSqlCatalog.Keywords, [
        "SELECT", "FROM", "WHERE", "GROUP", "BY", "ORDER", "FETCH", "FIRST",
        "NEXT", "ROWS", "ROW", "ONLY", "INSERT", "UPDATE", "DELETE", "MERGE",
        "INTO", "VALUES", "SET", "WITH", "JOIN", "INNER", "LEFT", "RIGHT", "FULL",
        "OUTER", "CROSS", "ON", "AND", "OR", "NOT", "NULL", "AS", "CREATE", "ALTER",
        "DROP", "TABLE", "VIEW", "PROCEDURE", "FUNCTION", "ALIAS", "NICKNAME",
        "OPTIMIZE", "FOR", "READ", "UR", "CS", "RS", "RR", "FINAL", "DECLARE",
        "GLOBAL", "TEMPORARY", "LANGUAGE", "SQL", "IDENTITY", "GENERATED",
    ]);

    public SqlFormatterProfile FormatterProfile { get; } =
        SqlAuthoringCatalogComposer.MergeFormatterProfiles(
            AnsiSqlCatalog.FormatterProfile,
            new SqlFormatterProfile(["OPTIMIZE FOR", "FOR READ ONLY", "FOR UPDATE", "WITH UR", "WITH CS", "WITH RS", "WITH RR"]));

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
