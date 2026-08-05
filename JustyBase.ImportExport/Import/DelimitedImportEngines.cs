using System.Globalization;
using System.Text;
using JustyBase.NetezzaDdl;

namespace JustyBase.ImportExport.Import;

public sealed record PipeImportProgress(long RowsRead, long RowsWritten, long RowsSkipped, string? Error = null);

public sealed record CsvImportOptions(
    char Delimiter = ',',
    char Quote = '"',
    Encoding? Encoding = null,
    bool HasHeader = true,
    string? NullValue = null);

public sealed record FastCsvRawOptions(
    bool HasHeader = true,
    int SkipRows = 0,
    string? FilterPattern = null,
    string? TransformPattern = null,
    string? TransformReplacement = null);

public static class DelimitedRowEncoder
{
    public static string Encode(
        IEnumerable<object?> values,
        char delimiter = '\t',
        string nullValue = "",
        bool escapeControls = true)
    {
        ArgumentNullException.ThrowIfNull(values);
        return string.Join(delimiter, values.Select(value => EncodeValue(value, delimiter, nullValue, escapeControls)));
    }

    private static string EncodeValue(object? value, char delimiter, string nullValue, bool escapeControls)
    {
        if (value is null || value == DBNull.Value)
            return nullValue;
        string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        text = text.Replace("\\", "\\\\", StringComparison.Ordinal);
        if (escapeControls)
        {
            text = text.Replace("\r", "\\r", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal);
        }
        return text.Replace(delimiter.ToString(), "\\" + delimiter, StringComparison.Ordinal);
    }
}

public sealed class NetezzaImportEngine
{
    public async Task<IReadOnlyList<PipeImportProgress>> WriteTypedRowsAsync(
        IAsyncEnumerable<IReadOnlyList<object?>> rows,
        TextWriter destination,
        char delimiter = '\t',
        string nullValue = "",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(destination);
        var progress = new List<PipeImportProgress>();
        long read = 0;
        long written = 0;

        await foreach (var row in rows.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            read++;
            cancellationToken.ThrowIfCancellationRequested();
            await destination.WriteLineAsync(DelimitedRowEncoder.Encode(row, delimiter, nullValue)).ConfigureAwait(false);
            written++;
            if (written % 1000 == 0)
                progress.Add(new PipeImportProgress(read, written, 0));
        }

        progress.Add(new PipeImportProgress(read, written, 0));
        return progress;
    }

    public static string BuildInsertSql(
        string tableName,
        string pipeName,
        IReadOnlyList<string> columns,
        NetezzaImportUsingOptions? options = null,
        bool sameAs = false)
    {
        string sql = sameAs
            ? NetezzaImportSql.InsertSameAsFromExternalPipe(tableName, pipeName)
            : NetezzaImportSql.InsertFromExternalPipe(tableName, pipeName, columns);
        return sql + NetezzaImportSql.BuildUsingClause(options ?? NetezzaImportUsingOptions.Default) + ";";
    }
}

public static class FastCsvImportEngine
{
    public static async IAsyncEnumerable<string> ReadRawAsync(
        TextReader reader,
        FastCsvRawOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        options ??= new FastCsvRawOptions();
        if (options.SkipRows < 0)
            throw new ArgumentOutOfRangeException(nameof(options.SkipRows));

        var filter = options.FilterPattern is null
            ? null
            : new System.Text.RegularExpressions.Regex(
                options.FilterPattern,
                System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        var transform = options.TransformPattern is null
            ? null
            : new System.Text.RegularExpressions.Regex(
                options.TransformPattern,
                System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        int rowIndex = 0;
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (rowIndex++ < options.SkipRows || (options.HasHeader && rowIndex == options.SkipRows + 1))
                continue;
            if (filter is not null && !filter.IsMatch(line))
                continue;
            yield return transform is null
                ? line
                : transform.Replace(line, options.TransformReplacement ?? string.Empty);
        }
    }

    public static async IAsyncEnumerable<IReadOnlyList<string?>> ReadAsync(
        TextReader reader,
        CsvImportOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        options ??= new CsvImportOptions();
        string? line;
        var pending = new StringBuilder();
        bool firstRecord = true;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (pending.Length > 0)
                pending.Append('\n');
            pending.Append(line);
            if (!HasCompleteQuotes(pending, options.Quote))
                continue;

            string record = pending.ToString();
            pending.Clear();
            if (firstRecord && options.HasHeader)
            {
                firstRecord = false;
                continue;
            }
            firstRecord = false;
            yield return ParseRecord(record, options);
        }

        if (pending.Length > 0 && !(firstRecord && options.HasHeader))
            yield return ParseRecord(pending.ToString(), options);
    }

    private static bool HasCompleteQuotes(StringBuilder text, char quote)
    {
        int quotes = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != quote)
                continue;
            if (i + 1 < text.Length && text[i + 1] == quote)
                i++;
            else
                quotes++;
        }
        return quotes % 2 == 0;
    }

    private static IReadOnlyList<string?> ParseRecord(string record, CsvImportOptions options)
    {
        var fields = new List<string?>();
        var value = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < record.Length; i++)
        {
            char current = record[i];
            if (current == options.Quote)
            {
                if (quoted && i + 1 < record.Length && record[i + 1] == options.Quote)
                {
                    value.Append(options.Quote);
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (current == options.Delimiter && !quoted)
            {
                fields.Add(ToNull(value.ToString(), options.NullValue));
                value.Clear();
            }
            else
            {
                value.Append(current);
            }
        }
        fields.Add(ToNull(value.ToString(), options.NullValue));
        return fields;
    }

    private static string? ToNull(string value, string? nullValue)
        => nullValue is not null && value == nullValue ? null : value;
}
