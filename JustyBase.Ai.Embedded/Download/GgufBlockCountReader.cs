using System.Text;

namespace JustyBase.Ai.Embedded.Download;

/// <summary>
/// Reads <c>llama.block_count</c> (the model's layer count) from a GGUF header without
/// loading the model. Returns null when the file is not a parseable GGUF or lacks the key.
/// </summary>
public static class GgufBlockCountReader
{
    public static int? Read(string? modelPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
        {
            return null;
        }

        try
        {
            using var stream = new FileStream(modelPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);

            Span<byte> magic = stackalloc byte[4];
            if (reader.Read(magic) != 4
                || magic[0] != (byte)'G'
                || magic[1] != (byte)'G'
                || magic[2] != (byte)'U'
                || magic[3] != (byte)'F')
            {
                return null;
            }

            _ = reader.ReadUInt32(); // format version
            _ = reader.ReadUInt64(); // tensor_count
            var kvCount = reader.ReadUInt64();

            for (ulong i = 0; i < kvCount; i++)
            {
                var key = ReadString(reader);
                var type = reader.ReadUInt32();
                // The metadata key is "{architecture}.block_count" (e.g. llama.block_count,
                // qwen35.block_count), so match any architecture by the suffix.
                if (key.EndsWith(".block_count", StringComparison.Ordinal))
                {
                    return ReadNumeric(reader, type);
                }

                SkipValue(reader, type);
            }

            return null;
        }
        catch (EndOfStreamException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static string ReadString(BinaryReader reader)
    {
        var length = reader.ReadUInt64();
        if (length > 1_000_000)
        {
            throw new EndOfStreamException("Unreasonable GGUF string length.");
        }

        var bytes = reader.ReadBytes((int)length);
        return Encoding.UTF8.GetString(bytes);
    }

    private static int ReadNumeric(BinaryReader reader, uint type) => type switch
    {
        4 => (int)reader.ReadUInt32(),
        5 => reader.ReadInt32(),
        6 => (int)reader.ReadSingle(),
        10 => (int)reader.ReadUInt64(),
        11 => (int)reader.ReadInt64(),
        12 => (int)reader.ReadDouble(),
        _ => throw new EndOfStreamException("llama.block_count uses an unsupported GGUF type."),
    };

    private static void SkipValue(BinaryReader reader, uint type)
    {
        switch (type)
        {
            case 0 or 1 or 7:
                reader.BaseStream.Seek(1, SeekOrigin.Current);
                break;
            case 2 or 3:
                reader.BaseStream.Seek(2, SeekOrigin.Current);
                break;
            case 4 or 5 or 6:
                reader.BaseStream.Seek(4, SeekOrigin.Current);
                break;
            case 8:
                _ = ReadString(reader);
                break;
            case 10 or 11 or 12:
                reader.BaseStream.Seek(8, SeekOrigin.Current);
                break;
            case 9:
                {
                    var elementType = reader.ReadUInt32();
                    var count = reader.ReadUInt64();
                    for (ulong i = 0; i < count; i++)
                    {
                        SkipValue(reader, elementType);
                    }

                    break;
                }
            default:
                throw new EndOfStreamException("Unknown GGUF value type.");
        }
    }
}
