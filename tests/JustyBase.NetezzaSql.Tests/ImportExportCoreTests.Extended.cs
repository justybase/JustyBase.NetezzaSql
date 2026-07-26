using System.IO.Pipes;
using System.Text;
using JustyBase.ImportExport.Export;
using JustyBase.ImportExport.Import;

namespace JustyBase.NetezzaSql.Tests;

public sealed class ImportExportExtendedTests
{
    [Theory]
    [InlineData("plain", ',', "plain")]
    [InlineData("a,b", ',', "\"a,b\"")]
    [InlineData("say \"hi\"", ',', "\"say \"\"hi\"\"\"")]
    [InlineData("line\nbreak", ',', "\"line\nbreak\"")]
    public void CsvExportWriter_Escape_quotes_when_needed(string input, char delimiter, string expected)
        => Assert.Equal(expected, CsvExportWriter.Escape(input, delimiter));

    [Fact]
    public void CsvExportWriter_Escape_null_as_empty()
        => Assert.Equal(string.Empty, CsvExportWriter.Escape(null, ','));

    [Fact]
    public async Task CsvExportWriter_WriteAsync_writes_headers_metadata_and_rows()
    {
        var sb = new StringBuilder();
        using var writer = new StringWriter(sb);
        var progress = new SyncProgressList<ExportProgress>();
        async IAsyncEnumerable<IReadOnlyList<object?>> Rows()
        {
            yield return ["1", "Ada"];
            await Task.CompletedTask;
        }

        long count = await CsvExportWriter.WriteAsync(
            writer,
            ["id", "name"],
            Rows(),
            new ExportOptions(Delimiter: '|', IncludeSqlMetadata: true, SqlText: "SELECT 1"),
            progress);

        Assert.Equal(1, count);
        string text = sb.ToString();
        Assert.Contains("# SQL: SELECT 1", text, StringComparison.Ordinal);
        Assert.Contains("id|name", text, StringComparison.Ordinal);
        Assert.Contains("1|Ada", text, StringComparison.Ordinal);
        Assert.True(progress.Items.Exists(p => p.Completed));
    }

    [Fact]
    public void CsvExportWriter_WriteFromDataReader_rejects_invalid_newline()
    {
        using var reader = new StubDataReader([("id", typeof(int))], [1]);
        using var writer = new StringWriter();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CsvExportWriter.WriteFromDataReader(writer, reader, new ExportOptions(NewLine: "bad")));
    }

    [Fact]
    public void CsvExportWriter_WriteFromDataReader_writes_null_and_header()
    {
        using var reader = new StubDataReader(
            [("id", typeof(int)), ("note", typeof(string))],
            [1, null]);
        using var writer = new StringWriter();
        long written = CsvExportWriter.WriteFromDataReader(
            writer,
            reader,
            new ExportOptions(Delimiter: '\t', NewLine: "\n", IncludeHeaders: true));
        Assert.Equal(1, written);
        string[] lines = writer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("id\tnote", lines[0]);
        Assert.Equal("1\t", lines[1]);
    }

    [Fact]
    public void DatabaseTypeChooser_Infer_detects_common_types()
    {
        var columns = DatabaseTypeChooser.Infer(
            ["flag", "count", "amount", "when", "text"],
            [
                ["true", "1", "1.5", "2020-01-01", "hello"],
                ["false", "2", "2.0", "2020-02-01", "world"]
            ]);

        Assert.Equal("BOOLEAN", columns[0].NetezzaType);
        Assert.Equal("INTEGER", columns[1].NetezzaType);
        Assert.Equal("NUMERIC(38,10)", columns[2].NetezzaType);
        Assert.Equal("DATETIME", columns[3].NetezzaType);
        Assert.StartsWith("VARCHAR(", columns[4].NetezzaType, StringComparison.Ordinal);
        Assert.False(columns[0].IsNullable);
    }

    [Fact]
    public void DatabaseTypeChooser_Infer_empty_column_uses_default_varchar()
    {
        var columns = DatabaseTypeChooser.Infer(["x"], [[]], varcharLength: 100);
        Assert.Equal("VARCHAR(100)", columns[0].NetezzaType);
        Assert.True(columns[0].IsNullable);
    }

    [Fact]
    public void DatabaseTypeChooser_Infer_rejects_invalid_varchar_length()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            DatabaseTypeChooser.Infer(["a"], [["x"]], varcharLength: 0));

    [Fact]
    public void DelimitedRowEncoder_Encode_null_uses_null_marker()
        => Assert.Equal("\\N", DelimitedRowEncoder.Encode([DBNull.Value], '\t', nullValue: "\\N"));

    [Fact]
    public async Task FastCsv_ReadAsync_honors_null_value_marker()
    {
        using var input = new StringReader("a\n\\N\n");
        var rows = new List<IReadOnlyList<string?>>();
        await foreach (var row in FastCsvImportEngine.ReadAsync(input, new CsvImportOptions(HasHeader: false, NullValue: "\\N")))
            rows.Add(row);
        Assert.Null(rows[1][0]);
    }

    [Fact]
    public async Task FastCsv_ReadRaw_rejects_negative_skip_rows()
    {
        using var input = new StringReader("x");
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
        {
            await foreach (var unused in FastCsvImportEngine.ReadRawAsync(input, new FastCsvRawOptions(SkipRows: -1)))
                _ = unused;
        });
    }

    [Fact]
    public async Task NetezzaImportEngine_WriteTypedRowsAsync_streams_encoded_rows()
    {
        async IAsyncEnumerable<IReadOnlyList<object?>> Rows()
        {
            yield return ["a", 1];
            yield return ["b", 2];
            await Task.CompletedTask;
        }

        using var dest = new StringWriter();
        var engine = new NetezzaImportEngine();
        var progress = await engine.WriteTypedRowsAsync(Rows(), dest, delimiter: '|', nullValue: "");
        Assert.Equal(2, progress[^1].RowsWritten);
        Assert.Contains("a|1", dest.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void PipeExecutor_CreatePipeName_uses_prefix()
    {
        string name = NetezzaPipeImportExecutor.CreatePipeName("jb_test");
        Assert.StartsWith("jb_test_", name, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PipeExecutor_ServeRawLinesAsync_streams_to_client()
    {
        string pipe = NetezzaPipeImportExecutor.CreatePipeName("ut");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Task serve = NetezzaPipeImportExecutor.ServeRawLinesAsync(
            Lines(),
            pipe,
            cancellationToken: cts.Token);

        await Task.Delay(50, cts.Token);
        using var client = new NamedPipeClientStream(".", pipe, PipeDirection.In);
        await client.ConnectAsync(5000, cts.Token);
        using var reader = new StreamReader(client, Encoding.UTF8);
        string all = await reader.ReadToEndAsync(cts.Token);
        await serve.WaitAsync(cts.Token);

        Assert.Equal("alpha\nbeta\n", all);

        static async IAsyncEnumerable<string> Lines()
        {
            yield return "alpha";
            yield return "beta";
            await Task.CompletedTask;
        }
    }

    [Fact]
    public async Task PipeExecutor_ServeDataReaderAsync_streams_typed_header_and_row()
    {
        string pipe = NetezzaPipeImportExecutor.CreatePipeName("ut");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var dataReader = new StubDataReader(
            [
                ("flag", typeof(bool)),
                ("n", typeof(int)),
                ("label", typeof(string)),
                ("amount", typeof(decimal)),
                ("when", typeof(DateTime))
            ],
            [true, 7, "ok", 1.5m, new DateTime(2020, 1, 2, 3, 4, 5)]);

        Task serve = NetezzaPipeImportExecutor.ServeDataReaderAsync(dataReader, pipe, cancellationToken: cts.Token);
        await Task.Delay(50, cts.Token);
        using var client = new NamedPipeClientStream(".", pipe, PipeDirection.In);
        await client.ConnectAsync(5000, cts.Token);
        using var reader = new StreamReader(client, Encoding.UTF8);
        string all = await reader.ReadToEndAsync(cts.Token);
        await serve.WaitAsync(cts.Token);

        Assert.StartsWith("flag\tn\tlabel\tamount\twhen\n", all, StringComparison.Ordinal);
        Assert.Contains("ok", all, StringComparison.Ordinal);
    }

    [Fact]
    public void Sanitize_returns_unchanged_when_no_special_chars()
    {
        var values = System.Buffers.SearchValues.Create(['\\', '\t', '\n', '\r']);
        Assert.Equal("plain", NetezzaPipeImportExecutor.Sanitize("plain", values, "\\\\", '\t', "\\t", "\\n"));
    }

    [Fact]
    public void Sanitize_null_returns_empty()
    {
        var values = System.Buffers.SearchValues.Create(['\\', '\t', '\n', '\r']);
        Assert.Equal(string.Empty, NetezzaPipeImportExecutor.Sanitize(null, values, "\\\\", '\t', "\\t", "\\n"));
    }

    private sealed class SyncProgressList<T> : IProgress<T>
    {
        public List<T> Items { get; } = [];
        public void Report(T value) => Items.Add(value);
    }
}
