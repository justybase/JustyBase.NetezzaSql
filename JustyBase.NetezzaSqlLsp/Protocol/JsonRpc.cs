using System.Text.Json;

namespace JustyBase.NetezzaSqlLsp.Protocol;

public sealed class JsonRpcTransport : IDisposable
{
    private readonly Stream _in;
    private readonly Stream _out;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly MemoryStream _readBuffer = new();

    public JsonRpcTransport(Stream input, Stream output)
    {
        _in = input;
        _out = output;
    }

    /// <summary>Read a complete JSON-RPC message and return a disposable JsonDocument.</summary>
    public async Task<JsonDocument?> ReadDocumentAsync(CancellationToken ct)
    {
        int contentLength = 0;
        while (true)
        {
            var line = await ReadLineAsync(ct);
            if (line is null) return null;
            if (line.Length == 0) break;

            if (line.StartsWith("Content-Length: ", StringComparison.OrdinalIgnoreCase))
            {
                int.TryParse(line.AsSpan(16), out contentLength);
            }
        }

        if (contentLength <= 0) return null;

        // Read exactly contentLength bytes into a buffer
        _readBuffer.SetLength(contentLength);
        _readBuffer.Position = 0;
        int totalRead = 0;
        while (totalRead < contentLength)
        {
            var remaining = contentLength - totalRead;
            var buf = _readBuffer.GetBuffer();
            var read = await _in.ReadAsync(buf.AsMemory(totalRead, remaining), ct);
            if (read <= 0) return null;
            totalRead += read;
        }

        // Parse from the buffer
        var jsonStr = System.Text.Encoding.UTF8.GetString(_readBuffer.GetBuffer(), 0, contentLength);
        return JsonDocument.Parse(jsonStr);
    }

    public async Task SendResponseAsync(JsonRpcResponse response, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(response, LspJsonContext.Default.JsonRpcResponse);
        await WriteMessageAsync(json, ct);
    }

    public async Task SendNotificationAsync(string method, object? paramsObj, CancellationToken ct)
    {
        JsonElement? paramsEl = null;
        if (paramsObj is not null)
        {
            paramsEl = JsonSerializer.SerializeToElement(paramsObj, LspJsonContext.Default.Object);
        }
        var notification = new JsonRpcNotification("2.0", method, paramsEl);
        var json = JsonSerializer.Serialize(notification, LspJsonContext.Default.JsonRpcNotification);
        await WriteMessageAsync(json, ct);
    }

    private async Task<string?> ReadLineAsync(CancellationToken ct)
    {
        var sb = new List<byte>();
        while (true)
        {
            int b = await _in.ReadByteAsync(ct);
            if (b < 0) return null;
            if (b == '\n')
            {
                // Strip trailing \r
                if (sb.Count > 0 && sb[^1] == '\r')
                    sb.RemoveAt(sb.Count - 1);
                return System.Text.Encoding.UTF8.GetString(sb.ToArray());
            }
            sb.Add((byte)b);
        }
    }

    private async Task WriteMessageAsync(string json, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            var header = $"Content-Length: {System.Text.Encoding.UTF8.GetByteCount(json)}\r\n\r\n";
            var headerBytes = System.Text.Encoding.UTF8.GetBytes(header);
            var jsonBytes = System.Text.Encoding.UTF8.GetBytes(json);

            await _out.WriteAsync(headerBytes, ct);
            await _out.WriteAsync(jsonBytes, ct);
            await _out.FlushAsync(ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public void Dispose()
    {
        _in.Dispose();
        _out.Dispose();
        _writeLock.Dispose();
        _readBuffer.Dispose();
    }
}

file static class StreamExtensions
{
    public static async Task<int> ReadByteAsync(this Stream stream, CancellationToken ct)
    {
        byte[] buf = new byte[1];
        var read = await stream.ReadAsync(buf.AsMemory(0, 1), ct);
        return read > 0 ? buf[0] : -1;
    }
}
