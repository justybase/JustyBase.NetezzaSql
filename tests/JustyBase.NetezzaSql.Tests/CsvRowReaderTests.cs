using JustyBase.ImportExport.Import;
using System.IO.Compression;
using System.Text;

namespace JustyBase.NetezzaSql.Tests;

public sealed class CsvRowReaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "justybase-csvrow-tests-" + Guid.NewGuid().ToString("N"));

    public CsvRowReaderTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private string WriteFile(string content)
    {
        string path = Path.Combine(_dir, Path.GetRandomFileName() + ".csv");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Reader_plain_csv_returns_header_and_typed_rows()
    {
        using var reader = new CsvRowReader();
        reader.Open(WriteFile("id,name,amount\n1,Ada,3.5\n2,Bob,\n"));

        Assert.Equal(3, reader.FieldCount);
        Assert.Equal("id", reader.GetName(0));

        Assert.True(reader.Read());
        Assert.Equal(CsvCellKind.Int64, reader.InferCell(0).Kind);
        Assert.Equal(CsvCellKind.String, reader.InferCell(1).Kind);
        Assert.Equal("Ada", reader.InferCell(1).StringValue);
        Assert.Equal(CsvCellKind.Double, reader.InferCell(2).Kind);
        Assert.Equal(3.5m, reader.InferCell(2).DecimalValue);

        Assert.True(reader.Read());
        Assert.Equal(CsvCellKind.Int64, reader.InferCell(0).Kind);
        Assert.Equal(CsvCellKind.String, reader.InferCell(1).Kind);
        Assert.Equal(CsvCellKind.Null, reader.InferCell(2).Kind);

        Assert.False(reader.Read());
    }

    [Fact]
    public void Reader_infers_boolean_and_datetime()
    {
        using var reader = new CsvRowReader();
        reader.Open(WriteFile("active,created\ntrue,2024-01-15\nfalse,not-a-date\n"));

        Assert.True(reader.Read());
        Assert.Equal(CsvCellKind.Boolean, reader.InferCell(0).Kind);
        Assert.Equal(CsvCellKind.DateTime, reader.InferCell(1).Kind);

        Assert.True(reader.Read());
        Assert.Equal(CsvCellKind.Boolean, reader.InferCell(0).Kind);
        Assert.Equal(CsvCellKind.String, reader.InferCell(1).Kind);
    }

    [Fact]
    public void Reader_treat_all_columns_as_text_forces_strings()
    {
        using var reader = new CsvRowReader(treatAllColumnsAsText: true);
        reader.Open(WriteFile("id,amount\n1,3.5\n"));

        Assert.True(reader.Read());
        Assert.Equal(CsvCellKind.String, reader.InferCell(0).Kind);
        Assert.Equal(CsvCellKind.String, reader.InferCell(1).Kind);
    }

    [Fact]
    public void Reader_keeps_pesel_and_regon_columns_as_text()
    {
        using var reader = new CsvRowReader();
        reader.Open(WriteFile("Pesel,Regon,id\n12345678901,123456789,5\n"));

        Assert.True(reader.Read());
        Assert.Equal(CsvCellKind.String, reader.InferCell(0).Kind);
        Assert.Equal(CsvCellKind.String, reader.InferCell(1).Kind);
        Assert.Equal(CsvCellKind.Int64, reader.InferCell(2).Kind);
    }

    [Fact]
    public void Reader_supports_multiline_quoted_field()
    {
        using var reader = new CsvRowReader();
        reader.Open(WriteFile("name,note\nAda,\"line1\nline2\"\n"));

        Assert.True(reader.Read());
        Assert.Equal("line1\nline2", reader.InferCell(1).StringValue);
    }

    [Theory]
    [InlineData(CsvCompression.Gzip)]
    [InlineData(CsvCompression.Brotli)]
    public void Reader_reads_gzip_and_brotli_compressed_csv(CsvCompression compression)
    {
        string payload = "id,name\n1,Ada\n2,Bob\n";
        string path = Path.Combine(_dir, Path.GetRandomFileName() + ".csv");
        using (var file = File.Create(path))
        {
            if (compression == CsvCompression.Gzip)
            {
                using var gz = new GZipStream(file, CompressionLevel.Optimal, leaveOpen: true);
                gz.Write(Encoding.UTF8.GetBytes(payload));
            }
            else
            {
                using var br = new BrotliStream(file, CompressionLevel.Optimal, leaveOpen: true);
                br.Write(Encoding.UTF8.GetBytes(payload));
            }
        }

        using var reader = new CsvRowReader(compression);
        reader.Open(path);

        Assert.True(reader.Read());
        Assert.Equal("Ada", reader.InferCell(1).StringValue);
        Assert.True(reader.Read());
        Assert.Equal(2, reader.InferCell(0).Int64Value);
        Assert.False(reader.Read());
    }

    [Fact]
    public void Reader_reads_zstd_compressed_csv()
    {
        string payload = "id,name\n1,Ada\n2,Bob\n";
        string path = Path.Combine(_dir, Path.GetRandomFileName() + ".csv");
        using (var file = File.Create(path))
        {
            using var zstdStream = new ZstdSharp.CompressionStream(file);
            byte[] bytes = Encoding.UTF8.GetBytes(payload);
            zstdStream.Write(bytes, 0, bytes.Length);
        }

        using var reader = new CsvRowReader(CsvCompression.Zstd);
        reader.Open(path);

        Assert.True(reader.Read());
        Assert.Equal("Ada", reader.InferCell(1).StringValue);
        Assert.True(reader.Read());
        Assert.Equal(2, reader.InferCell(0).Int64Value);
        Assert.False(reader.Read());
    }

    [Fact]
    public void Resolver_matches_legacy_codepage_numeric_behavior()
    {
        Assert.Equal(CsvCellKind.Double, CsvCellTypeResolver.Infer("1.5", "c", treatAllColumnsAsText: false).Kind);
        Assert.Equal(CsvCellKind.Double, CsvCellTypeResolver.Infer("-2,5", "c", false).Kind);
        Assert.Equal(CsvCellKind.String, CsvCellTypeResolver.Infer("0", "c", false).Kind);
        Assert.Equal(CsvCellKind.String, CsvCellTypeResolver.Infer("abc", "c", false).Kind);
        Assert.Equal(CsvCellKind.Null, CsvCellTypeResolver.Infer("", "c", false).Kind);
    }
}
