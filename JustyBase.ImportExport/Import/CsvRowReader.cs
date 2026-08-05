using System.IO.Compression;
using System.Text;
using Sylvan.Data.Csv;

namespace JustyBase.ImportExport.Import;

/// <summary>
/// Shared compressed CSV row reader used by the host import wizards.
/// Extracted from the Avalonia <c>CsvReader</c> (superset: Gzip/Zstd in addition to
/// Brotli, <c>TreatAllColumnsAsText</c>, Pesel/Regon-as-text). Hosts keep their
/// <c>ExcelReaderAbstract</c> facade and map <see cref="CsvCell"/> to host cell types.
/// </summary>
public sealed class CsvRowReader : IDisposable
{
    private readonly CsvCompression _compression;
    private readonly Encoding? _encoding;
    private bool _treatAllColumnsAsText;

    private FileStream? _originalFileStream;
    private StreamReader? _streamReader;
    private CsvDataReader? _csvReader;

    public CsvRowReader(
        CsvCompression compression = CsvCompression.None,
        Encoding? encoding = null,
        bool treatAllColumnsAsText = false)
    {
        _compression = compression;
        _encoding = encoding;
        _treatAllColumnsAsText = treatAllColumnsAsText;
    }

    public string? FilePath { get; private set; }

    public CsvCompression Compression => _compression;

    public bool TreatAllColumnsAsText
    {
        get => _treatAllColumnsAsText;
        set => _treatAllColumnsAsText = value;
    }

    public int FieldCount => _csvReader?.FieldCount ?? 0;

    public void Open(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        FilePath = path;
        _originalFileStream = File.OpenRead(path);
        var buffered = new BufferedStream(_originalFileStream);
        Stream source = _compression switch
        {
            CsvCompression.Brotli => new BrotliStream(buffered, CompressionMode.Decompress),
            CsvCompression.Gzip => new GZipStream(buffered, CompressionMode.Decompress),
            CsvCompression.Zstd => new ZstdSharp.DecompressionStream(buffered),
            _ => buffered
        };
        _streamReader = _encoding is null
            ? new StreamReader(source)
            : new StreamReader(source, _encoding);
        _csvReader = CsvDataReader.Create(_streamReader);
    }

    public bool Read() => _csvReader!.Read();

    public string GetName(int index) => _csvReader!.GetName(index);

    public string GetFieldString(int index) => _csvReader!.GetFieldSpan(index).ToString();

    public int GetFieldLength(int index) => _csvReader!.GetFieldSpan(index).Length;

    public CsvCell InferCell(int index)
    {
        var span = _csvReader!.GetFieldSpan(index);
        return CsvCellTypeResolver.Infer(span, GetName(index), _treatAllColumnsAsText);
    }

    /// <summary>0..1 read progress for seekable (uncompressed) and compressed inputs.</summary>
    public double Position
    {
        get
        {
            if (_streamReader?.BaseStream.CanSeek == true && _streamReader.BaseStream.Length > 0)
                return (double)_streamReader.BaseStream.Position / _streamReader.BaseStream.Length;
            if (_compression != CsvCompression.None && _originalFileStream is { Length: > 0 })
                return (double)_originalFileStream.Position / _originalFileStream.Length;
            return 0.5;
        }
    }

    public void Dispose()
    {
        _csvReader?.Dispose();
        _streamReader?.Dispose();
        _originalFileStream?.Dispose();
    }
}
