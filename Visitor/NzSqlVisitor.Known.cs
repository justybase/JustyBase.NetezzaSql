using JustyBase.NetezzaSqlParser.Ast;

namespace JustyBase.NetezzaSqlParser.Visitor;

public partial class NzSqlVisitor
{
    private static readonly HashSet<string> AggregateFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "SUM", "COUNT", "AVG", "MIN", "MAX", "STDDEV", "VARIANCE",
        "STDDEV_POP", "STDDEV_SAMP", "VAR_POP", "VAR_SAMP",
    };

    private static readonly HashSet<string> AlwaysAggregateFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "STRING_AGG", "ARRAY_AGG", "LISTAGG",
    };

    private static readonly HashSet<string> BooleanLiterals = new(StringComparer.OrdinalIgnoreCase)
    {
        "TRUE", "FALSE"
    };

    private static readonly HashSet<string> SpecialBuiltinValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "CURRENT_TIMESTAMP", "CURRENT_DATE", "CURRENT_TIME", "CURRENT_USER",
        "CURRENT_SID", "CURRENT_DATABASE", "CURRENT_SCHEMA", "CURRENT_CATALOG",
        "CURRENT_DB", "CURRENT_TIMEZONE", "CURRENT_TX_SCHEMA", "USER",
        "SESSION_USER", "SYSTEM_USER", "NOW", "TODAY", "TOMORROW", "YESTERDAY"
    };

    private static readonly HashSet<string> SystemColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "DATASLICEID", "ROWID", "CREATEXID", "DELETEXID"
    };

    private static readonly HashSet<string> KnownDataTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "INT", "INT1", "INT2", "INT4", "INT8", "INTEGER",
        "BIGINT", "SMALLINT", "BYTEINT",
        "NUMERIC", "DECIMAL", "FLOAT", "FLOAT4", "FLOAT8", "REAL", "DOUBLE", "DOUBLE PRECISION",
        "VARCHAR", "NVARCHAR", "CHAR", "NCHAR", "CHARACTER", "CHARACTER VARYING",
        "BOOLEAN", "BOOL",
        "DATE", "TIME", "TIMETZ", "TIMESTAMP", "TIMESTAMPTZ", "INTERVAL",
        "ST_GEOMETRY", "ST_POINT", "ST_LINESTRING", "ST_POLYGON",
        "BINARY", "VARBINARY", "BLOB", "CLOB", "NCLOB",
        "TEXT", "NTEXT",
        "VARRAY", "RECORD", "REFTABLE",
        "ALIAS", "REF CURSOR",
        "NATIONAL CHARACTER", "NATIONAL CHAR", "NATIONAL CHARACTER VARYING",
        "LONG VARCHAR", "LONG NVARCHAR", "CHAR VARYING",
    };

    private static readonly HashSet<string> KnownExternalTableOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "BOOLSTYLE", "COMPRESS", "CRINSTRING", "CTRLCHARS", "DATAOBJECT",
        "DATEDELIM", "DATESTYLE", "DATETIMEDELIM", "DECIMALDELIM", "DELIMITER",
        "ENCODING", "ESCAPECHAR", "FILLRECORD", "FORMAT", "IGNOREZERO",
        "INCLUDEHEADER", "INCLUDEZEROSECONDS", "LAYOUT", "LFINSTRING", "LOGDIR",
        "MAXERRORS", "MAXROWS", "MERIDIANDELIM", "NULLVALUE", "QUOTEDVALUE",
        "RECORDDELIM", "RECORDLENGTH", "REMOTESOURCE", "REQUIREQUOTES", "SKIPROWS",
        "SOCKETBUFSIZE", "TIMEDELIM", "TIMEROUNDNANOS", "TIMEEXTRAZEROS", "TIMESTYLE",
        "TRUNCSTRING", "Y2BASE", "UNIQUEID", "ACCESSKEYID", "SECRETACCESSKEY",
        "DEFAULTREGION", "BUCKETURL", "MULTIPARTSIZEMB", "ENDPOINT",
        "AZACCOUNT", "AZKEY", "AZCONTAINER", "AZMAXBLOCKS", "AZBLOCKSIZEMB", "AZLOGLEVEL",
    };

    private static readonly HashSet<string> KnownFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "ABS", "ADD_MONTHS", "AGE", "AVG", "BIGINT", "BITAND", "BITNOT", "BITOR", "BITXOR",
        "BTRIM", "CEIL", "CEILING", "COALESCE", "CONCAT", "CONVERT", "COUNT",
        "CURRENT_DATE", "CURRENT_TIME", "CURRENT_TIMESTAMP",
        "DATE_PART", "DATE_TRUNC", "DAY", "DAYS_BETWEEN", "DCEIL", "DECODE", "DENSE_RANK", "DFLOOR",
        "DURATION_ADD", "DURATION_SUBTRACT",
        "EXTRACT", "FIRST_DAY", "FIRST_VALUE", "FLOOR", "FORMAT", "FPOW",
        "GET_VIEWDEF", "GREATER", "GREATEST",
        "HASH", "HASH4", "HASH8", "HEX_TO_BINARY", "HEX_TO_GEOMETRY", "HOUR", "HOURS_BETWEEN",
        "INSTR", "INT_TO_STRING",
        "INT1AND", "INT1OR", "INT1XOR", "INT1NOT",
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
        "NTH_VALUE", "NTILE", "NULLIF", "NUMERIC_SQRT", "NVL", "NVL2", "NOW",
        "OVERLAPS", "PERCENTILE_CONT", "PERCENTILE_DISC", "POW", "POWER", "RANDOM", "RANK",
        "REGEXP_LIKE", "REGEXP_REPLACE", "REGEXP_SUBSTR",
        "REPLACE", "ROUND", "ROW_NUMBER",
        "SECONDS_BETWEEN", "SETSEED", "SQRT", "STDDEV", "STDDEV_POP", "STDDEV_SAMP", "STRING_AGG",
        "STRING_TO_INT", "STRPOS", "SUBSTR", "SUBSTRING", "SUM",
        "THIS_MONTH", "THIS_QUARTER", "THIS_WEEK", "THIS_YEAR",
        "TIMEOFDAY", "TIMEZONE", "TO_CHAR", "TO_DATE", "TO_NUMBER", "TO_TIMESTAMP",
        "TRANSLATE", "TRIM", "TRUNC", "UNICHR", "UNICODE", "UNICODES",
        "UPPER", "VARIANCE", "VAR_POP", "VAR_SAMP", "VERSION", "WEEKS_BETWEEN", "WIDTH_BUCKET", "YEAR", "YEARS_BETWEEN",
        "PROC_ARGUMENT_TYPES", "HASH_NVARCHAR", "SCORE_MP", "JOIN",
    };

    private static readonly HashSet<string> ExternalBooleanValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "TRUE", "FALSE", "ON", "OFF"
    };

    private static readonly HashSet<string> ExternalCompressValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "TRUE", "FALSE", "ON", "OFF", "ZLIB"
    };

    private static readonly HashSet<string> ExternalBoolStyleValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "1_0", "T_F", "Y_N", "YES_NO", "TRUE_FALSE"
    };

    private static readonly HashSet<string> ExternalDateStyleValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "YMD", "DMY", "MDY", "MONDY", "DMONY", "Y2MD", "DMY2", "MDY2", "MONDY2", "DMONY2"
    };

    private static readonly HashSet<string> ExternalDecimalDelimValues = new(StringComparer.OrdinalIgnoreCase)
    {
        ",", "."
    };

    private static readonly HashSet<string> ExternalEncodingValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "INTERNAL", "LATIN9", "UTF8", "UTF-8"
    };

    private static readonly HashSet<string> ExternalFormatValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "TEXT", "INTERNAL", "FIXED"
    };

    private static readonly HashSet<string> ExternalQuotedValueValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "NO", "YES", "SINGLE", "DOUBLE"
    };

    private static readonly HashSet<string> ExternalRemoteSourceValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "ODBC", "JDBC", "OLE-DB", "S3", "AZURE", "NZSQL", "YES"
    };

    private static readonly HashSet<string> ExternalTimeStyleValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "24HOUR", "12HOUR"
    };

    private void ValidateExternalOptionValue(string upperName, ExternalTableOption opt)
    {
        if (opt.Value is null)
        {
            var noValueOk = upperName is "FILLRECORD" or "INCLUDEHEADER" or "INCLUDEZEROSECONDS"
                or "REQUIREQUOTES" or "TIMEROUNDNANOS" or "TIMEEXTRAZEROS" or "TRUNCSTRING";
            if (!noValueOk)
            {
                AddError(
                    $"External table option '{opt.Name}' requires a value",
                    "error", "SQL017", opt.Position);
            }
            return;
        }

        var rawString = opt.Value switch
        {
            ExternalStringValue sv => sv.Value,
            ExternalIdentifierValue iv => iv.Value,
            ExternalNumberValue nv => nv.Value.ToString(),
            _ => null
        };
        if (rawString is null) return;

        // Strip surrounding single quotes from string literals
        var clean = rawString.Trim();
        if (clean.Length >= 2 && clean[0] == '\'' && clean[^1] == '\'')
            clean = clean[1..^1];
        var normalized = clean.Trim().ToUpperInvariant();

        var isBoolean = ExternalBooleanValues.Contains(normalized);
        var numericValue = opt.Value is ExternalNumberValue numVal ? numVal.Value : (long?)null;

        switch (upperName)
        {
            case "BOOLSTYLE":
                if (!ExternalBoolStyleValues.Contains(normalized))
                    goto case "invalid";
                break;
            case "COMPRESS":
                if (!ExternalCompressValues.Contains(normalized) && !normalized.StartsWith("ZSTD"))
                    goto case "invalid";
                break;
            case "CRINSTRING":
            case "CTRLCHARS":
            case "IGNOREZERO":
            case "LFINSTRING":
                if (!isBoolean) goto case "invalid";
                break;
            case "DATESTYLE":
                if (!ExternalDateStyleValues.Contains(normalized))
                    goto case "invalid";
                break;
            case "DECIMALDELIM":
                if (!ExternalDecimalDelimValues.Contains(normalized))
                    goto case "invalid";
                break;
            case "ENCODING":
                if (!ExternalEncodingValues.Contains(normalized))
                    goto case "invalid";
                break;
            case "FORMAT":
                if (!ExternalFormatValues.Contains(normalized))
                    goto case "invalid";
                break;
            case "QUOTEDVALUE":
                if (!ExternalQuotedValueValues.Contains(normalized))
                    goto case "invalid";
                break;
            case "REMOTESOURCE":
                if (!ExternalRemoteSourceValues.Contains(normalized))
                    goto case "invalid";
                break;
            case "TIMESTYLE":
                if (!ExternalTimeStyleValues.Contains(normalized))
                    goto case "invalid";
                break;
            case "MAXERRORS":
                if (numericValue < 0 || numericValue > 2147483647)
                    goto case "invalid";
                break;
            case "MAXROWS":
                if (numericValue < 0)
                    goto case "invalid";
                break;
            case "RECORDLENGTH":
                if (numericValue < 1)
                    goto case "invalid";
                break;
            case "SOCKETBUFSIZE":
                if (numericValue < 65536 || numericValue > 2147483648)
                    goto case "invalid";
                break;
            case "SKIPROWS":
                if (numericValue < 0)
                    goto case "invalid";
                break;
            case "Y2BASE":
                if (numericValue < 0)
                    goto case "invalid";
                break;
            case "AZMAXBLOCKS":
                if (numericValue < 1)
                    goto case "invalid";
                break;
            case "AZBLOCKSIZEMB":
                if (numericValue < 1 || numericValue > 99)
                    goto case "invalid";
                break;

            case "invalid":
                AddError(
                    $"Invalid value '{rawString}' for external table option '{opt.Name}'",
                    "error", "SQL017", opt.Position);
                break;
        }
    }
}
