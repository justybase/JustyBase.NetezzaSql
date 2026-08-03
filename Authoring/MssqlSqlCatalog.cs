namespace JustyBase.NetezzaSqlParser.Authoring;

/// <summary>
/// Microsoft SQL Server (T-SQL) SQL authoring catalog. Port of
/// extensions/mssql/src/sql/authoring.ts (completion keywords, type specs,
/// function signatures) from the reference TypeScript project.
/// </summary>
public sealed class MssqlSqlCatalog : ISqlAuthoringCatalog
{
    private static IReadOnlyList<NetezzaDataTypeSpec> MssqlDataTypes { get; } =
    [
        new("TINYINT", ["TINYINT"]),
        new("SMALLINT", ["SMALLINT"]),
        new("INT", ["INT"]),
        new("BIGINT", ["BIGINT"]),
        new("NUMERIC", ["NUMERIC"], 1, 2),
        new("DECIMAL", ["DECIMAL"], 1, 2),
        new("REAL", ["REAL"]),
        new("FLOAT", ["FLOAT"], 0, 1),
        new("BIT", ["BIT"]),
        new("CHAR", ["CHAR"], 1, 1, true),
        new("VARCHAR", ["VARCHAR"], 1, 1, true),
        new("NCHAR", ["NCHAR"], 1, 1, true),
        new("NVARCHAR", ["NVARCHAR"], 1, 1, true),
        new("TEXT", ["TEXT"]),
        new("NTEXT", ["NTEXT"]),
        new("DATE", ["DATE"]),
        new("TIME", ["TIME"], 0, 1),
        new("DATETIME", ["DATETIME"]),
        new("SMALLDATETIME", ["SMALLDATETIME"]),
        new("DATETIME2", ["DATETIME2"], 0, 1),
        new("DATETIMEOFFSET", ["DATETIMEOFFSET"], 0, 1),
        new("MONEY", ["MONEY"]),
        new("SMALLMONEY", ["SMALLMONEY"]),
        new("BINARY", ["BINARY"], 1, 1),
        new("VARBINARY", ["VARBINARY"], 1, 1, true),
        new("IMAGE", ["IMAGE"]),
        new("XML", ["XML"]),
        new("UNIQUEIDENTIFIER", ["UNIQUEIDENTIFIER"]),
        new("SQL_VARIANT", ["SQL_VARIANT"]),
    ];

    private MssqlSqlCatalog()
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

    private static IReadOnlyList<NetezzaBuiltinFunction> MssqlBuiltinFunctions { get; } =
    [
        Function("COUNT", NetezzaFunctionCategory.Aggregate,
            Signature("COUNT(expression)", "Returns the number of items found in a group.", Parameter("expression", "Expression or *."))),
        Function("ISNULL", NetezzaFunctionCategory.Conversion,
            Signature("ISNULL(check_expression, replacement_value)", "Replaces NULL with the specified replacement value.", Parameter("check_expression", "Expression to check."), Parameter("replacement_value", "Replacement value."))),
        Function("GETDATE", NetezzaFunctionCategory.DateTime,
            Signature("GETDATE()", "Returns the current database system timestamp.")),
        Function("STRING_AGG", NetezzaFunctionCategory.Aggregate,
            Signature("STRING_AGG(expression, separator)", "Concatenates string expressions and places separator values between them.", Parameter("expression", "Expression to concatenate."), Parameter("separator", "Separator value."))),
        Function("SYSDATETIME", NetezzaFunctionCategory.DateTime,
            Signature("SYSDATETIME()", "Returns the current database system timestamp as datetime2.")),
    ];

    public static MssqlSqlCatalog Instance { get; } = new();

    public IReadOnlyList<NetezzaBuiltinFunction> BuiltinFunctions { get; } =
        SqlAuthoringCatalogComposer.MergeFunctions(
            AnsiSqlCatalog.BuiltinFunctions,
            MssqlBuiltinFunctions);

    public IReadOnlyList<NetezzaBuiltinFunction> ValidationBuiltinFunctions { get; } =
        new[]
        {
            "ABS", "AVG", "CAST", "CHARINDEX", "CHOOSE", "COALESCE", "CONVERT",
            "COUNT", "DATALENGTH", "DATEADD", "DATEDIFF", "FLOOR", "FORMAT",
            "GETDATE", "IIF", "ISNULL", "LEN", "LOWER", "MAX", "MIN", "NEWID",
            "NULLIF", "REPLACE", "ROUND", "STRING_AGG", "STUFF", "SUBSTRING",
            "SUM", "SYSDATETIME", "TRIM", "UPPER",
        }.Select(name => new NetezzaBuiltinFunction(name, NetezzaFunctionCategory.System,
            [new NetezzaFunctionSignature($"{name}(...)", "SQL Server built-in function.", [])])).ToArray();

    public IReadOnlyList<NetezzaDataTypeSpec> DataTypes { get; } =
        SqlAuthoringCatalogComposer.MergeTypes(AnsiSqlCatalog.DataTypes, MssqlDataTypes);

    public IReadOnlyList<string> DataTypeNames { get; } =
        SqlAuthoringCatalogComposer.MergeTypes(AnsiSqlCatalog.DataTypes, MssqlDataTypes)
            .SelectMany(t => t.Aliases).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public IReadOnlyList<string> CompletionKeywords { get; } =
        SqlAuthoringCatalogComposer.MergeValues(AnsiSqlCatalog.CompletionKeywords, [
        "SELECT", "FROM", "WHERE", "INSERT", "UPDATE", "DELETE", "MERGE", "INTO",
        "CREATE", "ALTER", "DROP", "TABLE", "VIEW", "OUTPUT", "INDEX", "FUNCTION",
        "TRIGGER", "HAVING", "TOP", "FETCH NEXT", "CROSS APPLY", "OUTER APPLY",
        "BEGIN TRY", "BEGIN CATCH", "GO", "ORDER BY", "GROUP BY",
    ]);

    public IReadOnlyList<string> Keywords { get; } =
        SqlAuthoringCatalogComposer.MergeValues(AnsiSqlCatalog.Keywords, [
        "SELECT", "FROM", "WHERE", "GROUP", "BY", "ORDER", "TOP", "OUTPUT", "APPLY",
        "CROSS", "OUTER", "GO", "TRY", "CATCH", "PROC", "PROCEDURE", "FETCH",
        "NEXT", "ROWS", "ROW", "ONLY", "INSERT", "UPDATE", "DELETE", "MERGE",
        "INTO", "VALUES", "SET", "IDENTITY", "RECOMPILE", "ENCRYPTION", "EXEC",
        "DECLARE", "BEGIN", "END",
    ]);

    public SqlFormatterProfile FormatterProfile { get; } =
        SqlAuthoringCatalogComposer.MergeFormatterProfiles(
            AnsiSqlCatalog.FormatterProfile,
            new SqlFormatterProfile(["OUTPUT", "CROSS APPLY", "OUTER APPLY"]));

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
