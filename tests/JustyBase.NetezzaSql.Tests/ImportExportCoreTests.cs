using JustyBase.ImportExport.Export;
using JustyBase.ImportExport.Import;
using JustyBase.NetezzaDdl;
using System.Text;

namespace JustyBase.NetezzaSql.Tests;

public sealed class ImportExportCoreTests
{
    [Fact]
    public void PipeNumericFormat_avoids_forced_decimals_and_exponent_notation()
    {
        Span<char> buffer = stackalloc char[64];
        Assert.Equal("10.5", NetezzaPipeImportExecutor.FormatNumeric(10.5m, buffer));
        Assert.Equal("20.75", NetezzaPipeImportExecutor.FormatNumeric(20.75m, buffer));
        Assert.Equal("0", NetezzaPipeImportExecutor.FormatNumeric(0m, buffer));
        Assert.Equal("1.5", NetezzaPipeImportExecutor.FormatNumeric(1.5d, buffer));
        Assert.Equal("1.25", NetezzaPipeImportExecutor.FormatNumeric(1.25f, buffer));
        Assert.DoesNotContain("E", NetezzaPipeImportExecutor.FormatNumeric(0.0000001d, buffer), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PipeDateTimeFormat_midnight_emits_date_only()
    {
        Span<char> buffer = stackalloc char[64];
        Assert.Equal("2024-01-15", NetezzaPipeImportExecutor.FormatDateTime(new DateTime(2024, 1, 15), buffer));
        Assert.Equal("2024-01-15 10:30:00", NetezzaPipeImportExecutor.FormatDateTime(new DateTime(2024, 1, 15, 10, 30, 0), buffer));
    }

    [Fact]
    public void PipeDateTimeFormatFull_always_emits_timestamp_including_midnight()
    {
        Span<char> buffer = stackalloc char[64];
        Assert.Equal("2024-01-15 00:00:00", NetezzaPipeImportExecutor.FormatDateTimeFull(new DateTime(2024, 1, 15), buffer));
        Assert.Equal("2024-01-15 10:30:00", NetezzaPipeImportExecutor.FormatDateTimeFull(new DateTime(2024, 1, 15, 10, 30, 0), buffer));
    }

    [Fact]
    public void PipeDateFormat_emits_date_only()
    {
        Span<char> buffer = stackalloc char[64];
        Assert.Equal("2024-01-15", NetezzaPipeImportExecutor.FormatDate(new DateTime(2024, 1, 15), buffer));
        Assert.Equal("2024-01-15", NetezzaPipeImportExecutor.FormatDate(new DateTime(2024, 1, 15, 10, 30, 0), buffer));
    }

    [Fact]
    public void UsingBuilder_contains_shared_typed_and_fast_options()
    {
        string sql = NetezzaImportSql.BuildUsingClause(new NetezzaImportUsingOptions
        {
            Delimiter = "|",
            EncodingName = "UTF-8",
            MaxErrors = 4,
            NullValue = "\\N",
            TruncString = true,
            CRinString = true
        });

        Assert.Contains("DELIMITER '|'", sql, StringComparison.Ordinal);
        Assert.Contains("REMOTESOURCE 'dotnet'", sql, StringComparison.Ordinal);
        Assert.Contains("MAXERRORS 4", sql, StringComparison.Ordinal);
        Assert.Contains(@"NULLVALUE '\N'", sql, StringComparison.Ordinal);
        Assert.Contains("TRUNCSTRING", sql, StringComparison.Ordinal);
        Assert.Contains("CRINSTRING", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void UsingBuilder_always_emits_dotnet_remotesource_when_unset_or_blank()
    {
        string fromDefault = NetezzaImportSql.BuildUsingClause(new NetezzaImportUsingOptions());
        string fromBlank = NetezzaImportSql.BuildUsingClause(new NetezzaImportUsingOptions { RemoteSource = "  " });
        string fromNull = NetezzaImportSql.BuildUsingClause(new NetezzaImportUsingOptions { RemoteSource = null });

        Assert.Contains("REMOTESOURCE 'dotnet'", fromDefault, StringComparison.Ordinal);
        Assert.Contains("REMOTESOURCE 'dotnet'", fromBlank, StringComparison.Ordinal);
        Assert.Contains("REMOTESOURCE 'dotnet'", fromNull, StringComparison.Ordinal);
    }

    [Fact]
    public void UsingBuilder_preserves_empty_null_marker_and_omits_non_positive_max_rows()
    {
        string sql = NetezzaImportSql.BuildUsingClause(new NetezzaImportUsingOptions
        {
            NullValue = string.Empty,
            MaxRows = 0,
            MaxErrors = 0
        });

        Assert.Contains("NULLVALUE ''", sql, StringComparison.Ordinal);
        Assert.Contains("MAXERRORS 0", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("MAXROWS", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Typed_encoder_escapes_delimiter_and_newline()
    {
        string row = DelimitedRowEncoder.Encode(["a|b", "line\nvalue"], '|');

        Assert.Equal("a\\|b|line\\nvalue", row);
    }

    [Fact]
    public async Task Fast_reader_supports_buffered_multiline_csv()
    {
        using var input = new StringReader("id,name\n1,\"Ada\nLovelace\"\n");
        var rows = new List<IReadOnlyList<string?>>();
        await foreach (var row in FastCsvImportEngine.ReadAsync(input, new CsvImportOptions(HasHeader: false)))
            rows.Add(row);

        Assert.Equal(2, rows.Count);
        Assert.Equal("Ada\nLovelace", rows[1][1]);
    }

    [Fact]
    public async Task Fast_raw_reader_supports_filter_transform_and_header_skip()
    {
        using var input = new StringReader("id,name\n1,Ada\n2,Bob\n3,Ada\n");
        var rows = new List<string>();
        await foreach (string row in FastCsvImportEngine.ReadRawAsync(input, new FastCsvRawOptions(
                           FilterPattern: "Ada",
                           TransformPattern: "Ada",
                           TransformReplacement: "ADA")))
            rows.Add(row);

        Assert.Equal(["1,ADA", "3,ADA"], rows);
    }

    [Fact]
    public void Sanitize_escapes_tab_newline_and_backslash()
    {
        var values = System.Buffers.SearchValues.Create(['\\', '\t', '\n', '\r']);
        string sanitized = NetezzaPipeImportExecutor.Sanitize(
            "a\tb\\c\nd",
            values,
            "\\\\",
            '\t',
            "\\t",
            "\\n");

        Assert.Equal("a\\tb\\\\c\\nd", sanitized);
    }

    [Fact]
    public void BuildInsertSql_includes_using_clause()
    {
        string sql = NetezzaImportEngine.BuildInsertSql(
            "T",
            "pipe1",
            ["ID INTEGER"],
            new NetezzaImportUsingOptions { Delimiter = "\\t", MaxErrors = 0 });

        Assert.Contains("INSERT INTO T", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\\\\.\\pipe\\pipe1", sql, StringComparison.Ordinal);
        Assert.Contains("REMOTESOURCE 'dotnet'", sql, StringComparison.Ordinal);
        Assert.Contains("USING", sql, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(";", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void EncodingResolver_aliases_resolve_to_expected_encodings()
    {
        Assert.Same(Encoding.UTF8, ExportEncodingResolver.Resolve(null));
        Assert.Same(Encoding.UTF8, ExportEncodingResolver.Resolve(""));
        Assert.Same(Encoding.UTF8, ExportEncodingResolver.Resolve("utf-8"));
        Assert.Same(Encoding.UTF8, ExportEncodingResolver.Resolve("UTF8"));
        Assert.Same(Encoding.Latin1, ExportEncodingResolver.Resolve("latin1"));
        Assert.Same(Encoding.Unicode, ExportEncodingResolver.Resolve("utf-16"));
        Assert.Same(Encoding.UTF32, ExportEncodingResolver.Resolve("UTF-32"));
    }

    [Fact]
    public void EncodingResolver_codepage_and_name_support_matches_legacy_behavior()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Assert.Equal(Encoding.GetEncoding(1252), ExportEncodingResolver.Resolve("1252"));
        Assert.Equal(Encoding.GetEncoding("windows-1250"), ExportEncodingResolver.Resolve("windows-1250"));
        Assert.Same(Encoding.UTF8, ExportEncodingResolver.Resolve("   utf-8   "));
    }

    [Fact]
    public void EncodingResolver_utf8_bom_variant_has_no_preamble()
    {
        var encoding = ExportEncodingResolver.Resolve("utf8_bm");
        Assert.Empty(encoding.GetPreamble());
    }

    [Fact]
    public void EncodingResolver_newline_escapes_are_translated()
    {
        Assert.Equal(Environment.NewLine, ExportEncodingResolver.ResolveNewLine(null));
        Assert.Equal(Environment.NewLine, ExportEncodingResolver.ResolveNewLine(""));
        Assert.Equal("\r\n", ExportEncodingResolver.ResolveNewLine("\\r\\n"));
        Assert.Equal("\n", ExportEncodingResolver.ResolveNewLine("\\n"));
    }

    [Fact]
    public void JsonExportWriter_writes_row_arrays_and_null_for_dbnulls()
    {
        using var reader = new StubDataReader(
            [("id", typeof(int)), ("name", typeof(string))],
            [1, "Ada"],
            [2, null]);

        using var writer = new StringWriter();
        long count = JsonExportWriter.WriteFromDataReader(writer, reader);

        Assert.Equal(2, count);
        Assert.Equal("[[\"1\",\"Ada\"],[\"2\",null]]", writer.ToString());
    }

    [Fact]
    public void JsonExportWriter_empty_reader_writes_empty_array()
    {
        using var reader = new StubDataReader([("id", typeof(int))]);
        using var writer = new StringWriter();

        long count = JsonExportWriter.WriteFromDataReader(writer, reader);

        Assert.Equal(0, count);
        Assert.Equal("[]", writer.ToString());
    }
}