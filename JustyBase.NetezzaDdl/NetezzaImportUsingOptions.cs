namespace JustyBase.NetezzaDdl;

/// <summary>
/// Union of the typed-pipe and Legacy Fast EXTERNAL import options.
/// Null/omitted optional fields keep the generated SQL compact, except
/// <see cref="RemoteSource"/> which always defaults to <c>dotnet</c> so the
/// driver resolves DATAOBJECT/pipe paths on the client.
/// </summary>
public sealed record NetezzaImportUsingOptions
{
    public const string DefaultRemoteSource = "dotnet";

    public static NetezzaImportUsingOptions Default { get; } = new();

    public string Delimiter { get; init; } = "\t";
    public string EncodingName { get; init; } = "utf-8";
    public int? MaxRows { get; init; }
    public int? MaxErrors { get; init; }
    public string? DecimalDelimiter { get; init; }
    public string? DecimalDelim { get; init; }
    public string? NullValue { get; init; }
    public string? DateStyle { get; init; }
    public string? BoolStyle { get; init; }
    public string? TimeDelimiter { get; init; }
    public string? TimeDelim { get; init; }
    public int? Y2Base { get; init; }
    public string? LogDirectory { get; init; }
    public bool TruncateStrings { get; init; }
    public bool TruncString { get; init; }
    public bool CrInString { get; init; }
    public bool CRinString { get; init; }
    public bool FillRecord { get; init; }
    public bool IgnoreZeroes { get; init; }
    public bool RequireQuotes { get; init; }
    public bool StripNulls { get; init; }
    public bool AllowControlCharacters { get; init; }
    public bool TrimBlankLines { get; init; }
    /// <summary>Always emitted; empty/null falls back to <see cref="DefaultRemoteSource"/>.</summary>
    public string? RemoteSource { get; init; } = DefaultRemoteSource;
    public long? SkipRows { get; init; }
    public string? EscapeChar { get; init; }
    public string? TimeStyle { get; init; }
    public string? QuotedValue { get; init; }
    public long? SocketBufferSize { get; init; }
    public bool? IncludeHeader { get; init; }
    public bool? IncludeZeroSeconds { get; init; }
    public bool? Compress { get; init; }
    public bool? LfInString { get; init; }
    public bool? TimeRoundNanos { get; init; }
}
