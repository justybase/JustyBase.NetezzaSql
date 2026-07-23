using System.Text;
using System.Text.Json;
using JustyBase.NetezzaSqlLsp;
using JustyBase.NetezzaSqlLsp.Handlers;
using JustyBase.NetezzaSqlLsp.Protocol;
using JustyBase.NetezzaSqlLsp.Workspace;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.NetezzaSql.Tests;

public sealed class LspHandlerCoverageTests
{
    [Fact]
    public async Task TextDocumentHandlers_UpdateWorkspaceAndPublishDiagnostics()
    {
        await using var input = new MemoryStream();
        await using var output = new MemoryStream();
        using var transport = new JsonRpcTransport(input, output);
        var server = new LspServer(transport);
        var documents = new DocumentManager();
        var schema = new InMemorySchemaProvider();
        const string uri = "file:///handler.sql";

        using var open = JsonDocument.Parse("""{"params":{"textDocument":{"uri":"file:///handler.sql","version":1,"text":"SELECT * FROM missing"}}}""");
        await TextDocumentHandlers.HandleDidOpen(server, documents, schema, open.RootElement, CancellationToken.None);
        Assert.Equal("SELECT * FROM missing", documents.GetText(uri));

        using var change = JsonDocument.Parse("""{"params":{"textDocument":{"uri":"file:///handler.sql","version":2},"contentChanges":[{"text":"SELECT 1"}]}}""");
        await TextDocumentHandlers.HandleDidChange(server, documents, schema, change.RootElement, CancellationToken.None);
        Assert.Equal("SELECT 1", documents.GetText(uri));

        using var close = JsonDocument.Parse("""{"params":{"textDocument":{"uri":"file:///handler.sql"}}}""");
        await TextDocumentHandlers.HandleDidClose(server, documents, close.RootElement, CancellationToken.None);
        Assert.False(documents.IsOpen(uri));
        Assert.Contains("textDocument/publishDiagnostics", Encoding.UTF8.GetString(output.ToArray()), StringComparison.Ordinal);
    }
}
