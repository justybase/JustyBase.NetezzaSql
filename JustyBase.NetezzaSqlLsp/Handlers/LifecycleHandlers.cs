using System.Text.Json;
using JustyBase.NetezzaSqlLsp.Protocol;
using JustyBase.NetezzaSqlLsp.Services;

namespace JustyBase.NetezzaSqlLsp.Handlers;

/// <summary>Handles LSP lifecycle methods: initialize, initialized, shutdown.</summary>
public static class LifecycleHandlers
{
    /// <summary>Handles the initialize request: returns server capabilities.</summary>
    public static async Task HandleInitialize(LspServer server, JsonElement root, string id, CancellationToken ct)
    {
        var result = new InitializeResult(
            new ServerCapabilities(
                TextDocumentSync: TextDocumentSyncKind.Full,
                CompletionProvider: new CompletionOptions(false, new[] { "." }),
                DiagnosticProvider: null,
                SemanticTokensProvider: new SemanticTokensOptions(
                    new SemanticTokensLegend(SemanticTokensService.TokenTypesLegend, SemanticTokensService.TokenModifiersLegend),
                    Full: true,
                    Range: null
                ),
                HoverProvider: true,
                DefinitionProvider: true,
                ReferencesProvider: true,
                DocumentSymbolProvider: true,
                SignatureHelpProvider: new SignatureHelpOptions(new[] { "(", "," }),
                RenameProvider: true
            ),
            new ServerInfo("Netezza SQL LSP", "0.1.0")
        );

        await server.SendResult(id, result, ct);
    }

    /// <summary>Handles the initialized notification.</summary>
    public static void HandleInitialized(LspServer server)
    {
        // Client confirmed initialization — server is ready
        server.SetInitialized();
    }

    /// <summary>Handles the shutdown request.</summary>
    public static async Task HandleShutdown(LspServer server, string id, CancellationToken ct)
    {
        await server.SendResult(id, null, ct);
    }
}
