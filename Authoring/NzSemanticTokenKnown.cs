namespace JustyBase.NetezzaSqlParser.Authoring;

internal static class NzSemanticTokenKnown
{
    public static readonly HashSet<string> DataTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "INT", "INT1", "INT2", "INT4", "INT8", "INTEGER",
        "BIGINT", "SMALLINT", "BYTEINT",
        "NUMERIC", "DECIMAL", "FLOAT", "FLOAT4", "FLOAT8", "REAL", "DOUBLE", "DOUBLE PRECISION",
        "VARCHAR", "NVARCHAR", "CHAR", "NCHAR", "CHARACTER", "CHARACTER VARYING", "CHAR VARYING",
        "BOOLEAN", "BOOL",
        "DATE", "TIME", "TIMETZ", "TIMESTAMP", "TIMESTAMPTZ", "INTERVAL",
        "BINARY", "VARBINARY", "BLOB", "CLOB", "NCLOB",
        "TEXT", "NTEXT",
        "VARRAY", "RECORD", "REFTABLE",
        "NATIONAL CHARACTER", "NATIONAL CHAR", "NATIONAL CHARACTER VARYING",
        "LONG VARCHAR", "LONG NVARCHAR",
    };

    public static readonly HashSet<string> FunctionNames = new(StringComparer.Ordinal)
    {
        "ABS", "ADD_MONTHS", "AGE", "AVG", "BIGINT", "BITAND", "BITNOT", "BITOR", "BITXOR",
        "BTRIM", "CEIL", "CEILING", "COALESCE", "CONCAT", "CONVERT", "COUNT",
        "DATE_PART", "DATE_TRUNC", "DAY", "DAYS_BETWEEN", "DCEIL", "DECODE", "DENSE_RANK", "DFLOOR",
        "DURATION_ADD", "DURATION_SUBTRACT",
        "EXTRACT", "FIRST_DAY", "FIRST_VALUE", "FLOOR", "FORMAT", "FPOW",
        "GET_VIEWDEF", "GREATER", "GREATEST",
        "HASH", "HASH4", "HASH8", "HEX_TO_BINARY", "HEX_TO_GEOMETRY", "HOUR", "HOURS_BETWEEN",
        "INSTR", "INT_TO_STRING", "INT1AND", "INT1OR", "INT1XOR", "INT1NOT",
        "INT1INCR", "INT2INCR", "INT4INCR", "INT8INCR",
        "INT1DECR", "INT2DECR", "INT4DECR", "INT8DECR",
        "INT1SHL", "INT1SHR", "INT2SHL", "INT2SHR", "INT4SHL", "INT4SHR", "INT8SHL", "INT8SHR",
        "INT2AND", "INT2OR", "INT2XOR", "INT2NOT",
        "INT4AND", "INT4OR", "INT4XOR", "INT4NOT",
        "INT8AND", "INT8OR", "INT8XOR", "INT8NOT",
        "ISFALSE", "ISNOTFALSE", "ISNOTTRUE", "ISTRUE",
        "LAG", "LAST_DAY", "LAST_VALUE", "LEAD", "LEAST", "LENGTH",
        "LISTAGG", "LOWER", "MAX", "MEDIAN", "MIN", "MINUTES_BETWEEN", "MOD", "MONTH",
        "MONTHS_BETWEEN", "NEXT_MONTH", "NEXT_QUARTER", "NEXT_WEEK", "NEXT_YEAR",
        "NTH_VALUE", "NTILE", "NULLIF", "NUMERIC_SQRT", "NVL", "NVL2",
        "OVERLAPS", "POW", "POWER", "RANDOM", "RANK",
        "REGEXP_LIKE", "REGEXP_REPLACE", "REGEXP_SUBSTR",
        "REPLACE", "ROUND", "ROW_NUMBER",
        "SECONDS_BETWEEN", "SETSEED", "SQRT", "STDDEV", "STDDEV_POP", "STDDEV_SAMP",
        "STRING_AGG", "STRING_TO_INT", "STRPOS", "SUBSTR", "SUBSTRING", "SUM",
        "THIS_MONTH", "THIS_QUARTER", "THIS_WEEK", "THIS_YEAR",
        "TIMEOFDAY", "TIMEZONE", "TO_CHAR", "TO_DATE", "TO_NUMBER", "TO_TIMESTAMP",
        "TRANSLATE", "TRIM", "TRUNC", "UNICHR", "UNICODE", "UNICODES",
        "UPPER", "VARIANCE", "VAR_POP", "VAR_SAMP", "VERSION",
        "WEEKS_BETWEEN", "WIDTH_BUCKET", "YEAR", "YEARS_BETWEEN",
    };

    public const int LargeDocumentCharLimit = 500_000;
}
