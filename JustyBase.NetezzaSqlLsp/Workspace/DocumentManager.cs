using System.Collections.Concurrent;

namespace JustyBase.NetezzaSqlLsp.Workspace;

/// <summary>Represents an open text document in the LSP workspace.</summary>
public sealed class Document
{
    /// <summary>Document URI.</summary>
    public string Uri { get; }

    /// <summary>Current text content.</summary>
    public string Text { get; set; }

    /// <summary>Document version number.</summary>
    public int Version { get; set; }

    /// <summary>Creates a new document.</summary>
    public Document(string uri, string text, int version)
    {
        Uri = uri;
        Text = text;
        Version = version;
    }
}

/// <summary>Manages open text documents in the LSP workspace.</summary>
public sealed class DocumentManager
{
    private readonly ConcurrentDictionary<string, Document> _docs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Opens or replaces a document.</summary>
    public void OpenOrUpdate(string uri, string text, int version)
    {
        _docs[uri] = new Document(uri, text, version);
    }

    /// <summary>Updates the text of an existing document.</summary>
    public void UpdateText(string uri, string text, int version)
    {
        if (_docs.TryGetValue(uri, out var doc))
        {
            doc.Text = text;
            doc.Version = version;
        }
    }

    /// <summary>Closes a document and removes it from the workspace.</summary>
    public void Close(string uri)
    {
        _docs.TryRemove(uri, out _);
    }

    /// <summary>Returns the current text for the given document, or <see langword="null"/>.</summary>
    public string? GetText(string uri)
    {
        return _docs.TryGetValue(uri, out var doc) ? doc.Text : null;
    }

    /// <summary>Returns whether the document is currently open.</summary>
    public bool IsOpen(string uri) => _docs.ContainsKey(uri);

    /// <summary>Returns all open document URIs.</summary>
    public IEnumerable<string> GetAllUris() => _docs.Keys;
}
