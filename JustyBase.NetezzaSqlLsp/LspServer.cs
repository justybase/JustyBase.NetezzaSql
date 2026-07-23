using System.Text.Json;
using JustyBase.NetezzaSqlLsp.Protocol;

namespace JustyBase.NetezzaSqlLsp;

/// <summary>JSON-RPC based LSP server for Netezza SQL.</summary>
public sealed class LspServer
{
    private readonly JsonRpcTransport _transport;
    private bool _initialized;
    private bool _shutdown;
    private readonly Dictionary<string, Func<JsonElement, string?, CancellationToken, Task>> _handlers = new();

    /// <summary>Creates a new LSP server over the given transport.</summary>
    public LspServer(JsonRpcTransport transport)
    {
        _transport = transport;
    }

    /// <summary>Marks the server as initialized (accepts all methods).</summary>
    public void SetInitialized() => _initialized = true;

    /// <summary>Registers a handler for the given method name.</summary>
    public void RegisterRequestHandler(string method, Func<JsonElement, string?, CancellationToken, Task> handler)
    {
        _handlers[method] = handler;
    }

    /// <summary>Main loop: reads JSON-RPC messages and dispatches to registered handlers.</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && !_shutdown)
        {
            using var doc = await _transport.ReadDocumentAsync(ct);
            if (doc is null) break;

            var root = doc.RootElement;

            string? id = null;
            JsonElement? idEl = null;
            if (root.TryGetProperty("id", out var idProp))
            {
                id = idProp.GetRawText();
                idEl = idProp;
            }

            string method = root.GetProperty("method").GetString() ?? "";
            bool hasId = idEl is not null;

            if (hasId)
            {
                if (!_initialized && method != "initialize")
                {
                    await SendError(id!, JsonRpcErrorCodes.ServerNotInitialized, "Server not initialized", ct);
                    continue;
                }

                if (_handlers.TryGetValue(method, out var handler))
                {
                    await handler(root, id, ct);
                }
                else
                {
                    await SendError(id!, JsonRpcErrorCodes.MethodNotFound, $"Method not found: {method}", ct);
                }
            }
            else
            {
                if (method == "exit") { _shutdown = true; break; }
                if (string.IsNullOrEmpty(method)) continue;
                if (!_initialized && method != "initialized") continue;

                if (_handlers.TryGetValue(method, out var handler))
                {
                    await handler(root, null, ct);
                }
            }
        }
    }

    /// <summary>Sends a successful JSON-RPC response preserving the original request ID type.</summary>
    public async Task SendResult(string id, object? result, CancellationToken ct)
    {
        // Preserve original ID type (number stays number, string stays string)
        JsonElement? parsedId = null;
        if (id is not null)
        {
            using var idDoc = JsonDocument.Parse(id);
            parsedId = idDoc.RootElement.Clone();
        }

        var jsonResult = result is not null
            ? JsonSerializer.SerializeToElement(result, Protocol.LspJsonContext.Default.Object)
            : null as JsonElement?;

        var response = new JsonRpcResponse("2.0", parsedId, jsonResult, null);
        await _transport.SendResponseAsync(response, ct);
    }

    /// <summary>Sends a JSON-RPC error response.</summary>
    public async Task SendError(string id, int code, string message, CancellationToken ct, JsonElement? data = null)
    {
        JsonElement? parsedId = null;
        if (id is not null)
        {
            using var idDoc = JsonDocument.Parse(id);
            parsedId = idDoc.RootElement.Clone();
        }

        var response = new JsonRpcResponse("2.0", parsedId, null, new JsonRpcError(code, message, data));
        await _transport.SendResponseAsync(response, ct);
    }

    /// <summary>Sends a JSON-RPC notification (no response expected).</summary>
    public async Task SendNotification(string method, object? paramsObj, CancellationToken ct)
    {
        await _transport.SendNotificationAsync(method, paramsObj, ct);
    }
}
