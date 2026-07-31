using System.IO.Compression;
using System.Text;

namespace JustyBase.ImportExport.Export;

public enum SharedCompressionKind
{
    None,
    Gzip,
    Zip
}

/// <summary>
/// Opens a <see cref="StreamWriter"/> over gzip/zip (BCL) for CSV/Parquet export.
/// Host-specific codecs (LZ4, Brotli, Zstd) stay in the host façade.
/// </summary>
public static class CompressedExportStreams
{
    public sealed record OpenedExport(
        StreamWriter Writer,
        string FinalFilePath,
        Action Dispose);

    public static OpenedExport Open(
        string baseFilePath,
        SharedCompressionKind kind,
        Encoding? encoding = null)
    {
        encoding ??= new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        return kind switch
        {
            SharedCompressionKind.Gzip => OpenGzip(baseFilePath, encoding),
            SharedCompressionKind.Zip => OpenZip(baseFilePath, encoding),
            _ => OpenPlain(baseFilePath, encoding)
        };
    }

    private static OpenedExport OpenPlain(string path, Encoding encoding)
    {
        var writer = new StreamWriter(path, append: false, encoding: encoding);
        return new OpenedExport(writer, path, writer.Dispose);
    }

    private static OpenedExport OpenGzip(string baseFilePath, Encoding encoding)
    {
        string finalPath = baseFilePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
            ? baseFilePath
            : baseFilePath + ".gz";
        var fileStream = File.Open(finalPath, FileMode.Create, FileAccess.Write, FileShare.None);
        var gzip = new GZipStream(fileStream, CompressionLevel.Optimal);
        var writer = new StreamWriter(gzip, encoding);
        return new OpenedExport(writer, finalPath, () =>
        {
            writer.Dispose();
            gzip.Dispose();
            fileStream.Dispose();
        });
    }

    private static OpenedExport OpenZip(string baseFilePath, Encoding encoding)
    {
        string finalPath = baseFilePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            ? baseFilePath
            : baseFilePath + ".zip";
        string entryName = Path.GetFileName(
            baseFilePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                ? Path.GetFileNameWithoutExtension(baseFilePath)
                : baseFilePath);
        var helperStream = new FileStream(finalPath, FileMode.Create);
        var archive = new ZipArchive(helperStream, ZipArchiveMode.Create, leaveOpen: true);
        var entry = archive.CreateEntry(entryName);
        Stream openedEntry = entry.Open();
        var writer = new StreamWriter(openedEntry, encoding);
        return new OpenedExport(writer, finalPath, () =>
        {
            writer.Dispose();
            openedEntry.Dispose();
            archive.Dispose();
            helperStream.Dispose();
        });
    }
}
