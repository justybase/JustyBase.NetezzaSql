namespace JustyBase.NetezzaSqlParser.Authoring;

/// <summary>
/// Microsoft Access / Jet / ACE authoring metadata. The list deliberately
/// covers both native Access names and the aliases commonly accepted by
/// UCanAccess so completion, hover and semantic validation agree.
/// </summary>
public sealed class AccessSqlCatalog : ISqlAuthoringCatalog
{
    private static NetezzaFunctionSignature Signature(string label, string documentation) =>
        new(label, documentation, []);

    private static NetezzaBuiltinFunction Function(
        string name,
        NetezzaFunctionCategory category,
        string? label = null) =>
        new(name, category, [Signature(label ?? $"{name}(...)", "Microsoft Access SQL built-in function.")]);

    private static readonly IReadOnlyList<NetezzaBuiltinFunction> AccessFunctions =
    [
        Function("NZ", NetezzaFunctionCategory.Conversion, "Nz(value [, valueIfNull])"),
        Function("IIF", NetezzaFunctionCategory.Conversion, "IIf(condition, truePart, falsePart)"),
        Function("SWITCH", NetezzaFunctionCategory.Conversion, "Switch(expr1, value1 [, expr2, value2, ...])"),
        Function("NOW", NetezzaFunctionCategory.DateTime, "Now()"),
        Function("DATE", NetezzaFunctionCategory.DateTime, "Date()"),
        Function("TIME", NetezzaFunctionCategory.DateTime, "Time()"),
        Function("DATEVALUE", NetezzaFunctionCategory.DateTime, "DateValue(string)"),
        Function("TIMEVALUE", NetezzaFunctionCategory.DateTime, "TimeValue(string)"),
        Function("CDATE", NetezzaFunctionCategory.Conversion, "CDate(expression)"),
        Function("DATESERIAL", NetezzaFunctionCategory.DateTime, "DateSerial(year, month, day)"),
        Function("TIMESERIAL", NetezzaFunctionCategory.DateTime, "TimeSerial(hour, minute, second)"),
        Function("DATEADD", NetezzaFunctionCategory.DateTime, "DateAdd(interval, number, date)"),
        Function("DATEDIFF", NetezzaFunctionCategory.DateTime, "DateDiff(interval, date1, date2)"),
        Function("DATEPART", NetezzaFunctionCategory.DateTime, "DatePart(interval, date)"),
        Function("WEEKDAY", NetezzaFunctionCategory.DateTime, "Weekday(date [, firstdayofweek])"),
        Function("WEEKDAYNAME", NetezzaFunctionCategory.DateTime, "WeekdayName(weekday)"),
        Function("MONTHNAME", NetezzaFunctionCategory.DateTime, "MonthName(month)"),
        Function("YEAR", NetezzaFunctionCategory.DateTime, "Year(date)"),
        Function("MONTH", NetezzaFunctionCategory.DateTime, "Month(date)"),
        Function("DAY", NetezzaFunctionCategory.DateTime, "Day(date)"),
        Function("HOUR", NetezzaFunctionCategory.DateTime, "Hour(time)"),
        Function("MINUTE", NetezzaFunctionCategory.DateTime, "Minute(time)"),
        Function("SECOND", NetezzaFunctionCategory.DateTime, "Second(time)"),
        Function("INSTR", NetezzaFunctionCategory.String, "InStr([start, ]string1, string2)"),
        Function("INSTRREV", NetezzaFunctionCategory.String, "InStrRev(stringcheck, stringmatch)"),
        Function("MID", NetezzaFunctionCategory.String, "Mid(string, start [, length])"),
        Function("LEFT", NetezzaFunctionCategory.String, "Left(string, length)"),
        Function("RIGHT", NetezzaFunctionCategory.String, "Right(string, length)"),
        Function("ASC", NetezzaFunctionCategory.String, "Asc(string)"),
        Function("CHR", NetezzaFunctionCategory.String, "Chr(charcode)"),
        Function("STRCONV", NetezzaFunctionCategory.String, "StrConv(string, conversion)"),
        Function("STRCOMP", NetezzaFunctionCategory.String, "StrComp(string1, string2)"),
        Function("STRREVERSE", NetezzaFunctionCategory.String, "StrReverse(string)"),
        Function("UCASE", NetezzaFunctionCategory.String, "UCase(string)"),
        Function("LCASE", NetezzaFunctionCategory.String, "LCase(string)"),
        Function("TRIM", NetezzaFunctionCategory.String, "Trim(string)"),
        Function("LTRIM", NetezzaFunctionCategory.String, "LTrim(string)"),
        Function("RTRIM", NetezzaFunctionCategory.String, "RTrim(string)"),
        Function("SPACE", NetezzaFunctionCategory.String, "Space(number)"),
        Function("STRING", NetezzaFunctionCategory.String, "String(number, character)"),
        Function("LEN", NetezzaFunctionCategory.String, "Len(string)"),
        Function("FORMAT", NetezzaFunctionCategory.String, "Format(expression [, format])"),
        Function("VAL", NetezzaFunctionCategory.Numeric, "Val(string)"),
        Function("STR", NetezzaFunctionCategory.String, "Str(number)"),
        Function("INT", NetezzaFunctionCategory.Numeric, "Int(number)"),
        Function("FIX", NetezzaFunctionCategory.Numeric, "Fix(number)"),
        Function("SGN", NetezzaFunctionCategory.Numeric, "Sgn(number)"),
        Function("SIGN", NetezzaFunctionCategory.Numeric, "Sign(number)"),
        Function("CLNG", NetezzaFunctionCategory.Conversion, "CLng(expression)"),
        Function("CLONG", NetezzaFunctionCategory.Conversion, "CLong(expression)"),
        Function("CSIGN", NetezzaFunctionCategory.Conversion, "CSng(expression)"),
        Function("SQR", NetezzaFunctionCategory.Numeric, "Sqr(number)"),
        Function("SIN", NetezzaFunctionCategory.Numeric, "Sin(number)"),
        Function("COS", NetezzaFunctionCategory.Numeric, "Cos(number)"),
        Function("TAN", NetezzaFunctionCategory.Numeric, "Tan(number)"),
        Function("ASIN", NetezzaFunctionCategory.Numeric, "ASin(number)"),
        Function("ACOS", NetezzaFunctionCategory.Numeric, "ACos(number)"),
        Function("ATN", NetezzaFunctionCategory.Numeric, "Atn(number)"),
        Function("ABS", NetezzaFunctionCategory.Numeric, "Abs(number)"),
        Function("EXP", NetezzaFunctionCategory.Numeric, "Exp(number)"),
        Function("LOG", NetezzaFunctionCategory.Numeric, "Log(number)"),
        Function("LOG10", NetezzaFunctionCategory.Numeric, "Log10(number)"),
        Function("RND", NetezzaFunctionCategory.Numeric, "Rnd([number])"),
        Function("ISDATE", NetezzaFunctionCategory.Conversion, "IsDate(expression)"),
        Function("ISNUMERIC", NetezzaFunctionCategory.Conversion, "IsNumeric(expression)"),
        Function("CSTR", NetezzaFunctionCategory.Conversion, "CStr(expression)"),
        Function("CBOOL", NetezzaFunctionCategory.Conversion, "CBool(expression)"),
        Function("CBYTE", NetezzaFunctionCategory.Conversion, "CByte(expression)"),
        Function("CINT", NetezzaFunctionCategory.Conversion, "CInt(expression)"),
        Function("CDBL", NetezzaFunctionCategory.Conversion, "CDbl(expression)"),
        Function("CSNG", NetezzaFunctionCategory.Conversion, "CSng(expression)"),
        Function("CCUR", NetezzaFunctionCategory.Conversion, "CCur(expression)"),
        Function("CDEC", NetezzaFunctionCategory.Conversion, "CDec(expression)"),
        Function("CVAR", NetezzaFunctionCategory.Conversion, "CVar(expression)"),
        Function("PMT", NetezzaFunctionCategory.Numeric, "Pmt(rate, nper, pv [, fv [, type]])"),
        Function("NPER", NetezzaFunctionCategory.Numeric, "NPer(rate, pmt, pv [, fv [, type]])"),
        Function("PV", NetezzaFunctionCategory.Numeric, "PV(rate, nper, pmt [, fv [, type]])"),
        Function("FV", NetezzaFunctionCategory.Numeric, "FV(rate, nper, pmt [, pv [, type]])"),
        Function("SLN", NetezzaFunctionCategory.Numeric, "SLN(cost, salvage, life)"),
        Function("SYD", NetezzaFunctionCategory.Numeric, "SYD(cost, salvage, life, period)"),
        Function("DDB", NetezzaFunctionCategory.Numeric, "DDB(cost, salvage, life, period [, factor])"),
        Function("IPMT", NetezzaFunctionCategory.Numeric, "IPmt(rate, per, nper, pv [, fv [, type]])"),
        Function("PPMT", NetezzaFunctionCategory.Numeric, "PPmt(rate, per, nper, pv [, fv [, type]])"),
        Function("RATE", NetezzaFunctionCategory.Numeric, "Rate(nper, pmt, pv [, fv [, type [, guess]]])"),
        Function("NPV", NetezzaFunctionCategory.Numeric, "NPV(rate, value1 [, value2, ...])"),
        Function("MIRR", NetezzaFunctionCategory.Numeric, "MIRR(values, financeRate, reinvestRate)"),
        Function("DCOUNT", NetezzaFunctionCategory.Aggregate, "DCount(expr, domain [, criteria])"),
        Function("DSUM", NetezzaFunctionCategory.Aggregate, "DSum(expr, domain [, criteria])"),
        Function("DAVG", NetezzaFunctionCategory.Aggregate, "DAvg(expr, domain [, criteria])"),
        Function("DMIN", NetezzaFunctionCategory.Aggregate, "DMin(expr, domain [, criteria])"),
        Function("DMAX", NetezzaFunctionCategory.Aggregate, "DMax(expr, domain [, criteria])"),
        Function("DFIRST", NetezzaFunctionCategory.Aggregate, "DFirst(expr, domain [, criteria])"),
        Function("DLAST", NetezzaFunctionCategory.Aggregate, "DLast(expr, domain [, criteria])"),
        Function("DLOOKUP", NetezzaFunctionCategory.Aggregate, "DLookup(expr, domain [, criteria])"),
        Function("DSTDEV", NetezzaFunctionCategory.Aggregate, "DStDev(expr, domain [, criteria])"),
        Function("DSTDEVP", NetezzaFunctionCategory.Aggregate, "DStDevP(expr, domain [, criteria])"),
        Function("DVAR", NetezzaFunctionCategory.Aggregate, "DVar(expr, domain [, criteria])"),
        Function("DVARP", NetezzaFunctionCategory.Aggregate, "DVarP(expr, domain [, criteria])"),
        Function("FIRST", NetezzaFunctionCategory.Aggregate, "First(expression)"),
        Function("LAST", NetezzaFunctionCategory.Aggregate, "Last(expression)"),
        Function("STDEV", NetezzaFunctionCategory.Aggregate, "StDev(expression)"),
        Function("STDEVP", NetezzaFunctionCategory.Aggregate, "StDevP(expression)"),
        Function("VAR", NetezzaFunctionCategory.Aggregate, "Var(expression)"),
        Function("VARP", NetezzaFunctionCategory.Aggregate, "VarP(expression)"),
        Function("ROUND", NetezzaFunctionCategory.Numeric, "Round(expression [, numdecimalplaces])"),
        Function("PARTITION", NetezzaFunctionCategory.String, "Partition(number, start, stop, interval)"),
        Function("LBOUND", NetezzaFunctionCategory.Numeric, "LBound(array)"),
        Function("UBOUND", NetezzaFunctionCategory.Numeric, "UBound(array)"),
        Function("VERSION", NetezzaFunctionCategory.System, "Version()"),
        Function("EVAL", NetezzaFunctionCategory.System, "Eval(expression)"),
    ];

    private static readonly IReadOnlyList<NetezzaDataTypeSpec> AccessTypes =
    [
        new("YESNO", ["YESNO", "BIT", "BOOLEAN"]),
        new("BYTE", ["BYTE", "TINYINT"]),
        new("SHORT", ["SHORT", "SHORT INTEGER", "SMALLINT"]),
        new("LONG", ["LONG", "LONG INTEGER", "INTEGER", "INT"]),
        new("BIG", ["BIG", "BIG INTEGER", "BIGINT"]),
        new("COUNTER", ["COUNTER", "AUTOINCREMENT"]),
        new("SINGLE", ["SINGLE", "REAL"]),
        new("DOUBLE", ["DOUBLE", "FLOAT"], 0, 1),
        new("DECIMAL", ["DECIMAL", "NUMERIC"], 0, 2),
        new("CURRENCY", ["CURRENCY", "MONEY"]),
        new("DATE/TIME", ["DATE/TIME", "DATETIME", "DATE", "TIME"]),
        new("TEXT", ["TEXT", "CHAR", "VARCHAR"], 0, 1),
        new("LONGTEXT", ["LONGTEXT", "MEMO", "LONG VARCHAR"]),
        new("BINARY", ["BINARY", "VARBINARY"]),
        new("OLEOBJECT", ["OLEOBJECT", "IMAGE", "BLOB"]),
        new("GUID", ["GUID", "UNIQUEIDENTIFIER"]),
    ];

    public static AccessSqlCatalog Instance { get; } = new();

    public IReadOnlyList<NetezzaBuiltinFunction> BuiltinFunctions { get; } =
        SqlAuthoringCatalogComposer.MergeFunctions(AnsiSqlCatalog.BuiltinFunctions, AccessFunctions);

    public IReadOnlyList<NetezzaDataTypeSpec> DataTypes { get; } =
        SqlAuthoringCatalogComposer.MergeTypes(AnsiSqlCatalog.DataTypes, AccessTypes);

    public IReadOnlyList<string> DataTypeNames { get; } =
        SqlAuthoringCatalogComposer.MergeTypes(AnsiSqlCatalog.DataTypes, AccessTypes)
            .SelectMany(type => type.Aliases).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public IReadOnlyList<string> CompletionKeywords { get; } =
        SqlAuthoringCatalogComposer.MergeValues(AnsiSqlCatalog.CompletionKeywords,
        [
            "TOP", "PERCENT", "DISTINCTROW", "TRANSFORM", "PIVOT", "PARAMETERS",
            "OWNERACCESS", "OPTION", "AUTOINCREMENT", "COUNTER", "CURRENCY", "MEMO",
            "YESNO", "GUID", "IN", "WITH COMPRESSION", "WITH COMP", "INDEX"
        ]);

    public IReadOnlyList<string> Keywords { get; } =
        SqlAuthoringCatalogComposer.MergeValues(AnsiSqlCatalog.Keywords,
        [
            "TOP", "PERCENT", "DISTINCTROW", "TRANSFORM", "PIVOT", "PARAMETERS",
            "OWNERACCESS", "OPTION", "AUTOINCREMENT", "COUNTER", "CURRENCY", "MEMO",
            "YESNO", "GUID", "INDEX", "COMPRESSION", "COMP"
        ]);

    public SqlFormatterProfile FormatterProfile { get; } =
        SqlAuthoringCatalogComposer.MergeFormatterProfiles(
            AnsiSqlCatalog.FormatterProfile,
            new SqlFormatterProfile(["TOP", "PERCENT", "DISTINCTROW", "TRANSFORM", "PIVOT", "PARAMETERS", "OWNERACCESS"]));

    public bool TryGetFunction(string name, out NetezzaBuiltinFunction function)
    {
        function = BuiltinFunctions.FirstOrDefault(f =>
            string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))!;
        return function is not null;
    }

    public bool TryGetDataType(string name, out NetezzaDataTypeSpec type)
    {
        var normalized = name.Trim().ToUpperInvariant();
        type = DataTypes.FirstOrDefault(t => t.Aliases.Any(alias =>
            string.Equals(alias, normalized, StringComparison.OrdinalIgnoreCase)))!;
        return type is not null;
    }
}
