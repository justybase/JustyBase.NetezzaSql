using JustyBase.NetezzaDdl;

namespace JustyBase.ImportExport.Import;

/// <summary>Per-execution configuration for an <see cref="IImportEngine"/>.</summary>
public sealed record ImportEngineOptions
{
    /// <summary>Directory used for import log/error artifacts (external-table engines).</summary>
    public string? TempLogDirectory { get; init; }

    /// <summary>External data source name (Netezza: 'dotnet', 'ODBC'...).</summary>
    public string RemoteSource { get; init; } = NetezzaImportUsingOptions.DefaultRemoteSource;

    /// <summary>Explicit USING options; when null the ambient <see cref="ImportUsingOptionsContext"/> is used.</summary>
    public ImportUsingOptions? UsingOptions { get; init; }

    /// <summary>
    /// When set, rows are inserted into the existing target table using these destination
    /// column names (1:1 with the source columns, in source order) and the CREATE is skipped.
    /// </summary>
    public IReadOnlyList<string>? TargetColumnNames { get; init; }
}