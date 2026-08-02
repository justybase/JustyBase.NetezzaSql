namespace JustyBase.NetezzaSqlParser.Authoring;

/// <summary>
/// Oracle SQL authoring catalog. Port of
/// extensions/oracle/src/sql/authoring.ts (completion keywords, type specs,
/// function signatures) from the reference TypeScript project.
/// </summary>
public sealed class OracleSqlCatalog : ISqlAuthoringCatalog
{
    // Declared before Instance: the static cctor initializes fields in
    // declaration order, and the instance initializers reference this list.
    private static IReadOnlyList<NetezzaDataTypeSpec> OracleDataTypes { get; } =
    [
        new("NUMBER", ["NUMBER"], 0, 2),
        new("FLOAT", ["FLOAT"], 0, 1),
        new("BINARY_FLOAT", ["BINARY_FLOAT"]),
        new("BINARY_DOUBLE", ["BINARY_DOUBLE"]),
        new("CHAR", ["CHAR"], 1, 1, true),
        new("NCHAR", ["NCHAR"], 1, 1, true),
        new("VARCHAR2", ["VARCHAR2"], 1, 1, true),
        new("NVARCHAR2", ["NVARCHAR2"], 1, 1, true),
        new("RAW", ["RAW"], 1, 1, true),
        new("DATE", ["DATE"]),
        new("TIMESTAMP", ["TIMESTAMP"], 0, 1),
        new("TIMESTAMP WITH TIME ZONE", ["TIMESTAMP WITH TIME ZONE"], 0, 1),
        new("TIMESTAMP WITH LOCAL TIME ZONE", ["TIMESTAMP WITH LOCAL TIME ZONE"], 0, 1),
        new("CLOB", ["CLOB"]),
        new("NCLOB", ["NCLOB"]),
        new("BLOB", ["BLOB"]),
        new("LONG", ["LONG"]),
        new("LONG RAW", ["LONG RAW"]),
        new("ROWID", ["ROWID"]),
        new("UROWID", ["UROWID"], 0, 1),
        new("BOOLEAN", ["BOOLEAN"]),
        new("XMLTYPE", ["XMLTYPE"]),
        new("JSON", ["JSON"]),
        new("INTERVAL YEAR TO MONTH", ["INTERVAL YEAR TO MONTH"], 1, 1),
        new("INTERVAL DAY TO SECOND", ["INTERVAL DAY TO SECOND"], 1, 1),
    ];

    private OracleSqlCatalog()
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

    private static IReadOnlyList<NetezzaBuiltinFunction> OracleBuiltinFunctions { get; } =
    [
        Function("COUNT", NetezzaFunctionCategory.Aggregate,
            Signature("COUNT(expression)", "Returns the number of non-null values for the expression.", Parameter("expression", "Expression or *."))),
        Function("NVL", NetezzaFunctionCategory.Conversion,
            Signature("NVL(value, fallback)", "Returns the fallback when the value is null.", Parameter("value", "Value to test."), Parameter("fallback", "Fallback value."))),
        Function("COALESCE", NetezzaFunctionCategory.Conversion,
            Signature("COALESCE(value1, value2, ...)", "Returns the first non-null argument.", Parameter("value1", "First value."), Parameter("value2", "Second value."))),
        Function("TO_CHAR", NetezzaFunctionCategory.Conversion,
            Signature("TO_CHAR(value, format?)", "Converts a value to VARCHAR2 using an optional format mask.", Parameter("value", "Value to format."), Parameter("format", "Optional format mask."))),
        Function("SUBSTR", NetezzaFunctionCategory.String,
            Signature("SUBSTR(value, start, length?)", "Returns a substring starting at the given offset.", Parameter("value", "Input string."), Parameter("start", "Start offset."), Parameter("length", "Optional length."))),
        Function("SYS_CONTEXT", NetezzaFunctionCategory.System,
            Signature("SYS_CONTEXT(namespace, parameter)", "Returns the value of an Oracle application or USERENV context.", Parameter("namespace", "Context namespace."), Parameter("parameter", "Context parameter."))),
        Function("TO_DATE", NetezzaFunctionCategory.Conversion,
            Signature("TO_DATE(value, format?)", "Converts text to an Oracle DATE using an optional format mask.", Parameter("value", "Date text."), Parameter("format", "Optional format mask."))),
        Function("REGEXP_LIKE", NetezzaFunctionCategory.String,
            Signature("REGEXP_LIKE(source, pattern, match_parameter?)", "Tests whether a source value matches a regular expression.", Parameter("source", "Source value."), Parameter("pattern", "Regular expression."), Parameter("match_parameter", "Optional match parameters."))),
        Function("ADD_MONTHS", NetezzaFunctionCategory.DateTime,
            Signature("ADD_MONTHS(date, months)", "Returns a date shifted by the requested number of months.", Parameter("date", "Date expression."), Parameter("months", "Number of months."))),
    ];

    public static OracleSqlCatalog Instance { get; } = new();

    public IReadOnlyList<NetezzaBuiltinFunction> BuiltinFunctions { get; } =
        SqlAuthoringCatalogComposer.MergeFunctions(AnsiSqlCatalog.BuiltinFunctions, OracleBuiltinFunctions);

    // Validation profile: builtinFunctions from authoring.ts (Oracle).
    public IReadOnlyList<NetezzaBuiltinFunction> ValidationBuiltinFunctions { get; } =
        new[]
        {
            "ABS", "ADD_MONTHS", "AVG", "CAST", "COALESCE", "COUNT", "CURRENT_DATE",
            "CURRENT_TIMESTAMP", "DECODE", "EXTRACT", "GREATEST", "INSTR", "LAST_DAY",
            "LEAST", "DBMS_METADATA.GET_DDL", "LOWER", "MAX", "MIN", "NVL", "NVL2",
            "NULLIF", "REGEXP_LIKE", "REGEXP_REPLACE", "REGEXP_SUBSTR", "ROUND",
            "SUBSTR", "SUM", "SYSDATE", "SYSTIMESTAMP", "SYS_CONTEXT", "TO_CLOB",
            "TO_CHAR", "TO_DATE", "TO_NUMBER", "TO_TIMESTAMP", "TRUNC", "UID", "UPPER",
        }.Select(name => new NetezzaBuiltinFunction(name, NetezzaFunctionCategory.System,
            [new NetezzaFunctionSignature($"{name}(...)", "Oracle built-in function.", [])])).ToArray();

    public IReadOnlyList<NetezzaDataTypeSpec> DataTypes { get; } =
        SqlAuthoringCatalogComposer.MergeTypes(AnsiSqlCatalog.DataTypes, OracleDataTypes);

    public IReadOnlyList<string> DataTypeNames { get; } =
        SqlAuthoringCatalogComposer.MergeTypes(AnsiSqlCatalog.DataTypes, OracleDataTypes)
            .SelectMany(t => t.Aliases).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    // Completion keywords from authoring.ts.
    public IReadOnlyList<string> CompletionKeywords { get; } =
        SqlAuthoringCatalogComposer.MergeValues(AnsiSqlCatalog.CompletionKeywords, [
        "SELECT", "FROM", "WHERE", "INSERT", "UPDATE", "DELETE", "MERGE",
        "BEGIN", "DECLARE", "CALL", "CREATE", "ALTER", "DROP", "TABLE", "VIEW",
        "SEQUENCE", "PROCEDURE", "FUNCTION", "PACKAGE", "TRIGGER", "SYNONYM",
        "INDEX", "MATERIALIZED VIEW", "GRANT", "REVOKE", "COMMIT", "ROLLBACK",
        "SAVEPOINT", "RETURNING INTO", "PIVOT", "UNPIVOT", "ORDER BY",
        "GROUP BY", "CONNECT BY", "START WITH", "FETCH FIRST", "FETCH NEXT",
        "ROWNUM", "DUAL",
    ]);

    // Keywords surfaced by hover for non-identifier tokens (formatter profile
    // keyword list from authoring.ts).
    public IReadOnlyList<string> Keywords { get; } =
        SqlAuthoringCatalogComposer.MergeValues(AnsiSqlCatalog.Keywords, [
        "SELECT", "FROM", "WHERE", "GROUP", "BY", "ORDER", "FETCH", "FIRST",
        "NEXT", "ROWS", "ROW", "ONLY", "INSERT", "UPDATE", "DELETE", "MERGE",
        "INTO", "VALUES", "SET", "WITH", "CONNECT", "START", "JOIN", "INNER",
        "LEFT", "RIGHT", "FULL", "OUTER", "CROSS", "ON", "AND", "OR", "NOT",
        "NULL", "AS", "BEGIN", "DECLARE", "END", "EXCEPTION", "CREATE", "ALTER",
        "DROP", "TABLE", "VIEW", "PACKAGE", "PROCEDURE", "FUNCTION", "TRIGGER",
        "SEQUENCE", "SYNONYM", "PIVOT", "UNPIVOT", "RETURNING", "PRIOR",
        "NOCYCLE", "SIBLINGS", "GRANT", "REVOKE", "COMMIT", "ROLLBACK",
    ]);

    public SqlFormatterProfile FormatterProfile { get; } =
        SqlAuthoringCatalogComposer.MergeFormatterProfiles(
            AnsiSqlCatalog.FormatterProfile,
            new SqlFormatterProfile(["CONNECT BY", "START WITH", "ORDER SIBLINGS BY"]));

    public bool TryGetFunction(string name, out NetezzaBuiltinFunction function)
    {
        function = BuiltinFunctions.FirstOrDefault(f =>
            string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))!;
        return function is not null;
    }

    public bool TryGetDataType(string name, out NetezzaDataTypeSpec type)
    {
        type = DataTypes.FirstOrDefault(t =>
            t.Aliases.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase)))!;
        return type is not null;
    }
}
