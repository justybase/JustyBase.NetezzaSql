namespace JustyBase.ImportExport.Import;

/// <summary>
/// EXTERNAL/USING options for database-specific import (moved from the Avalonia host).
/// The Netezza engine maps these onto the full <c>NetezzaImportUsingOptions</c> surface.
/// </summary>
public sealed class ImportUsingOptions
{
    public static ImportUsingOptions Default { get; } = new();

    /// <summary>Column delimiter written to the pipe and used in EXTERNAL USING (default tab).</summary>
    public string Delimiter { get; init; } = "\t";

    /// <summary>Encoding name for pipe writer and EXTERNAL USING ENCODING (default utf-8).</summary>
    public string EncodingName { get; init; } = "utf-8";

    /// <summary>Optional MAXROWS for EXTERNAL USING; null means omit.</summary>
    public int? MaxRows { get; init; }
}

/// <summary>Ambient options for the current import operation (set by Import UI, read by the engines).</summary>
public static class ImportUsingOptionsContext
{
    private static readonly AsyncLocal<ImportUsingOptions?> s_current = new();

    public static ImportUsingOptions? Current
    {
        get => s_current.Value;
        set => s_current.Value = value;
    }
}
