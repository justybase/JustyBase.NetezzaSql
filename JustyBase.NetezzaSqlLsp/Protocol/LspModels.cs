using System.Text.Json.Serialization;
using System.Text.Json;

namespace JustyBase.NetezzaSqlLsp.Protocol;

// ====== JSON-RPC 2.0 ======

public record JsonRpcRequest(
    string Jsonrpc,
    JsonElement? Id,
    string Method,
    JsonElement? Params
);

public record JsonRpcResponse(
    string Jsonrpc,
    JsonElement? Id,
    JsonElement? Result,
    JsonRpcError? Error
);

public record JsonRpcNotification(
    string Jsonrpc,
    string Method,
    JsonElement? Params
);

public record JsonRpcError(
    int Code,
    string Message,
    JsonElement? Data
);

public static class JsonRpcErrorCodes
{
    public const int ParseError = -32700;
    public const int InvalidRequest = -32600;
    public const int MethodNotFound = -32601;
    public const int InvalidParams = -32602;
    public const int InternalError = -32603;
    public const int ServerNotInitialized = -32002;
    public const int RequestCancelled = -32800;
}

// ====== LSP Types ======

public record Position(int Line, int Character);
public record Range(Position Start, Position End);
public record Location(string Uri, Range Range);
public record TextDocumentItem(string Uri, string LanguageId, int Version, string Text);
public record VersionedTextDocumentIdentifier(string Uri, int Version);
public record TextDocumentContentChangeEvent(string Text);

public record InitializeParams(
    int? ProcessId,
    string? RootUri,
    ClientCapabilities? Capabilities
);

public record ClientCapabilities();

public record InitializeResult(
    ServerCapabilities Capabilities,
    ServerInfo? ServerInfo
);

public record ServerCapabilities(
    TextDocumentSyncKind? TextDocumentSync,
    CompletionOptions? CompletionProvider,
    DiagnosticOptions? DiagnosticProvider,
    SemanticTokensOptions? SemanticTokensProvider,
    bool? HoverProvider,
    bool? DefinitionProvider,
    bool? ReferencesProvider,
    bool? DocumentSymbolProvider,
    SignatureHelpOptions? SignatureHelpProvider,
    bool? RenameProvider
);

public record ServerInfo(string Name, string Version);

public record CompletionOptions(bool ResolveProvider, string[]? TriggerCharacters);

public record DiagnosticOptions(bool InterFileDependencies, bool WorkspaceDiagnostics);

public record SemanticTokensOptions(
    SemanticTokensLegend Legend,
    bool Full,
    bool? Range
);

public record SemanticTokensLegend(string[] TokenTypes, string[] TokenModifiers);

public record CompletionParams(TextDocumentIdentifier TextDocument, Position Position);

public record TextDocumentIdentifier(string Uri);

public record CompletionList(bool IsIncomplete, CompletionItem[]? Items);

public record CompletionItem(
    string Label,
    CompletionItemKind? Kind,
    string? Detail,
    string? InsertText
);

public enum CompletionItemKind
{
    Text = 1, Method = 2, Function = 3, Constructor = 4,
    Field = 5, Variable = 6, Class = 7, Interface = 8,
    Module = 9, Property = 10, Unit = 11, Value = 12,
    Enum = 13, Keyword = 14, Snippet = 15, Color = 16,
    File = 17, Reference = 18, Folder = 19, EnumMember = 20,
    Constant = 21, Struct = 22, Event = 23, Operator = 24,
    TypeParameter = 25
}

public enum TextDocumentSyncKind
{
    None = 0,
    Full = 1,
    Incremental = 2
}

public record Diagnostic(
    Range Range,
    DiagnosticSeverity? Severity,
    string? Code,
    string? Source,
    string Message,
    Dictionary<string, object?>? Data = null
);

public enum DiagnosticSeverity
{
    Error = 1,
    Warning = 2,
    Information = 3,
    Hint = 4
}

public record PublishDiagnosticsParams(string Uri, Diagnostic[] Diagnostics);

public record DidOpenTextDocumentParams(TextDocumentItem TextDocument);
public record DidChangeTextDocumentParams(VersionedTextDocumentIdentifier TextDocument, TextDocumentContentChangeEvent[] ContentChanges);
public record DidCloseTextDocumentParams(TextDocumentIdentifier TextDocument);

public record SemanticTokensParams(TextDocumentIdentifier TextDocument);
public record SemanticTokensResult(uint[]? Data);

public record HoverParams(TextDocumentIdentifier TextDocument, Position Position);
public record Hover(MarkupContent? Contents, Range? Range);
public record MarkupContent(string Kind, string Value);

public record DefinitionParams(TextDocumentIdentifier TextDocument, Position Position);
public record ReferenceParams(TextDocumentIdentifier TextDocument, Position Position, ReferenceContext Context);
public record ReferenceContext(bool IncludeDeclaration);

public record DocumentSymbolParams(TextDocumentIdentifier TextDocument);
public record SymbolInformation(
    string Name,
    SymbolKind Kind,
    Range Range,
    string? ContainerName
);

public enum SymbolKind
{
    File = 1, Module = 2, Namespace = 3, Package = 4,
    Class = 5, Method = 6, Property = 7, Field = 8,
    Constructor = 9, Enum = 10, Interface = 11, Function = 12,
    Variable = 13, Constant = 14, String = 15, Number = 16,
    Boolean = 17, Array = 18, Object = 19, Key = 20,
    Null = 21, EnumMember = 22, Struct = 23, Event = 24,
    Operator = 25, TypeParameter = 26
}

// ====== Signature Help ======

public record SignatureHelpOptions(string[]? TriggerCharacters);

public record SignatureHelpParams(TextDocumentIdentifier TextDocument, Position Position, string? Context);

public record SignatureHelp(
    SignatureInformation[] Signatures,
    int ActiveSignature,
    int ActiveParameter
);

public record SignatureInformation(
    string Label,
    string? Documentation,
    ParameterInformation[]? Parameters
);

public record ParameterInformation(
    string Label,
    string? Documentation
);

// ====== Custom JustyBase protocol ======

public record SyncSchemaParams(string Database, string Schema, TableSchema[] Tables);
public record TableSchema(string Name, ColumnSchema[] Columns);
public record ColumnSchema(string Name);

// ====== Rename ======

public record RenameParams(TextDocumentIdentifier TextDocument, Position Position, string NewName);

public record PrepareRenameParams(TextDocumentIdentifier TextDocument, Position Position);

public record PrepareRenameResult(bool PreparePlaceholder, Range Range);

public record WorkspaceEdit(Dictionary<string, TextEdit[]>? Changes);

public record TextEdit(Range Range, string NewText);


