namespace JustyBase.NetezzaSqlParser.Authoring;

/// <summary>Logical group used to present Netezza functions to SQL tooling.</summary>
public enum NetezzaFunctionCategory
{
    Aggregate,
    Window,
    DateTime,
    Numeric,
    String,
    Conversion,
    System,
    NetezzaSpecific
}

/// <summary>One overload of a Netezza built-in function.</summary>
public sealed record NetezzaFunctionSignature(
    string Label,
    string? Documentation,
    IReadOnlyList<SqlSignatureParameterInfo> Parameters,
    bool Variadic = false);

/// <summary>Metadata shared by completion, hover, signature help and validation.</summary>
public sealed record NetezzaBuiltinFunction(
    string Name,
    NetezzaFunctionCategory Category,
    IReadOnlyList<NetezzaFunctionSignature> Signatures);

/// <summary>Accepted Netezza type name and its optional parameter shape.</summary>
public sealed record NetezzaDataTypeSpec(
    string CanonicalName,
    IReadOnlyList<string> Aliases,
    int MinParameters = 0,
    int MaxParameters = 0,
    bool WarnWhenLengthIsMissing = false);

/// <summary>
/// Central Netezza SQL authoring catalog. The catalog intentionally contains
/// metadata only; it does not change parser acceptance rules.
/// </summary>
public static class NetezzaSqlCatalog
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

    private static IReadOnlyList<NetezzaBuiltinFunction> CoreBuiltinFunctions { get; } =
    [
        Function("ABS", NetezzaFunctionCategory.Numeric, Signature("ABS(value)", "Absolute value.", Parameter("value", "Numeric expression."))),
        Function("ADD_MONTHS", NetezzaFunctionCategory.DateTime, Signature("ADD_MONTHS(date, months)", "Adds months to a date.", Parameter("date", "Date expression."), Parameter("months", "Number of months."))),
        Function("AVG", NetezzaFunctionCategory.Aggregate, Signature("AVG(expression)", "Average of non-NULL values.", Parameter("expression", "Numeric expression."))),
        Function("BTRIM", NetezzaFunctionCategory.String, Signature("BTRIM(string [, trim])", "Trims characters from both ends of a string.", Parameter("string", "Input string."), Parameter("trim", "Optional trim characters."))),
        Function("CEIL", NetezzaFunctionCategory.Numeric, Signature("CEIL(value)", "Rounds a number upward.", Parameter("value", "Numeric expression."))),
        Function("CEILING", NetezzaFunctionCategory.Numeric, Signature("CEILING(value)", "Rounds a number upward.", Parameter("value", "Numeric expression."))),
        Function("CAST", NetezzaFunctionCategory.Conversion, Signature("CAST(expression AS type)", "Converts an expression to a data type.", Parameter("expression", "Expression to convert."), Parameter("type", "Target data type."))),
        Function("COALESCE", NetezzaFunctionCategory.Conversion, Signature("COALESCE(value1, value2, ...)", "Returns the first non-NULL value.", Parameter("value1", "First value."), Parameter("value2", "Fallback value.")),
            VariadicSignature("COALESCE(value1, value2, ...)", "Accepts two or more values.", Parameter("value1", "First value."), Parameter("value2", "Fallback value."))),
        Function("CONCAT", NetezzaFunctionCategory.String, VariadicSignature("CONCAT(value1, value2, ...)", "Concatenates string values.", Parameter("value1", "First value."), Parameter("value2", "Additional value."))),
        Function("COUNT", NetezzaFunctionCategory.Aggregate, Signature("COUNT(expression)", "Counts non-NULL values.", Parameter("expression", "Expression or *."))),
        Function("CURRENT_DATE", NetezzaFunctionCategory.DateTime, Signature("CURRENT_DATE", "Current database date.")),
        Function("CURRENT_TIME", NetezzaFunctionCategory.DateTime, Signature("CURRENT_TIME", "Current database time.")),
        Function("CURRENT_TIMESTAMP", NetezzaFunctionCategory.DateTime, Signature("CURRENT_TIMESTAMP", "Current database timestamp.")),
        Function("DATE_PART", NetezzaFunctionCategory.DateTime, Signature("DATE_PART(part, date)", "Extracts a date/time part.", Parameter("part", "Date part name."), Parameter("date", "Date expression."))),
        Function("DATE_TRUNC", NetezzaFunctionCategory.DateTime, Signature("DATE_TRUNC(precision, date)", "Truncates a date/time value.", Parameter("precision", "Truncation precision."), Parameter("date", "Date expression."))),
        Function("DAY", NetezzaFunctionCategory.DateTime, Signature("DAY(date)", "Returns the day of month.", Parameter("date", "Date expression."))),
        Function("DAYS_BETWEEN", NetezzaFunctionCategory.DateTime, Signature("DAYS_BETWEEN(date1, date2)", "Returns the number of days between dates.", Parameter("date1", "First date."), Parameter("date2", "Second date."))),
        Function("DECODE", NetezzaFunctionCategory.Conversion, VariadicSignature("DECODE(expression, search, result, ... [, default])", "Returns the result for the first matching search value.", Parameter("expression", "Expression to compare."), Parameter("search", "Search value."), Parameter("result", "Matching result."))),
        Function("DENSE_RANK", NetezzaFunctionCategory.Window, Signature("DENSE_RANK() OVER (...)", "Ranks rows without gaps.")),
        Function("EXTRACT", NetezzaFunctionCategory.DateTime, Signature("EXTRACT(field FROM source)", "Extracts a date/time field.", Parameter("field", "Year, month, day, hour, minute or second."), Parameter("source", "Date/time expression."))),
        Function("FIRST_VALUE", NetezzaFunctionCategory.Window, Signature("FIRST_VALUE(expression) OVER (...)", "Returns the first value in a window.", Parameter("expression", "Window expression."))),
        Function("FLOOR", NetezzaFunctionCategory.Numeric, Signature("FLOOR(value)", "Rounds a number downward.", Parameter("value", "Numeric expression."))),
        Function("GROUP_CONCAT", NetezzaFunctionCategory.Aggregate,
            Signature("GROUP_CONCAT(expression)", "Concatenates values in a group.", Parameter("expression", "Expression to concatenate.")),
            Signature("GROUP_CONCAT(expression SEPARATOR delimiter)", "Concatenates values with a custom separator.", Parameter("expression", "Expression to concatenate."), Parameter("delimiter", "Separator."))),
        Function("GROUP_CONCAT_SORT", NetezzaFunctionCategory.Aggregate, Signature("GROUP_CONCAT_SORT(expression [SEPARATOR delimiter])", "Concatenates sorted group values.", Parameter("expression", "Expression to concatenate."), Parameter("delimiter", "Optional separator."))),
        Function("GREATEST", NetezzaFunctionCategory.Numeric, VariadicSignature("GREATEST(value1, value2, ...)", "Returns the greatest value.", Parameter("value1", "First value."), Parameter("value2", "Additional value."))),
        Function("HASH", NetezzaFunctionCategory.NetezzaSpecific, Signature("HASH(expression)", "Returns the Netezza hash value.", Parameter("expression", "Expression to hash."))),
        Function("HASH4", NetezzaFunctionCategory.NetezzaSpecific, Signature("HASH4(expression)", "Returns a 32-bit Netezza hash.", Parameter("expression", "Expression to hash."))),
        Function("HASH8", NetezzaFunctionCategory.NetezzaSpecific, Signature("HASH8(expression)", "Returns a 64-bit Netezza hash.", Parameter("expression", "Expression to hash."))),
        Function("INSTR", NetezzaFunctionCategory.String, Signature("INSTR(string, substring [, position [, occurrence]])", "Returns the position of a substring.", Parameter("string", "Input string."), Parameter("substring", "Search string."), Parameter("position", "Optional start position."), Parameter("occurrence", "Optional occurrence."))),
        Function("LAG", NetezzaFunctionCategory.Window, Signature("LAG(expression [, offset [, default]]) OVER (...)", "Returns a preceding row value.", Parameter("expression", "Window expression."), Parameter("offset", "Rows behind."), Parameter("default", "Fallback value."))),
        Function("LAST_VALUE", NetezzaFunctionCategory.Window, Signature("LAST_VALUE(expression) OVER (...)", "Returns the last value in a window.", Parameter("expression", "Window expression."))),
        Function("LEAD", NetezzaFunctionCategory.Window, Signature("LEAD(expression [, offset [, default]]) OVER (...)", "Returns a following row value.", Parameter("expression", "Window expression."), Parameter("offset", "Rows ahead."), Parameter("default", "Fallback value."))),
        Function("LEAST", NetezzaFunctionCategory.Numeric, VariadicSignature("LEAST(value1, value2, ...)", "Returns the least value.", Parameter("value1", "First value."), Parameter("value2", "Additional value."))),
        Function("LENGTH", NetezzaFunctionCategory.String, Signature("LENGTH(string)", "Returns string length.", Parameter("string", "Input string."))),
        Function("LISTAGG", NetezzaFunctionCategory.Aggregate, Signature("LISTAGG(expression, delimiter)", "Aggregates values into a delimited string.", Parameter("expression", "Expression to concatenate."), Parameter("delimiter", "Separator."))),
        Function("LOWER", NetezzaFunctionCategory.String, Signature("LOWER(string)", "Converts a string to lowercase.", Parameter("string", "Input string."))),
        Function("MAX", NetezzaFunctionCategory.Aggregate, Signature("MAX(expression)", "Returns the maximum value.", Parameter("expression", "Expression."))),
        Function("MEDIAN", NetezzaFunctionCategory.Aggregate, Signature("MEDIAN(expression)", "Returns the median value.", Parameter("expression", "Numeric expression."))),
        Function("MIN", NetezzaFunctionCategory.Aggregate, Signature("MIN(expression)", "Returns the minimum value.", Parameter("expression", "Expression."))),
        Function("MOD", NetezzaFunctionCategory.Numeric, Signature("MOD(value, divisor)", "Returns a remainder.", Parameter("value", "Dividend."), Parameter("divisor", "Divisor."))),
        Function("MONTH", NetezzaFunctionCategory.DateTime, Signature("MONTH(date)", "Returns the month number.", Parameter("date", "Date expression."))),
        Function("MONTHS_BETWEEN", NetezzaFunctionCategory.DateTime, Signature("MONTHS_BETWEEN(date1, date2)", "Returns the number of months between dates.", Parameter("date1", "First date."), Parameter("date2", "Second date."))),
        Function("NEXT_MONTH", NetezzaFunctionCategory.DateTime, Signature("NEXT_MONTH(date)", "Returns the next month boundary.", Parameter("date", "Date expression."))),
        Function("NOW", NetezzaFunctionCategory.DateTime, Signature("NOW()", "Current database timestamp.")),
        Function("NTH_VALUE", NetezzaFunctionCategory.Window, Signature("NTH_VALUE(expression, n) OVER (...)", "Returns the nth value in a window.", Parameter("expression", "Window expression."), Parameter("n", "Position."))),
        Function("NTILE", NetezzaFunctionCategory.Window, Signature("NTILE(buckets) OVER (...)", "Divides rows into buckets.", Parameter("buckets", "Number of buckets."))),
        Function("NULLIF", NetezzaFunctionCategory.Conversion, Signature("NULLIF(value1, value2)", "Returns NULL when values are equal.", Parameter("value1", "First value."), Parameter("value2", "Second value."))),
        Function("NVL", NetezzaFunctionCategory.Conversion, Signature("NVL(value, replacement)", "Returns a replacement for NULL.", Parameter("value", "Value to test."), Parameter("replacement", "Fallback value."))),
        Function("NVL2", NetezzaFunctionCategory.Conversion, Signature("NVL2(value, ifNotNull, ifNull)", "Chooses a result based on NULL status.", Parameter("value", "Value to test."), Parameter("ifNotNull", "Result when not NULL."), Parameter("ifNull", "Result when NULL."))),
        Function("PERCENTILE_CONT", NetezzaFunctionCategory.Aggregate, Signature("PERCENTILE_CONT(fraction) WITHIN GROUP (ORDER BY expression)", "Returns an interpolated percentile.", Parameter("fraction", "Percentile between 0 and 1."))),
        Function("PERCENTILE_DISC", NetezzaFunctionCategory.Aggregate, Signature("PERCENTILE_DISC(fraction) WITHIN GROUP (ORDER BY expression)", "Returns a discrete percentile.", Parameter("fraction", "Percentile between 0 and 1."))),
        Function("POWER", NetezzaFunctionCategory.Numeric, Signature("POWER(base, exponent)", "Raises a value to a power.", Parameter("base", "Base value."), Parameter("exponent", "Exponent."))),
        Function("RANDOM", NetezzaFunctionCategory.NetezzaSpecific, Signature("RANDOM()", "Returns a pseudo-random value.")),
        Function("RANK", NetezzaFunctionCategory.Window, Signature("RANK() OVER (...)", "Ranks rows with gaps.")),
        Function("REGEXP_LIKE", NetezzaFunctionCategory.String, Signature("REGEXP_LIKE(string, pattern)", "Tests a regular expression match.", Parameter("string", "Input string."), Parameter("pattern", "Regular expression."))),
        Function("REGEXP_REPLACE", NetezzaFunctionCategory.String, Signature("REGEXP_REPLACE(string, pattern, replacement)", "Replaces regular expression matches.", Parameter("string", "Input string."), Parameter("pattern", "Regular expression."), Parameter("replacement", "Replacement string."))),
        Function("REPLACE", NetezzaFunctionCategory.String, Signature("REPLACE(string, search, replacement)", "Replaces a substring.", Parameter("string", "Input string."), Parameter("search", "Search string."), Parameter("replacement", "Replacement string."))),
        Function("ROUND", NetezzaFunctionCategory.Numeric, Signature("ROUND(value [, scale])", "Rounds a number.", Parameter("value", "Numeric expression."), Parameter("scale", "Optional decimal scale."))),
        Function("ROW_NUMBER", NetezzaFunctionCategory.Window, Signature("ROW_NUMBER() OVER (...)", "Numbers rows in a window.")),
        Function("SQRT", NetezzaFunctionCategory.Numeric, Signature("SQRT(value)", "Returns a square root.", Parameter("value", "Non-negative numeric expression."))),
        Function("STDDEV", NetezzaFunctionCategory.Aggregate, Signature("STDDEV(expression)", "Returns standard deviation.", Parameter("expression", "Numeric expression."))),
        Function("STRING_AGG", NetezzaFunctionCategory.Aggregate, Signature("STRING_AGG(expression, delimiter)", "Aggregates strings with a delimiter.", Parameter("expression", "Value to concatenate."), Parameter("delimiter", "Separator."))),
        Function("SUBSTR", NetezzaFunctionCategory.String, Signature("SUBSTR(string, start [, length])", "Extracts a substring.", Parameter("string", "Input string."), Parameter("start", "Start position."), Parameter("length", "Optional length."))),
        Function("SUBSTRING", NetezzaFunctionCategory.String, Signature("SUBSTRING(string, start [, length])", "Extracts a substring.", Parameter("string", "Input string."), Parameter("start", "Start position."), Parameter("length", "Optional length."))),
        Function("SUM", NetezzaFunctionCategory.Aggregate, Signature("SUM(expression)", "Returns the sum of values.", Parameter("expression", "Numeric expression."))),
        Function("TO_CHAR", NetezzaFunctionCategory.Conversion, Signature("TO_CHAR(value, format)", "Formats a value as text.", Parameter("value", "Value to format."), Parameter("format", "Format pattern."))),
        Function("TO_DATE", NetezzaFunctionCategory.Conversion, Signature("TO_DATE(string, format)", "Converts text to a date.", Parameter("string", "Date text."), Parameter("format", "Date format."))),
        Function("TO_NUMBER", NetezzaFunctionCategory.Conversion, Signature("TO_NUMBER(string)", "Converts text to a number.", Parameter("string", "Numeric text."))),
        Function("TO_TIMESTAMP", NetezzaFunctionCategory.Conversion, Signature("TO_TIMESTAMP(string, format)", "Converts text to a timestamp.", Parameter("string", "Timestamp text."), Parameter("format", "Timestamp format."))),
        Function("TRIM", NetezzaFunctionCategory.String, Signature("TRIM(string)", "Removes surrounding spaces.", Parameter("string", "Input string."))),
        Function("UPPER", NetezzaFunctionCategory.String, Signature("UPPER(string)", "Converts a string to uppercase.", Parameter("string", "Input string."))),
        Function("VARIANCE", NetezzaFunctionCategory.Aggregate, Signature("VARIANCE(expression)", "Returns variance.", Parameter("expression", "Numeric expression."))),
        Function("WIDTH_BUCKET", NetezzaFunctionCategory.Numeric, Signature("WIDTH_BUCKET(value, min, max, buckets)", "Returns a histogram bucket.", Parameter("value", "Expression."), Parameter("min", "Lower boundary."), Parameter("max", "Upper boundary."), Parameter("buckets", "Number of buckets."))),
        Function("YEAR", NetezzaFunctionCategory.DateTime, Signature("YEAR(date)", "Returns the year.", Parameter("date", "Date expression."))),
    ];

    // Names retained from the existing completion surface and the TMP Netezza
    // dialect. They intentionally use a generic signature until a dialect
    // source provides a more precise overload.
    private static readonly string[] AdditionalFunctionNames =
    [
        "AGE", "BITAND", "BITNOT", "BITOR", "BITXOR", "CONVERT", "DCEIL", "DFLOOR",
        "DURATION_ADD", "DURATION_SUBTRACT", "FIRST_DAY", "FORMAT", "FPOW", "GET_VIEWDEF",
        "GREATER", "HEX_TO_BINARY", "HEX_TO_GEOMETRY", "HOUR", "HOURS_BETWEEN", "INT_TO_STRING",
        "INT1AND", "INT1OR", "INT1XOR", "INT1NOT", "INT1INCR", "INT2INCR", "INT4INCR", "INT8INCR",
        "INT1DECR", "INT2DECR", "INT4DECR", "INT8DECR", "INT1SHL", "INT1SHR", "INT2SHL", "INT2SHR",
        "INT4SHL", "INT4SHR", "INT8SHL", "INT8SHR", "INT2AND", "INT2OR", "INT2XOR", "INT2NOT",
        "INT4AND", "INT4OR", "INT4XOR", "INT4NOT", "INT8AND", "INT8OR", "INT8XOR", "INT8NOT",
        "ISFALSE", "ISNOTFALSE", "ISNOTTRUE", "ISTRUE", "LAST_DAY", "MINUTES_BETWEEN",
        "NEXT_QUARTER", "NEXT_WEEK", "NEXT_YEAR", "NUMERIC_SQRT", "OVERLAPS", "POW", "SECONDS_BETWEEN",
        "SETSEED", "STDDEV_POP", "STDDEV_SAMP", "STRPOS", "STRING_TO_INT", "THIS_MONTH", "THIS_QUARTER",
        "THIS_WEEK", "THIS_YEAR", "TIMEOFDAY", "TIMEZONE", "TRANSLATE", "TRUNC", "UNICHR", "UNICODE",
        "UNICODES", "VAR_POP", "VAR_SAMP", "VERSION", "WEEKS_BETWEEN", "YEARS_BETWEEN",
        "REGEXP_CAPTURE", "REGEXP_COUNT", "REGEXP_EXTRACT", "REGEXP_FIND", "REGEXP_GMATCH", "REGEXP_GSPLIT",
        "REGEXP_SPLIT", "BASENAME", "DIRNAME", "STRLEN", "SPLIT", "URLDECODE", "URLENCODE",
        "URLPARSEQUERY", "DLE_DST", "LE_DST", "NYSIIS", "DBL_MP", "PRI_MP", "GROUPING",
    ];

    public static IReadOnlyList<NetezzaBuiltinFunction> BuiltinFunctions { get; } =
        CoreBuiltinFunctions
            .Concat(AdditionalFunctionNames.Select(name => Function(
                name,
                NetezzaFunctionCategory.NetezzaSpecific,
                Signature($"{name}(...)", "Netezza built-in function.", Parameter("expression", "Function expression.")))))
            .GroupBy(function => function.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

    public static IReadOnlyList<NetezzaDataTypeSpec> DataTypes { get; } =
    [
        new("BOOLEAN", ["BOOLEAN", "BOOL"]),
        new("INT1", ["INT1", "BYTEINT"]),
        new("INT2", ["INT2", "SMALLINT", "INT16"]),
        new("INT4", ["INT4", "INTEGER", "INT", "INT32"]),
        new("INT8", ["INT8", "BIGINT", "INT64"]),
        new("FLOAT4", ["FLOAT4", "REAL"]),
        new("FLOAT8", ["FLOAT8", "DOUBLE", "DOUBLE PRECISION"]),
        new("FLOAT", ["FLOAT"], 0, 1),
        new("NUMERIC", ["NUMERIC", "DECIMAL"], 0, 2),
        new("CHAR", ["CHAR", "CHARACTER", "FIXED"], 0, 1),
        new("VARCHAR", ["VARCHAR", "CHARACTER VARYING", "VARIABLE"], 0, 1, true),
        new("TEXT", ["TEXT"]),
        new("NCHAR", ["NCHAR", "NATIONAL CHARACTER"], 0, 1),
        new("NVARCHAR", ["NVARCHAR", "NATIONAL CHARACTER VARYING"], 0, 1, true),
        new("DATE", ["DATE"]),
        new("TIME", ["TIME"], 0, 1),
        new("TIMETZ", ["TIMETZ", "TIME WITH TIME ZONE"], 0, 1),
        new("TIMESTAMP", ["TIMESTAMP"], 0, 1),
        new("TIMESTAMPTZ", ["TIMESTAMPTZ", "TIMESTAMP WITH TIME ZONE"], 0, 1),
        new("INTERVAL", ["INTERVAL"], 0, 1),
        new("VARBYTE", ["VARBYTE"], 0, 1),
        new("SERIAL", ["SERIAL"]),
        new("BIGSERIAL", ["BIGSERIAL"]),
        new("CLOB", ["CLOB"], 0, 1),
        new("NCLOB", ["NCLOB"], 0, 1),
        new("BLOB", ["BLOB"], 0, 1),
    ];

    public static IReadOnlyList<string> BuiltinFunctionNames { get; } =
        BuiltinFunctions.Select(f => f.Name).ToArray();

    public static IReadOnlyList<string> DataTypeNames { get; } =
        DataTypes.SelectMany(t => t.Aliases).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public static IReadOnlyList<string> NetezzaKeywords { get; } =
    [
        "BEGIN_PROC", "END_PROC", "NOTICE", "DEBUG", "DISTRIBUTE", "RANDOM", "ORGANIZE",
        "GROOM", "GENERATE", "STATISTICS", "REFTABLE", "VARARGS", "NZPLSQL", "SESSION",
        "RECLAIM", "BACKUPSET", "EXPRESS", "SAMEAS", "HASH", "DISTRIBUTION", "PLANTEXT", "PLANGRAPH",
        "_V_SESSION", "_V_TABLE", "_V_VIEW", "_V_PROCEDURE", "_V_SYNONYM", "_V_RELATION_COLUMN",
        "_V_RELATION_KEYDATA", "_V_TABLE_DIST_MAP", "_V_TABLE_ORGANIZE_COLUMN", "_V_EXTERNAL",
        "_V_EXTOBJECT", "_V_DATABASE", "_V_SCHEMA"
    ];

    public static bool TryGetFunction(string name, out NetezzaBuiltinFunction function)
    {
        function = BuiltinFunctions.FirstOrDefault(f =>
            string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))!;
        return function is not null;
    }

    public static bool TryGetDataType(string name, out NetezzaDataTypeSpec type)
    {
        type = DataTypes.FirstOrDefault(t =>
            t.Aliases.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase)))!;
        return type is not null;
    }
}
