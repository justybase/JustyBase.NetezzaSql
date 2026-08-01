using System.Collections.Concurrent;
using System.Text.Json;
using JustyBase.NetezzaSqlParser.Caching;
using JustyBase.NetezzaSqlLsp.Protocol;
using JustyBase.NetezzaSqlLsp.Workspace;
using JustyBase.NetezzaSqlLsp.Services;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.NetezzaSqlLsp.Handlers;

/// <summary>Handles LSP text document lifecycle notifications (didOpen/didChange/didClose).</summary>
public static class TextDocumentHandlers
{
    // Per-document lock to avoid blocking all documents during lint
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _docLocks = new(StringComparer.OrdinalIgnoreCase);

    // Compatibility overloads for embedders that do not keep a document
    // analysis coordinator. The LSP executable uses the stateful overloads.
    public static async Task HandleDidOpen(LspServer server, DocumentManager docs, ISchemaProvider? schema, SqlDialect dialect, JsonElement root, CancellationToken ct)
    {
        using var parsingCoordinator = new DocumentParsingCoordinator();
        using var lintCoordinator = new LintCoordinator(parsingCoordinator);
        await HandleDidOpen(server, docs, schema, dialect, lintCoordinator, root, ct);
    }

    public static async Task HandleDidChange(LspServer server, DocumentManager docs, ISchemaProvider? schema, SqlDialect dialect, JsonElement root, CancellationToken ct)
    {
        using var parsingCoordinator = new DocumentParsingCoordinator();
        using var lintCoordinator = new LintCoordinator(parsingCoordinator);
        await HandleDidChange(server, docs, schema, dialect, lintCoordinator, root, ct);
    }

    public static async Task HandleDidClose(LspServer server, DocumentManager docs, JsonElement root, CancellationToken ct)
    {
        using var parsingCoordinator = new DocumentParsingCoordinator();
        using var lintCoordinator = new LintCoordinator(parsingCoordinator);
        await HandleDidClose(server, docs, lintCoordinator, root, ct);
    }

    public static async Task PublishDiagnosticsAsync(LspServer server, DocumentManager docs, ISchemaProvider? schema, SqlDialect dialect, string uri, CancellationToken ct)
    {
        using var parsingCoordinator = new DocumentParsingCoordinator();
        using var lintCoordinator = new LintCoordinator(parsingCoordinator);
        await PublishDiagnosticsAsync(server, docs, schema, dialect, lintCoordinator, uri, ct);
    }

    /// <summary>Handles textDocument/didOpen: stores the document and publishes diagnostics.</summary>
    /// <summary>Handles textDocument/didOpen: stores the document and publishes diagnostics.</summary>
    public static async Task HandleDidOpen(LspServer server, DocumentManager docs, ISchemaProvider? schema, SqlDialect dialect, LintCoordinator lintCoordinator, JsonElement root, CancellationToken ct)
    {
        var p = root.GetProperty("params");
        var textDoc = p.GetProperty("textDocument");
        var uri = textDoc.GetProperty("uri").GetString() ?? "";
        var text = textDoc.GetProperty("text").GetString() ?? "";
        var version = textDoc.GetProperty("version").GetInt32();

        docs.OpenOrUpdate(uri, text, version);
        await PublishDiagnosticsAsync(server, docs, schema, dialect, lintCoordinator, uri, ct);
    }

    /// <summary>Handles textDocument/didChange: updates the document text and re-publishes diagnostics.</summary>
    public static async Task HandleDidChange(LspServer server, DocumentManager docs, ISchemaProvider? schema, SqlDialect dialect, LintCoordinator lintCoordinator, JsonElement root, CancellationToken ct)
    {
        var p = root.GetProperty("params");
        var textDoc = p.GetProperty("textDocument");
        var uri = textDoc.GetProperty("uri").GetString() ?? "";
        var version = textDoc.GetProperty("version").GetInt32();
        var changes = p.GetProperty("contentChanges");
        if (changes.GetArrayLength() > 0)
        {
            var text = changes[0].GetProperty("text").GetString() ?? "";
            docs.UpdateText(uri, text, version);
        }
        await PublishDiagnosticsAsync(server, docs, schema, dialect, lintCoordinator, uri, ct);
    }

    /// <summary>Handles textDocument/didClose: removes the document and clears diagnostics.</summary>
    public static async Task HandleDidClose(LspServer server, DocumentManager docs, LintCoordinator lintCoordinator, JsonElement root, CancellationToken ct)
    {
        var p = root.GetProperty("params");
        var uri = p.GetProperty("textDocument").GetProperty("uri").GetString() ?? "";
        docs.Close(uri);
        lintCoordinator.Release(uri);

        // Clear diagnostics for closed document
        await ClearDiagnosticsAsync(server, uri, ct);
    }

    /// <summary>Lints the document and sends a textDocument/publishDiagnostics notification.</summary>
    public static async Task PublishDiagnosticsAsync(LspServer server, DocumentManager docs, ISchemaProvider? schema, SqlDialect dialect, LintCoordinator lintCoordinator, string uri, CancellationToken ct)
    {
        var docLock = _docLocks.GetOrAdd(uri, _ => new SemaphoreSlim(1, 1));
        await docLock.WaitAsync(ct);
        try
        {
            var text = docs.GetText(uri);
            if (text is null) return;

            var diagnostics = lintCoordinator.Lint(text, schema, dialect, uri, ct);
            await server.SendNotification("textDocument/publishDiagnostics",
                new PublishDiagnosticsParams(uri, diagnostics.ToArray()), ct);
        }
        finally
        {
            docLock.Release();
        }
    }

    private static async Task ClearDiagnosticsAsync(LspServer server, string uri, CancellationToken ct)
    {
        try
        {
            await server.SendNotification("textDocument/publishDiagnostics",
                new PublishDiagnosticsParams(uri, []), ct);
        }
        catch (OperationCanceledException)
        {
            // Shutdown in progress — ignore
        }
    }
}
