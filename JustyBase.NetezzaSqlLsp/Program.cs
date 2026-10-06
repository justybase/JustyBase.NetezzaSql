using JustyBase.NetezzaSqlLsp;
using JustyBase.NetezzaSqlLsp.Protocol;
using JustyBase.NetezzaSqlLsp.Handlers;
using JustyBase.NetezzaSqlLsp.Workspace;
using JustyBase.NetezzaSqlLsp.Services;
using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.NetezzaSqlParser.Caching;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Visitor;
using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Linter;
using System.Text.Json;

Console.InputEncoding = System.Text.Encoding.UTF8;
Console.OutputEncoding = System.Text.Encoding.UTF8;

// --dialect netezza|oracle|db2|mssql|mysql|postgresql|access selects the default SQL dialect; clients
// can switch it at runtime with the justy/setDialect request.
// Accepts both --dialect=oracle and --dialect oracle (likewise db2/mssql/mysql/postgresql/access).
var dialect = LspDialectArgs.Parse(args);

var input = Console.OpenStandardInput();
var output = Console.OpenStandardOutput();

var transport = new JsonRpcTransport(input, output);
var server = new LspServer(transport);
var docs = new DocumentManager();
var schema = new InMemorySchemaProvider();
using var parsingCoordinator = new DocumentParsingCoordinator();
using var lintCoordinator = new LintCoordinator(parsingCoordinator);
NzSemanticTokenClassifier semanticClassifier = new(schema, parsingCoordinator, dialect);
using var shutdownCts = new CancellationTokenSource();

// Handle Ctrl+C gracefully
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    shutdownCts.Cancel();
};

// ---- Helper: re-lint all open documents after schema/dialect changes ----
async Task ReLintAllAsync(CancellationToken ct)
{
    var tasks = new List<Task>();
    foreach (var uri in docs.GetAllUris())
    {
        tasks.Add(TextDocumentHandlers.PublishDiagnosticsAsync(server, docs, schema, dialect, lintCoordinator, uri, ct));
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

// ---- Dialect (custom JustyBase protocol) ----
server.RegisterRequestHandler("justy/setDialect", async (root, id, ct) =>
{
    try
    {
        var value = root.GetProperty("params").GetProperty("dialect").GetString() ?? "netezza";
        dialect = DialectRuntime.ParseName(value);
        lintCoordinator.Clear();
        parsingCoordinator.Clear();
        semanticClassifier = new NzSemanticTokenClassifier(schema, parsingCoordinator, dialect);
        await server.SendResult(id!, "ok", ct);
        await ReLintAllAsync(ct);
    }
    catch (Exception ex)
    {
        await server.SendError(id!, JsonRpcErrorCodes.InternalError, $"Set dialect error: {ex.Message}", ct);
    }
});

// ---- Text Document Sync ----
server.RegisterRequestHandler("textDocument/didOpen", (root, _, ct) =>
    TextDocumentHandlers.HandleDidOpen(server, docs, schema, dialect, lintCoordinator, root, ct));
server.RegisterRequestHandler("textDocument/didChange", (root, _, ct) =>
    TextDocumentHandlers.HandleDidChange(server, docs, schema, dialect, lintCoordinator, root, ct));
server.RegisterRequestHandler("textDocument/didClose", (root, _, ct) =>
    TextDocumentHandlers.HandleDidClose(server, docs, lintCoordinator, root, ct));

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
                    var dataType = colEl.TryGetProperty("dataType", out var dataTypeEl)
                        && dataTypeEl.ValueKind == JsonValueKind.String
                        ? dataTypeEl.GetString()
                        : null;
                    columns.Add(new ColumnInfo(colName, DataType: dataType));
                }
            }

            var tableSchema = string.IsNullOrEmpty(schemaName) ? null : schemaName;
            var tableDatabase = string.IsNullOrEmpty(database) ? null : database;
            schema.AddTable(new TableInfo(
                tableName,
                Schema: tableSchema,
                Database: tableDatabase,
                Columns: columns.Count > 0 ? columns.ToArray() : null
            ));

            var relations = new List<ForeignKeyRelation>();
            if (tableEl.TryGetProperty("foreignKeys", out var foreignKeysEl)
                && foreignKeysEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var fkEl in foreignKeysEl.EnumerateArray())
                {
                    var fkColumns = ReadStringArray(fkEl, "columns");
                    var referencedTable = fkEl.TryGetProperty("referencedTable", out var referencedEl)
                        ? referencedEl.GetString() ?? ""
                        : "";
                    var referencedColumns = ReadStringArray(fkEl, "referencedColumns");

                    if (fkColumns.Length == 0 || referencedColumns.Length == 0 || string.IsNullOrEmpty(referencedTable))
                        continue;

                    var referencedSchema = fkEl.TryGetProperty("referencedSchema", out var referencedSchemaEl)
                        ? referencedSchemaEl.GetString()
                        : null;
                    var referencedDatabase = fkEl.TryGetProperty("referencedDatabase", out var referencedDatabaseEl)
                        ? referencedDatabaseEl.GetString()
                        : null;
                    relations.Add(new ForeignKeyRelation(
                        fkColumns,
                        referencedTable,
                        referencedColumns,
                        ReferencedSchema: referencedSchema,
                        ReferencedDatabase: referencedDatabase));
                }

            }

            // Each syncSchema request is a full table snapshot. Missing or empty
            // foreignKeys therefore clears old relations instead of leaving stale JOIN hints.
            schema.ReplaceForeignKeys(tableDatabase, tableSchema, tableName, relations);
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
        int? triggerKind = null;
        if (p.TryGetProperty("context", out var completionContext)
            && completionContext.ValueKind == JsonValueKind.Object
            && completionContext.TryGetProperty("triggerKind", out var triggerKindValue)
            && triggerKindValue.TryGetInt32(out var parsedTriggerKind))
        {
            triggerKind = parsedTriggerKind;
        }

        var text = docs.GetText(uri);
        if (text is null)
        {
            await server.SendResult(id!, new CompletionList(false, null), ct);
            return;
        }

        // No live-database word-list provider is registered today; pass one to
        // CompletionService.GetCompletions to merge DB word-list items (the
        // ISqlDbWordListProvider headless seam).
        var completions = await CompletionService.GetCompletions(
            text,
            line,
            character,
            schema,
            dialect,
            cancellationToken: ct,
            coordinator: parsingCoordinator,
            documentUri: uri,
            triggerKind: triggerKind);
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

        var result = SemanticTokensService.GetSemanticTokens(text, semanticClassifier, uri);        await server.SendResult(id!, result, ct);
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

        var result = HoverService.GetHover(text, line, character, schema, dialect);
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
            : DefinitionService.GetDefinitions(text, line, character, uri, dialect);
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
            : ReferencesService.GetReferences(text, line, character, uri, includeDeclaration, dialect);
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
            : DocumentSymbolService.GetDocumentSymbols(text, dialect);
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

        var result = SignatureHelpService.GetSignatureHelp(text, line, character, dialect);
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

        var result = RenameService.PrepareRename(text, line, character, dialect);
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

        var result = RenameService.Rename(text, line, character, newName, uri, dialect);
        await server.SendResult(id!, result, ct);
    }
    catch (Exception ex)
    {
        await server.SendError(id!, JsonRpcErrorCodes.InternalError, $"Rename error: {ex.Message}", ct);
    }
});

// ---- Code Actions ----
server.RegisterRequestHandler("textDocument/codeAction", async (root, id, ct) =>
{
    try
    {
        var request = JsonSerializer.Deserialize(
            root.GetProperty("params").GetRawText(),
            LspJsonContext.Default.CodeActionParams);
        if (request is null)
        {
            await server.SendResult(id!, Array.Empty<CodeAction>(), ct);
            return;
        }

        if (request.Context.Only is { Length: > 0 } only
            && !only.Any(kind => kind is "quickfix" or "source" or "source.fixAll"))
        {
            await server.SendResult(id!, Array.Empty<CodeAction>(), ct);
            return;
        }

        var uri = request.TextDocument.Uri;
        var text = docs.GetText(uri);
        if (text is null)
        {
            await server.SendResult(id!, Array.Empty<CodeAction>(), ct);
            return;
        }

        var issues = request.Context.Diagnostics
            .Where(diagnostic => CodeActionService.IntersectsRange(diagnostic, request.Range, text))
            .Select(diagnostic => CodeActionService.ToLintIssue(diagnostic, text))
            .OfType<LintIssue>()
            .ToArray();
        var actions = CodeActionService.GetCodeActions(uri, text, issues, schema);
        if (request.Context.Only is { Length: > 0 } requestedKinds)
        {
            actions = actions
                .Where(action => action.Kind is not null
                    && requestedKinds.Any(kind => action.Kind.Equals(kind, StringComparison.Ordinal)
                        || action.Kind.StartsWith(kind + ".", StringComparison.Ordinal)))
                .ToArray();
        }
        await server.SendResult(id!, actions, ct);
    }
    catch (Exception ex)
    {
        await server.SendError(id!, JsonRpcErrorCodes.InternalError, $"Code action error: {ex.Message}", ct);
    }
});

// ---- Inlay Hints ----
server.RegisterRequestHandler("textDocument/inlayHint", async (root, id, ct) =>
{
    try
    {
        var p = root.GetProperty("params");
        var uri = p.GetProperty("textDocument").GetProperty("uri").GetString() ?? "";
        var rangeElement = p.GetProperty("range");
        var start = rangeElement.GetProperty("start");
        var end = rangeElement.GetProperty("end");
        var requestedRange = new JustyBase.NetezzaSqlLsp.Protocol.Range(
            new Position(start.GetProperty("line").GetInt32(), start.GetProperty("character").GetInt32()),
            new Position(end.GetProperty("line").GetInt32(), end.GetProperty("character").GetInt32()));
        var text = docs.GetText(uri);
        var hints = text is null
            ? Array.Empty<InlayHint>()
            : InlayHintService.GetInlayHints(text, schema, dialect, requestedRange);
        await server.SendResult(id!, hints, ct);
    }
    catch (Exception ex)
    {
        await server.SendError(id!, JsonRpcErrorCodes.InternalError, $"Inlay hint error: {ex.Message}", ct);
    }
});

// ---- Formatting ----
server.RegisterRequestHandler("textDocument/formatting", async (root, id, ct) =>
{
    try
    {
        var p = root.GetProperty("params");
        var uri = p.GetProperty("textDocument").GetProperty("uri").GetString() ?? "";
        var text = docs.GetText(uri);
        var formatted = text is null ? null : FormattingService.FormatDocument(text, dialect);
        if (text is null || formatted is null || string.Equals(formatted, text, StringComparison.Ordinal))
        {
            await server.SendResult(id!, Array.Empty<TextEdit>(), ct);
            return;
        }

        var lineStarts = LspTextUtilities.ComputeLineStarts(text);
        var lastLine = Math.Max(0, lineStarts.Length - 1);
        var range = new JustyBase.NetezzaSqlLsp.Protocol.Range(
            new Position(0, 0),
            new Position(lastLine, Math.Max(0, text.Length - lineStarts[lastLine])));
        await server.SendResult(id!, new[] { new TextEdit(range, formatted) }, ct);
    }
    catch (Exception ex)
    {
        await server.SendError(id!, JsonRpcErrorCodes.InternalError, $"Formatting error: {ex.Message}", ct);
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

static string[] ReadStringArray(JsonElement element, string propertyName)
{
    if (!element.TryGetProperty(propertyName, out var arrayEl) || arrayEl.ValueKind != JsonValueKind.Array)
        return Array.Empty<string>();

    return arrayEl.EnumerateArray()
        .Where(item => item.ValueKind == JsonValueKind.String)
        .Select(item => item.GetString() ?? "")
        .Where(value => value.Length > 0)
        .ToArray();
}
