using JustyBase.NetezzaSqlLsp;
using JustyBase.NetezzaSqlLsp.Protocol;
using JustyBase.NetezzaSqlLsp.Handlers;
using JustyBase.NetezzaSqlLsp.Workspace;
using JustyBase.NetezzaSqlLsp.Services;
using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.NetezzaSqlParser.Caching;
using JustyBase.NetezzaSqlParser.Visitor;
using JustyBase.NetezzaSqlParser.Ast;
using System.Text.Json;

Console.InputEncoding = System.Text.Encoding.UTF8;
Console.OutputEncoding = System.Text.Encoding.UTF8;

var input = Console.OpenStandardInput();
var output = Console.OpenStandardOutput();

var transport = new JsonRpcTransport(input, output);
var server = new LspServer(transport);
var docs = new DocumentManager();
var schema = new InMemorySchemaProvider();
using var parsingCoordinator = new DocumentParsingCoordinator();
var semanticClassifier = new NzSemanticTokenClassifier(schema, parsingCoordinator);
using var shutdownCts = new CancellationTokenSource();

// Handle Ctrl+C gracefully
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    shutdownCts.Cancel();
};

// ---- Helper: re-lint all open documents after schema sync ----
async Task ReLintAllAsync(CancellationToken ct)
{
    var tasks = new List<Task>();
    foreach (var uri in docs.GetAllUris())
    {
        tasks.Add(TextDocumentHandlers.PublishDiagnosticsAsync(server, docs, schema, uri, ct));
    }
    await Task.WhenAll(tasks);
}

// ---- Lifecycle ----
server.RegisterRequestHandler("initialize", (root, id, ct) =>
    LifecycleHandlers.HandleInitialize(server, root, id!, ct));
server.RegisterRequestHandler("shutdown", (_, id, ct) =>
    LifecycleHandlers.HandleShutdown(server, id!, ct));
server.RegisterRequestHandler("initialized", (_, _, _) =>
{
    LifecycleHandlers.HandleInitialized(server);
    return Task.CompletedTask;
});

// ---- Text Document Sync ----
server.RegisterRequestHandler("textDocument/didOpen", (root, _, ct) =>
    TextDocumentHandlers.HandleDidOpen(server, docs, schema, root, ct));
server.RegisterRequestHandler("textDocument/didChange", (root, _, ct) =>
    TextDocumentHandlers.HandleDidChange(server, docs, schema, root, ct));
server.RegisterRequestHandler("textDocument/didClose", (root, _, ct) =>
    TextDocumentHandlers.HandleDidClose(server, docs, root, ct));

// ---- Schema Sync (custom JustyBase protocol) ----
server.RegisterRequestHandler("justy/syncSchema", async (root, id, ct) =>
{
    try
    {
        var p = root.GetProperty("params");
        var database = p.GetProperty("database").GetString() ?? "";
        var schemaName = p.GetProperty("schema").GetString() ?? "";
        var tablesEl = p.GetProperty("tables");

        foreach (var tableEl in tablesEl.EnumerateArray())
        {
            var tableName = tableEl.GetProperty("name").GetString() ?? "";
            var columns = new List<ColumnInfo>();

            if (tableEl.TryGetProperty("columns", out var colsEl))
            {
                foreach (var colEl in colsEl.EnumerateArray())
                {
                    var colName = colEl.GetProperty("name").GetString() ?? "";
                    columns.Add(new ColumnInfo(colName));
                }
            }

            schema.AddTable(new TableInfo(
                tableName,
                Schema: string.IsNullOrEmpty(schemaName) ? null : schemaName,
                Database: string.IsNullOrEmpty(database) ? null : database,
                Columns: columns.Count > 0 ? columns.ToArray() : null
            ));
        }

        await server.SendResult(id!, "ok", ct);
        await ReLintAllAsync(ct);
    }
    catch (Exception ex)
    {
        await server.SendError(id!, JsonRpcErrorCodes.InternalError, $"Schema sync error: {ex.Message}", ct);
    }
});

// ---- Completion ----
server.RegisterRequestHandler("textDocument/completion", async (root, id, ct) =>
{
    try
    {
        var p = root.GetProperty("params");
        var uri = p.GetProperty("textDocument").GetProperty("uri").GetString() ?? "";
        var pos = p.GetProperty("position");
        var line = pos.GetProperty("line").GetInt32();
        var character = pos.GetProperty("character").GetInt32();

        var text = docs.GetText(uri);
        if (text is null)
        {
            await server.SendResult(id!, new CompletionList(false, null), ct);
            return;
        }

        var completions = CompletionService.GetCompletions(text, line, character, schema);
        await server.SendResult(id!, completions, ct);
    }
    catch (Exception ex)
    {
        await server.SendError(id!, JsonRpcErrorCodes.InternalError, $"Completion error: {ex.Message}", ct);
    }
});

// ---- Semantic Tokens ----
server.RegisterRequestHandler("textDocument/semanticTokens/full", async (root, id, ct) =>
{
    try
    {
        var p = root.GetProperty("params");
        var uri = p.GetProperty("textDocument").GetProperty("uri").GetString() ?? "";
        var text = docs.GetText(uri) ?? "";

        var result = SemanticTokensService.GetSemanticTokens(text, semanticClassifier, uri);
        await server.SendResult(id!, result, ct);
    }
    catch (Exception ex)
    {
        await server.SendError(id!, JsonRpcErrorCodes.InternalError, $"Semantic tokens error: {ex.Message}", ct);
    }
});

// ---- Hover ----
server.RegisterRequestHandler("textDocument/hover", async (root, id, ct) =>
{
    try
    {
        var p = root.GetProperty("params");
        var uri = p.GetProperty("textDocument").GetProperty("uri").GetString() ?? "";
        var pos = p.GetProperty("position");
        var line = pos.GetProperty("line").GetInt32();
        var character = pos.GetProperty("character").GetInt32();

        var text = docs.GetText(uri);
        if (text is null)
        {
            await server.SendResult(id!, null, ct);
            return;
        }

        var result = HoverService.GetHover(text, line, character, schema);
        await server.SendResult(id!, result, ct);
    }
    catch (Exception ex)
    {
        await server.SendError(id!, JsonRpcErrorCodes.InternalError, $"Hover error: {ex.Message}", ct);
    }
});

// ---- Definition / References / DocumentSymbol ----
server.RegisterRequestHandler("textDocument/definition", async (root, id, ct) =>
{
    try
    {
        var p = root.GetProperty("params");
        var uri = p.GetProperty("textDocument").GetProperty("uri").GetString() ?? "";
        var pos = p.GetProperty("position");
        var line = pos.GetProperty("line").GetInt32();
        var character = pos.GetProperty("character").GetInt32();

        var text = docs.GetText(uri);
        var result = text is null
            ? null
            : DefinitionService.GetDefinitions(text, line, character, uri);
        await server.SendResult(id!, result, ct);
    }
    catch (Exception ex)
    {
        await server.SendError(id!, JsonRpcErrorCodes.InternalError, $"Definition error: {ex.Message}", ct);
    }
});
server.RegisterRequestHandler("textDocument/references", async (root, id, ct) =>
{
    try
    {
        var p = root.GetProperty("params");
        var uri = p.GetProperty("textDocument").GetProperty("uri").GetString() ?? "";
        var pos = p.GetProperty("position");
        var line = pos.GetProperty("line").GetInt32();
        var character = pos.GetProperty("character").GetInt32();
        var includeDeclaration = p.TryGetProperty("context", out var ctx) && ctx.GetProperty("includeDeclaration").GetBoolean();

        var text = docs.GetText(uri);
        var result = text is null
            ? Array.Empty<Location>()
            : ReferencesService.GetReferences(text, line, character, uri, includeDeclaration);
        await server.SendResult(id!, result, ct);
    }
    catch (Exception ex)
    {
        await server.SendError(id!, JsonRpcErrorCodes.InternalError, $"References error: {ex.Message}", ct);
    }
});
server.RegisterRequestHandler("textDocument/documentSymbol", async (root, id, ct) =>
{
    try
    {
        var p = root.GetProperty("params");
        var uri = p.GetProperty("textDocument").GetProperty("uri").GetString() ?? "";
        var text = docs.GetText(uri);
        var result = text is null
            ? Array.Empty<SymbolInformation>()
            : DocumentSymbolService.GetDocumentSymbols(text);
        await server.SendResult(id!, result, ct);
    }
    catch (Exception ex)
    {
        await server.SendError(id!, JsonRpcErrorCodes.InternalError, $"Document symbol error: {ex.Message}", ct);
    }
});

// ---- Signature Help ----
server.RegisterRequestHandler("textDocument/signatureHelp", async (root, id, ct) =>
{
    try
    {
        var p = root.GetProperty("params");
        var uri = p.GetProperty("textDocument").GetProperty("uri").GetString() ?? "";
        var pos = p.GetProperty("position");
        var line = pos.GetProperty("line").GetInt32();
        var character = pos.GetProperty("character").GetInt32();

        var text = docs.GetText(uri);
        if (text is null)
        {
            await server.SendResult(id!, null, ct);
            return;
        }

        var result = SignatureHelpService.GetSignatureHelp(text, line, character);
        await server.SendResult(id!, result, ct);
    }
    catch (Exception ex)
    {
        await server.SendError(id!, JsonRpcErrorCodes.InternalError, $"Signature help error: {ex.Message}", ct);
    }
});

// ---- Rename ----
server.RegisterRequestHandler("textDocument/prepareRename", async (root, id, ct) =>
{
    try
    {
        var p = root.GetProperty("params");
        var uri = p.GetProperty("textDocument").GetProperty("uri").GetString() ?? "";
        var pos = p.GetProperty("position");
        var line = pos.GetProperty("line").GetInt32();
        var character = pos.GetProperty("character").GetInt32();

        var text = docs.GetText(uri);
        if (text is null)
        {
            await server.SendResult(id!, null, ct);
            return;
        }

        var result = RenameService.PrepareRename(text, line, character);
        await server.SendResult(id!, result, ct);
    }
    catch (Exception ex)
    {
        await server.SendError(id!, JsonRpcErrorCodes.InternalError, $"Prepare rename error: {ex.Message}", ct);
    }
});
server.RegisterRequestHandler("textDocument/rename", async (root, id, ct) =>
{
    try
    {
        var p = root.GetProperty("params");
        var uri = p.GetProperty("textDocument").GetProperty("uri").GetString() ?? "";
        var pos = p.GetProperty("position");
        var line = pos.GetProperty("line").GetInt32();
        var character = pos.GetProperty("character").GetInt32();
        var newName = p.GetProperty("newName").GetString() ?? "";

        var text = docs.GetText(uri);
        if (text is null)
        {
            await server.SendResult(id!, null, ct);
            return;
        }

        var result = RenameService.Rename(text, line, character, newName, uri);
        await server.SendResult(id!, result, ct);
    }
    catch (Exception ex)
    {
        await server.SendError(id!, JsonRpcErrorCodes.InternalError, $"Rename error: {ex.Message}", ct);
    }
});

try
{
    await server.RunAsync(shutdownCts.Token);
}
catch (OperationCanceledException) { /* graceful shutdown */ }
catch (Exception ex)
{
    await Console.Error.WriteLineAsync($"LSP server error: {ex.Message}");
}
