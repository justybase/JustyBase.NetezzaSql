using System.Data;
using System.Globalization;
using System.Text;

namespace JustyBase.ImportExport.Export;

public sealed record ExportOptions(
    char Delimiter = ',',
    string NewLine = "\r\n",
    bool IncludeHeaders = true,
    Encoding? Encoding = null,
    bool IncludeSqlMetadata = false,
    string? SqlText = null);

public sealed record ExportProgress(long RowsWritten, bool Completed = false);

/// <summary>Shared CSV writer used by Avalonia and Legacy hosts.</summary>
public static class CsvExportWriter
{
    public static async Task<long> WriteAsync(
        TextWriter writer,
        IReadOnlyList<string> headers,
        IAsyncEnumerable<IReadOnlyList<object?>> rows,
        ExportOptions? options = null,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);
        options ??= new ExportOptions();

        if (options.IncludeSqlMetadata && !string.IsNullOrEmpty(options.SqlText))
            await WriteLineAsync(writer, $"# SQL: {options.SqlText}", options.NewLine).ConfigureAwait(false);
        if (options.IncludeHeaders)
            await WriteLineAsync(writer, string.Join(options.Delimiter, headers.Select(value => Escape(value, options.Delimiter))), options.NewLine).ConfigureAwait(false);

        long written = 0;
        await foreach (var row in rows.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WriteLineAsync(writer, string.Join(options.Delimiter, row.Select(value => Escape(Convert.ToString(value, CultureInfo.InvariantCulture), options.Delimiter))), options.NewLine).ConfigureAwait(false);
            written++;
            if (written % 1000 == 0)
                progress?.Report(new ExportProgress(written));
        }
        progress?.Report(new ExportProgress(written, true));
        return written;
    }

    public static long WriteFromDataReader(
        TextWriter writer,
        IDataReader reader,
        ExportOptions? options = null,
        Action<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(reader);
        options ??= new ExportOptions();
        ValidateNewLine(options.NewLine);

        if (options.IncludeSqlMetadata && !string.IsNullOrEmpty(options.SqlText))
            WriteLine(writer, $"# SQL: {options.SqlText}", options.NewLine);

        if (options.IncludeHeaders)
        {
            WriteLine(
                writer,
                string.Join(options.Delimiter, Enumerable.Range(0, reader.FieldCount).Select(i => Escape(reader.GetName(i), options.Delimiter))),
                options.NewLine);
        }

        long written = 0;
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cells = new string[reader.FieldCount];
            for (int i = 0; i < reader.FieldCount; i++)
            {
                cells[i] = Escape(
                    reader.IsDBNull(i) ? null : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture),
                    options.Delimiter);
            }

            WriteLine(writer, string.Join(options.Delimiter, cells), options.NewLine);
            written++;
            if (written % 1000 == 0)
                progress?.Invoke(written);
        }

        progress?.Invoke(written);
        return written;
    }

    public static string Escape(string? value, char delimiter)
    {
        string text = value ?? string.Empty;
        return text.Contains(delimiter, StringComparison.Ordinal)
            || text.IndexOfAny(['"', '\r', '\n']) >= 0
            ? $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : text;
    }

    private static async Task WriteLineAsync(TextWriter writer, string line, string newLine)
        => await writer.WriteAsync(line + newLine).ConfigureAwait(false);

    private static void WriteLine(TextWriter writer, string line, string newLine)
        => writer.Write(line + newLine);

    private static void ValidateNewLine(string newLine)
    {
        if (newLine is not ("\n" or "\r\n" or "\r"))
            throw new ArgumentOutOfRangeException(nameof(newLine), "NewLine must be \\n, \\r\\n, or \\r.");
    }
}

/// <summary>
/// Excel-advanced options consumed by host writers (SpreadSheetTasks).
/// The CSV SoT lives in <see cref="CsvExportWriter"/>; Excel remains host-backed until
/// SpreadSheetTasks is packaged with ImportExport.
/// </summary>
public sealed record AdvancedExcelExportOptions(
    bool IntoExistingWorkbook = false,
    bool AddPivotSheet = false,
    string? ExistingSheet = null);
