namespace JustyBase.NetezzaSqlParser.Authoring;

/// <summary>
/// Formatter metadata shared by the ANSI-derived dialect profiles.
/// Multi-word phrases are stored as one value so completion and hover do not
/// accidentally split clauses such as <c>PARTITION BY</c>.
/// </summary>
public sealed record SqlFormatterProfile(IReadOnlyList<string> ClauseKeywords);

/// <summary>
/// Shared ANSI authoring profile. Dialects compose this profile with a small
/// overlay instead of copying common completion and signature metadata.
/// </summary>
public static class AnsiSqlCatalog
{
    private static SqlSignatureParameterInfo Parameter(string label, string documentation)
        => new(label, documentation);

    private static NetezzaFunctionSignature Signature(
        string label,
        string documentation,
        params SqlSignatureParameterInfo[] parameters)
        => new(label, documentation, parameters);

    private static NetezzaFunctionSignature VariadicSignature(
        string label,
        string documentation,
        params SqlSignatureParameterInfo[] parameters)
        => new(label, documentation, parameters, Variadic: true);

    private static NetezzaBuiltinFunction Function(
        string name,
        NetezzaFunctionCategory category,
        params NetezzaFunctionSignature[] signatures)
        => new(name, category, signatures);

    public static IReadOnlyList<NetezzaBuiltinFunction> BuiltinFunctions { get; } =
    [
        Function("ABS", NetezzaFunctionCategory.Numeric,
            Signature("ABS(value)", "Absolute value.", Parameter("value", "Numeric expression."))),
        Function("AVG", NetezzaFunctionCategory.Aggregate,
            Signature("AVG(expression)", "Average of non-NULL values.", Parameter("expression", "Numeric expression."))),
        Function("CAST", NetezzaFunctionCategory.Conversion,
            Signature("CAST(expression AS type)", "Converts an expression to a data type.",
                Parameter("expression", "Expression to convert."), Parameter("type", "Target data type."))),
        Function("COALESCE", NetezzaFunctionCategory.Conversion,
            VariadicSignature("COALESCE(value1, value2, ...)", "Returns the first non-NULL value.",
                Parameter("value1", "First value."), Parameter("value2", "Fallback value."))),
        Function("COUNT", NetezzaFunctionCategory.Aggregate,
            Signature("COUNT(expression)", "Counts non-NULL values.", Parameter("expression", "Expression or *."))),
        Function("CURRENT_DATE", NetezzaFunctionCategory.DateTime,
            Signature("CURRENT_DATE", "Current database date.")),
        Function("CURRENT_TIME", NetezzaFunctionCategory.DateTime,
            Signature("CURRENT_TIME", "Current database time.")),
        Function("CURRENT_TIMESTAMP", NetezzaFunctionCategory.DateTime,
            Signature("CURRENT_TIMESTAMP", "Current database timestamp.")),
        Function("EXTRACT", NetezzaFunctionCategory.DateTime,
            Signature("EXTRACT(field FROM source)", "Extracts a date/time field.",
                Parameter("field", "Date/time field."), Parameter("source", "Date/time expression."))),
        Function("FLOOR", NetezzaFunctionCategory.Numeric,
            Signature("FLOOR(value)", "Rounds a number downward.", Parameter("value", "Numeric expression."))),
        Function("GREATEST", NetezzaFunctionCategory.Numeric,
            VariadicSignature("GREATEST(value1, value2, ...)", "Returns the greatest value.",
                Parameter("value1", "First value."), Parameter("value2", "Additional value."))),
        Function("LEAST", NetezzaFunctionCategory.Numeric,
            VariadicSignature("LEAST(value1, value2, ...)", "Returns the least value.",
                Parameter("value1", "First value."), Parameter("value2", "Additional value."))),
        Function("LENGTH", NetezzaFunctionCategory.String,
            Signature("LENGTH(string)", "Returns string length.", Parameter("string", "Input string."))),
        Function("LOWER", NetezzaFunctionCategory.String,
            Signature("LOWER(string)", "Converts a string to lowercase.", Parameter("string", "Input string."))),
        Function("MAX", NetezzaFunctionCategory.Aggregate,
            Signature("MAX(expression)", "Returns the maximum value.", Parameter("expression", "Expression."))),
        Function("MIN", NetezzaFunctionCategory.Aggregate,
            Signature("MIN(expression)", "Returns the minimum value.", Parameter("expression", "Expression."))),
        Function("MOD", NetezzaFunctionCategory.Numeric,
            Signature("MOD(value, divisor)", "Returns a remainder.",
                Parameter("value", "Dividend."), Parameter("divisor", "Divisor."))),
        Function("NULLIF", NetezzaFunctionCategory.Conversion,
            Signature("NULLIF(value1, value2)", "Returns NULL when values are equal.",
                Parameter("value1", "First value."), Parameter("value2", "Second value."))),
        Function("POWER", NetezzaFunctionCategory.Numeric,
            Signature("POWER(base, exponent)", "Raises a value to a power.",
                Parameter("base", "Base value."), Parameter("exponent", "Exponent."))),
        Function("REPLACE", NetezzaFunctionCategory.String,
            Signature("REPLACE(string, search, replacement)", "Replaces a substring.",
                Parameter("string", "Input string."), Parameter("search", "Search string."),
                Parameter("replacement", "Replacement string."))),
        Function("ROUND", NetezzaFunctionCategory.Numeric,
            Signature("ROUND(value [, scale])", "Rounds a number.",
                Parameter("value", "Numeric expression."), Parameter("scale", "Optional decimal scale."))),
        Function("ROW_NUMBER", NetezzaFunctionCategory.Window,
            Signature("ROW_NUMBER() OVER (...)", "Numbers rows in a window.")),
        Function("SQRT", NetezzaFunctionCategory.Numeric,
            Signature("SQRT(value)", "Returns a square root.", Parameter("value", "Non-negative numeric expression."))),
        Function("SUBSTRING", NetezzaFunctionCategory.String,
            Signature("SUBSTRING(string, start [, length])", "Extracts a substring.",
                Parameter("string", "Input string."), Parameter("start", "Start position."),
                Parameter("length", "Optional length."))),
        Function("SUM", NetezzaFunctionCategory.Aggregate,
            Signature("SUM(expression)", "Returns the sum of values.", Parameter("expression", "Numeric expression."))),
        Function("TRIM", NetezzaFunctionCategory.String,
            Signature("TRIM(string)", "Removes surrounding spaces.", Parameter("string", "Input string."))),
        Function("UPPER", NetezzaFunctionCategory.String,
            Signature("UPPER(string)", "Converts a string to uppercase.", Parameter("string", "Input string."))),
    ];

    // Keep this base deliberately small. Dialects retain their own complete
    // type sets and can override a common type without leaking another
    // dialect's aliases (for example Oracle VARCHAR2 versus Db2 VARCHAR).
    public static IReadOnlyList<NetezzaDataTypeSpec> DataTypes { get; } =
    [
        new("BOOLEAN", ["BOOLEAN"]),
        new("DATE", ["DATE"]),
        new("TIME", ["TIME"]),
        new("TIMESTAMP", ["TIMESTAMP"], 0, 1),
        new("CLOB", ["CLOB"]),
        new("BLOB", ["BLOB"]),
    ];

    public static IReadOnlyList<string> CompletionKeywords { get; } =
    [
        "SELECT", "FROM", "WHERE", "INSERT", "INTO", "UPDATE", "DELETE", "MERGE",
        "VALUES", "SET", "CREATE", "ALTER", "DROP", "TABLE", "VIEW", "JOIN",
        "INNER JOIN", "LEFT JOIN", "RIGHT JOIN", "FULL JOIN", "CROSS JOIN", "ON",
        "GROUP BY", "HAVING", "ORDER BY", "PARTITION BY", "UNION", "INTERSECT",
        "EXCEPT", "WITH", "FETCH FIRST", "FETCH NEXT", "OFFSET", "ROW", "ROWS", "ONLY",
    ];

    public static IReadOnlyList<string> Keywords { get; } =
    [
        "SELECT", "FROM", "WHERE", "GROUP", "BY", "ORDER", "PARTITION", "FETCH", "FIRST",
        "NEXT", "OFFSET", "ROWS", "ROW", "ONLY", "INSERT", "UPDATE", "DELETE", "MERGE",
        "INTO", "VALUES", "SET", "WITH", "JOIN", "INNER", "LEFT", "RIGHT", "FULL",
        "OUTER", "CROSS", "ON", "AND", "OR", "NOT", "NULL", "AS", "CASE", "WHEN",
        "THEN", "ELSE", "END", "UNION", "INTERSECT", "EXCEPT", "CURRENT_DATE",
        "CURRENT_TIME", "CURRENT_TIMESTAMP",
    ];

    public static SqlFormatterProfile FormatterProfile { get; } = new(
    ["GROUP BY", "ORDER BY", "PARTITION BY", "OFFSET", "FETCH FIRST", "FETCH NEXT"]);
}

/// <summary>Deterministic, case-insensitive merge helpers for authoring profiles.</summary>
public static class SqlAuthoringCatalogComposer
{
    public static IReadOnlyList<NetezzaBuiltinFunction> MergeFunctions(
        params IEnumerable<NetezzaBuiltinFunction>[] profiles)
    {
        var result = new List<NetezzaBuiltinFunction>();
        var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var profile in profiles)
        {
            foreach (var function in profile)
            {
                if (!byName.TryGetValue(function.Name, out var index))
                {
                    byName[function.Name] = result.Count;
                    result.Add(function);
                    continue;
                }

                var existing = result[index];
                var signatures = existing.Signatures
                    .Concat(function.Signatures)
                    .GroupBy(signature => signature.Label, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.Last())
                    .ToArray();
                result[index] = existing with
                {
                    Category = function.Category,
                    Signatures = signatures,
                };
            }
        }

        return result;
    }

    public static IReadOnlyList<NetezzaDataTypeSpec> MergeTypes(
        params IEnumerable<NetezzaDataTypeSpec>[] profiles)
    {
        var result = new List<NetezzaDataTypeSpec>();
        var byCanonicalName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var profile in profiles)
        {
            foreach (var type in profile)
            {
                if (byCanonicalName.TryGetValue(type.CanonicalName, out var index))
                {
                    result[index] = type;
                    continue;
                }

                byCanonicalName[type.CanonicalName] = result.Count;
                result.Add(type);
            }
        }

        return result;
    }

    public static IReadOnlyList<string> MergeValues(
        params IEnumerable<string>[] profiles)
        => profiles.SelectMany(profile => profile)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static SqlFormatterProfile MergeFormatterProfiles(
        params SqlFormatterProfile[] profiles)
        => new(MergeValues(profiles.SelectMany(profile => profile.ClauseKeywords)));
}
