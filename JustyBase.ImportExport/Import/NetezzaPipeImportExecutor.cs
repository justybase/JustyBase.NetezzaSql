using System.Buffers;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Text;

namespace JustyBase.ImportExport.Import;

/// <summary>
/// Named-pipe EXTERNAL load helpers shared by Avalonia and Legacy hosts.
/// Pipe topology is Windows-oriented (<c>\\.\pipe\...</c>) and requires
/// <c>REMOTESOURCE 'dotnet'</c> so the driver opens the path on the client.
/// </summary>
public static class NetezzaPipeImportExecutor
{
    public const char DefaultDelimiter = '\t';
    public const char DefaultEscapeChar = '\\';

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private static readonly NumberFormatInfo NumberWithDot = new()
    {
        NumberDecimalSeparator = ".",
        NumberGroupSeparator = string.Empty
    };

    public static string CreatePipeName(string prefix = "JB")
        => $"{prefix}_{Path.GetRandomFileName().Replace('.', '_')}";

    /// <summary>
    /// Starts a background named-pipe server that streams typed <see cref="IDataReader"/> rows
    /// with EXTERNAL-safe escaping (Avalonia SoT behavior).
    /// </summary>
    public static Task ServeDataReaderAsync(
        IDataReader reader,
        string pipeName,
        Action<string>? progress = null,
        bool preparedStringsMode = false,
        char delimiter = DefaultDelimiter,
        Encoding? encoding = null,
        long rowsCount = -1,
        Action<long>? rowProgress = null,
        long progressEvery = 10_000,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        encoding ??= Utf8NoBom;
        if (progressEvery <= 0)
            progressEvery = 10_000;

        string escape = DefaultEscapeChar.ToString();
        string escapedDelimiter = escape + delimiter;
        string escapedEscape = "\\\\";
        string escapedNewLine = escape + "\n";
        var valuesToEscape = SearchValues.Create([DefaultEscapeChar, delimiter, '\n', '\r']);

        return Task.Run(() =>
        {
            using var server = new NamedPipeServerStream(pipeName);
            server.WaitForConnection();
            using var writer = new StreamWriter(server, encoding, 65_536);

            object[] header = new object[reader.FieldCount];
            TypeCode[]? typeCodes = null;
            if (!preparedStringsMode)
            {
                typeCodes = new TypeCode[reader.FieldCount];
                for (int j = 0; j < reader.FieldCount; j++)
                {
                    header[j] = reader.GetName(j);
                    typeCodes[j] = Type.GetTypeCode(reader.GetFieldType(j));
                }
            }

            writer.Write(string.Join(delimiter, header));
            writer.Write('\n');
            writer.Flush();

            long progressLineNumber = 0;
            Span<char> spanBuffer = stackalloc char[64];
            var sw = Stopwatch.StartNew();
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    if (!preparedStringsMode && typeCodes is not null)
                    {
                        if (!reader.IsDBNull(i))
                            WriteTypedValue(writer, reader, i, typeCodes[i], spanBuffer, valuesToEscape, escapedEscape, delimiter, escapedDelimiter, escapedNewLine);
                    }
                    else
                    {
                        string val = Sanitize(reader.GetString(i), valuesToEscape, escapedEscape, delimiter, escapedDelimiter, escapedNewLine);
                        writer.Write(val);
                    }

                    if (i < reader.FieldCount - 1)
                        writer.Write(delimiter);
                    else
                        writer.Write('\n');
                }

                writer.Flush();
                progressLineNumber++;
                if (progressLineNumber % progressEvery == 0)
                {
                    rowProgress?.Invoke(progressLineNumber);
                    if (sw.Elapsed > TimeSpan.FromSeconds(1))
                    {
                        progress?.Invoke(rowsCount > 0
                            ? $"{(double)progressLineNumber / rowsCount:P1} rows loaded"
                            : $"{progressLineNumber:N0} rows loaded");
                        sw.Restart();
                    }
                }
            }

            writer.Flush();
            reader.Close();
        }, cancellationToken);
    }

    /// <summary>
    /// Starts a background named-pipe server that streams raw text lines (Legacy Fast CSV path).
    /// </summary>
    public static Task ServeRawLinesAsync(
        IAsyncEnumerable<string> lines,
        string pipeName,
        Encoding? encoding = null,
        Action<long>? progress = null,
        long progressEvery = 1000,
        Func<bool>? shouldStop = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        encoding ??= Utf8NoBom;

        return Task.Run(async () =>
        {
            using var server = new NamedPipeServerStream(pipeName);
            await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var writer = new StreamWriter(server, encoding, 65_536, leaveOpen: true)
            {
                NewLine = "\n"
            };
            long i = 0;
            await foreach (string line in lines.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                if (shouldStop?.Invoke() == true)
                    break;
                await writer.WriteAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
                await writer.WriteAsync("\n".AsMemory(), cancellationToken).ConfigureAwait(false);
                i++;
                if (progressEvery > 0 && i % progressEvery == 0)
                    progress?.Invoke(i);
            }

            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            progress?.Invoke(i);
        }, cancellationToken);
    }

    /// <summary>
    /// Synchronous file → pipe pump with Legacy Fast filter/transform semantics,
    /// built on <see cref="FastCsvImportEngine.ReadRawAsync"/> for the transform path.
    /// </summary>
    public static Task ServeFileLinesAsync(
        string filePath,
        string pipeName,
        FastCsvRawOptions? rawOptions = null,
        bool stopOnEmpty = false,
        bool singleColumnMode = false,
        char delimiter = DefaultDelimiter,
        char escapeChar = DefaultEscapeChar,
        Encoding? encoding = null,
        Action<long>? progress = null,
        long progressEvery = 1000,
        Func<bool>? shouldStop = null,
        string? rejectPattern = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        encoding ??= Utf8NoBom;
        rawOptions ??= new FastCsvRawOptions();

        return Task.Run(async () =>
        {
            using var input = new StreamReader(filePath, encoding);
            var reject = rejectPattern is null
                ? null
                : new System.Text.RegularExpressions.Regex(
                    rejectPattern,
                    System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
            string escapedDelimiter = $"{escapeChar}{delimiter}";

            async IAsyncEnumerable<string> Source([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
            {
                await foreach (string line in FastCsvImportEngine.ReadRawAsync(input, rawOptions, ct).ConfigureAwait(false))
                {
                    if (line.Length == 0 && stopOnEmpty)
                        yield break;
                    if (line.Length == 0)
                        continue;
                    if (reject is not null && reject.IsMatch(line))
                        continue;
                    string output = line;
                    if (singleColumnMode && output.Contains(delimiter))
                        output = output.Replace(delimiter.ToString(), escapedDelimiter, StringComparison.Ordinal);
                    yield return output;
                }
            }

            await ServeRawLinesAsync(Source(cancellationToken), pipeName, encoding, progress, progressEvery, shouldStop, cancellationToken)
                .ConfigureAwait(false);
        }, cancellationToken);
    }

    private static void WriteTypedValue(
        StreamWriter writer,
        IDataReader reader,
        int ordinal,
        TypeCode typeCode,
        Span<char> spanBuffer,
        SearchValues<char> valuesToEscape,
        string escapedEscape,
        char delimiter,
        string escapedDelimiter,
        string escapedNewLine)
    {
        switch (typeCode)
        {
            case TypeCode.Boolean:
                writer.Write(reader.GetBoolean(ordinal) ? 1 : 0);
                break;
            case TypeCode.Char:
            case TypeCode.SByte:
            case TypeCode.Byte:
            case TypeCode.Int16:
            case TypeCode.UInt16:
            case TypeCode.Int32:
            case TypeCode.UInt32:
            case TypeCode.Int64:
            case TypeCode.UInt64:
                writer.Write(reader.GetInt64(ordinal));
                break;
            case TypeCode.Single:
                writer.Write(FormatNumeric(reader.GetFloat(ordinal), spanBuffer));
                break;
            case TypeCode.Double:
                writer.Write(FormatNumeric(reader.GetDouble(ordinal), spanBuffer));
                break;
            case TypeCode.Decimal:
                writer.Write(FormatNumeric(reader.GetDecimal(ordinal), spanBuffer));
                break;
            case TypeCode.DateTime:
                writer.Write(FormatDateTime(reader.GetDateTime(ordinal), spanBuffer));
                break;
            case TypeCode.String:
                writer.Write(Sanitize(reader.GetString(ordinal), valuesToEscape, escapedEscape, delimiter, escapedDelimiter, escapedNewLine));
                break;
        }
    }

    /// <summary>
    /// Formats a numeric value without forced decimals or exponent notation. A fixed F6
    /// format exceeded the scale of NUMERIC(p,s) columns inferred by the shared type
    /// chooser (e.g. NUMERIC(16,2) rejects "10.500000").
    /// </summary>
    internal static string FormatNumeric(float value, Span<char> buffer)
        => FormatNumericCore(value.TryFormat(buffer, out int written, "0.###############################", NumberWithDot), buffer, written);

    internal static string FormatNumeric(double value, Span<char> buffer)
        => FormatNumericCore(value.TryFormat(buffer, out int written, "0.###############################", NumberWithDot), buffer, written);

    internal static string FormatNumeric(decimal value, Span<char> buffer)
        => FormatNumericCore(value.TryFormat(buffer, out int written, "0.###############################", NumberWithDot), buffer, written);

    private static string FormatNumericCore(bool ok, Span<char> buffer, int written)
        => ok ? buffer[..written].ToString() : string.Empty;

    /// <summary>
    /// Midnight values carry no time information; emitting the date-only form keeps them
    /// loadable into DATE columns (a timestamp string is rejected by Netezza DATE).
    /// </summary>
    internal static string FormatDateTime(DateTime value, Span<char> buffer)
    {
        bool ok = value.TimeOfDay == TimeSpan.Zero
            ? value.TryFormat(buffer, out int written, "yyyy-MM-dd")
            : value.TryFormat(buffer, out written, "yyyy-MM-dd HH:mm:ss");
        return ok ? buffer[..written].ToString() : string.Empty;
    }

    public static string Sanitize(
        string? val,
        SearchValues<char> valuesToEscape,
        string escapedEscape,
        char delimiter,
        string escapedDelimiter,
        string escapedNewLine)
    {
        if (val is null)
            return string.Empty;
        if (val.AsSpan().IndexOfAny(valuesToEscape) == -1)
            return val;

        string result = val;
        if (result.Contains(DefaultEscapeChar))
            result = result.Replace("\\", escapedEscape, StringComparison.Ordinal);
        if (result.Contains(delimiter))
            result = result.Replace(delimiter.ToString(), escapedDelimiter, StringComparison.Ordinal);
        if (result.Contains('\n'))
            result = result.Replace("\n", escapedNewLine, StringComparison.Ordinal);
        if (result.Contains('\r'))
            result = result.Replace("\r", string.Empty, StringComparison.Ordinal);
        return result;
    }
}
