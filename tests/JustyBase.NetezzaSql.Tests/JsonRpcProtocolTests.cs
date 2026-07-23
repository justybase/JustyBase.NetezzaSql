using System.Text;
using System.Text.Json;
using JustyBase.NetezzaSqlLsp;
using JustyBase.NetezzaSqlLsp.Handlers;
using JustyBase.NetezzaSqlLsp.Protocol;

namespace JustyBase.NetezzaSql.Tests;

public sealed class JsonRpcProtocolTests
{
    [Fact]
    public async Task Transport_ReadsUtf8FramedDocument()
    {
        const string json = "{\"jsonrpc\":\"2.0\",\"method\":\"test\",\"params\":{\"value\":\"Łódź\"}}";
        await using var input = new MemoryStream(Encoding.UTF8.GetBytes(Frame(json)));
        await using var output = new MemoryStream();
        using var transport = new JsonRpcTransport(input, output);

        using var document = await transport.ReadDocumentAsync(CancellationToken.None);

        Assert.NotNull(document);
        Assert.Equal("test", document.RootElement.GetProperty("method").GetString());
        Assert.Equal("Łódź", document.RootElement.GetProperty("params").GetProperty("value").GetString());
    }

    [Fact]
    public async Task Server_EnforcesLifecycle_AndReturnsFramedResponses()
    {
        var inputText = string.Concat(
            Frame("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"test/echo\"}"),
            Frame("{\"jsonrpc\":\"2.0\",\"id\":\"initialize\",\"method\":\"initialize\"}"),
            Frame("{\"jsonrpc\":\"2.0\",\"method\":\"initialized\"}"),
            Frame("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"test/echo\",\"params\":{\"message\":\"ok\"}}"),
            Frame("{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"shutdown\"}"),
            Frame("{\"jsonrpc\":\"2.0\",\"method\":\"exit\"}"));
        await using var input = new MemoryStream(Encoding.UTF8.GetBytes(inputText));
        await using var output = new MemoryStream();
        using var transport = new JsonRpcTransport(input, output);
        var server = new LspServer(transport);
        server.RegisterRequestHandler("initialize", (root, id, ct) => LifecycleHandlers.HandleInitialize(server, root, id!, ct));
        server.RegisterRequestHandler("initialized", (_, _, _) =>
        {
            LifecycleHandlers.HandleInitialized(server);
            return Task.CompletedTask;
        });
        server.RegisterRequestHandler("shutdown", (_, id, ct) => LifecycleHandlers.HandleShutdown(server, id!, ct));
        server.RegisterRequestHandler("test/echo", async (root, id, ct) =>
        {
            var message = root.GetProperty("params").GetProperty("message").GetString();
            await server.SendResult(id!, message, ct);
        });

        await server.RunAsync(CancellationToken.None);

        var responseText = Encoding.UTF8.GetString(output.ToArray());
        Assert.Contains("Content-Length:", responseText, StringComparison.Ordinal);
        Assert.Contains("\"code\":-32002", responseText, StringComparison.Ordinal);
        Assert.Contains("\"id\":\"initialize\"", responseText, StringComparison.Ordinal);
        Assert.Contains("\"id\":2", responseText, StringComparison.Ordinal);
        Assert.Contains("\"result\":\"ok\"", responseText, StringComparison.Ordinal);
        Assert.Contains("\"id\":3", responseText, StringComparison.Ordinal);
    }

    private static string Frame(string json) =>
        $"Content-Length: {Encoding.UTF8.GetByteCount(json)}\r\n\r\n{json}";
}
