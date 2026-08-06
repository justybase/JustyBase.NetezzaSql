namespace JustyBase.NetezzaDdl;

/// <summary>Builds Netezza SQL used by streaming-import adapters.</summary>
public static class NetezzaImportSql
{
    public static readonly NetezzaImportUsingOptions DefaultUsingOptions = NetezzaImportUsingOptions.Default;

    public static string CreateRandomDistributionTable(string tableName, IReadOnlyList<string> columnDefinitions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentNullException.ThrowIfNull(columnDefinitions);
        if (columnDefinitions.Count == 0 || columnDefinitions.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("At least one column definition is required.", nameof(columnDefinitions));

        return $"CREATE TABLE {NetezzaNameHelper.QuoteNameIfNeeded(tableName)} ({string.Join(",", columnDefinitions)}){Environment.NewLine}DISTRIBUTE ON RANDOM;{Environment.NewLine}{Environment.NewLine}";
    }

    public static string InsertFromExternalPipe(string tableName, string pipeName, IReadOnlyList<string> columns)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        ArgumentNullException.ThrowIfNull(columns);
        if (columns.Count == 0 || columns.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("At least one column is required.", nameof(columns));

        return $"INSERT INTO {NetezzaNameHelper.QuoteNameIfNeeded(tableName)} SELECT * FROM EXTERNAL '\\\\.\\pipe\\{NetezzaNameHelper.EscapeLiteral(pipeName)}' ({string.Join(',', columns)}) ";
    }

    /// <summary>
    /// Inserts into an existing table through an explicit destination column list, mapping the
    /// piped source columns positionally: <c>INSERT INTO t (c1, c2) SELECT * FROM EXTERNAL 'pipe' (s1 T, s2 T)</c>.
    /// </summary>
    public static string InsertIntoColumnsFromExternalPipe(
        string tableName,
        IReadOnlyList<string> targetColumns,
        string pipeName,
        IReadOnlyList<string> pipeColumnDefinitions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentNullException.ThrowIfNull(targetColumns);
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        ArgumentNullException.ThrowIfNull(pipeColumnDefinitions);
        if (targetColumns.Count == 0 || targetColumns.Count != pipeColumnDefinitions.Count
            || targetColumns.Any(string.IsNullOrWhiteSpace) || pipeColumnDefinitions.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Target and pipe columns must be non-empty and of equal length.", nameof(targetColumns));
        }

        return $"INSERT INTO {NetezzaNameHelper.QuoteNameIfNeeded(tableName)} ({string.Join(',', targetColumns)}) " +
               $"SELECT * FROM EXTERNAL '\\\\.\\pipe\\{NetezzaNameHelper.EscapeLiteral(pipeName)}' ({string.Join(',', pipeColumnDefinitions)}) ";
    }

    public static string InsertSameAsFromExternalPipe(string tableName, string pipeName)
    {        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        string cleanTable = NetezzaNameHelper.QuoteNameIfNeeded(tableName);
        return $"INSERT INTO {cleanTable} SELECT * FROM EXTERNAL '\\\\.\\pipe\\{NetezzaNameHelper.EscapeLiteral(pipeName)}' SAMEAS {cleanTable} ";
    }

    /// <summary>Builds the common USING clause for both typed and Fast imports.</summary>
    public static string BuildUsingClause(NetezzaImportUsingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        string remoteSource = string.IsNullOrWhiteSpace(options.RemoteSource)
            ? NetezzaImportUsingOptions.DefaultRemoteSource
            : options.RemoteSource;

        var values = new List<string>
        {
            $"DELIMITER {QuoteUsingLiteral(options.Delimiter)}",
            $"ENCODING {QuoteUsingLiteral(options.EncodingName)}",
            // Always required: makes the driver open DATAOBJECT/pipe on the client.
            $"REMOTESOURCE {QuoteUsingLiteral(remoteSource)}"
        };

        AddNumber(values, "SKIPROWS", options.SkipRows);
        Add(values, "ESCAPECHAR", options.EscapeChar);
        Add(values, "TIMESTYLE", options.TimeStyle);
        Add(values, "QUOTEDVALUE", options.QuotedValue);
        AddNumber(values, "SOCKETBUFSIZE", options.SocketBufferSize);
        AddPositiveNumber(values, "MAXROWS", options.MaxRows);
        AddNumber(values, "MAXERRORS", options.MaxErrors);
        Add(values, "DECIMALDELIM", options.DecimalDelimiter ?? options.DecimalDelim);
        if (options.NullValue is not null)
            values.Add($"NULLVALUE {QuoteUsingLiteral(options.NullValue)}");
        Add(values, "DATESTYLE", options.DateStyle);
        Add(values, "BOOLSTYLE", options.BoolStyle);
        Add(values, "TIMEDELIM", options.TimeDelimiter ?? options.TimeDelim);
        AddNumber(values, "Y2BASE", options.Y2Base);
        Add(values, "LOGDIR", options.LogDirectory);

        AddFlag(values, "TRUNCSTRING", options.TruncateStrings || options.TruncString);
        AddFlag(values, "CRINSTRING", options.CrInString || options.CRinString);
        AddFlag(values, "FILLRECORD", options.FillRecord);
        AddFlag(values, "IGNOREZEROES", options.IgnoreZeroes);
        AddFlag(values, "REQUIREQUOTES", options.RequireQuotes);
        AddFlag(values, "STRIPNULLS", options.StripNulls);
        AddFlag(values, "CTRLCHARS", options.AllowControlCharacters);
        AddFlag(values, "TRIMBLANKLINES", options.TrimBlankLines);
        AddNullableFlag(values, "IncludeHeader", options.IncludeHeader);
        AddNullableFlag(values, "IncludeZeroSeconds", options.IncludeZeroSeconds);
        AddNullableFlag(values, "Compress", options.Compress);
        AddNullableFlag(values, "LfInString", options.LfInString);
        AddNullableFlag(values, "TimeRoundNanos", options.TimeRoundNanos);

        return $"USING ({string.Join(" ", values)})";
    }

    private static void Add(List<string> values, string name, object? value)
    {
        if (value is null || string.IsNullOrWhiteSpace(Convert.ToString(value)))
            return;
        values.Add($"{name} {QuoteUsingLiteral(Convert.ToString(value)!)}");
    }

    private static void AddFlag(List<string> values, string name, bool value)
    {
        if (value)
            values.Add(name);
    }

    private static void AddNumber(List<string> values, string name, long? value)
    {
        if (value is not null)
            values.Add($"{name} {value.Value}");
    }

    private static void AddPositiveNumber(List<string> values, string name, long? value)
    {
        if (value is > 0)
            values.Add($"{name} {value.Value}");
    }

    private static void AddNullableFlag(List<string> values, string name, bool? value)
    {
        if (value is not null)
            values.Add($"{name} {value.Value}");
    }

    private static string QuoteUsingLiteral(string value)
        => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
}
