namespace JustyBase.NetezzaDdl.Models;

/// <summary>Cached external table metadata fetched from the catalog.</summary>
public record NetezzaExternalTableCachedInfo
{
    public string? DataObject { get; init; }
    public string? Delimiter { get; init; }
    public string? Encoding { get; init; }
    public string? Timestyle { get; init; }
    public string? RemoteSource { get; init; }
    public long? SkipRows { get; init; }
    public long? MaxErrors { get; init; }
    public string? EscapeChar { get; init; }
    public string? DecimalDelim { get; init; }
    public string? LogDir { get; init; }
    public string? QuotedValue { get; init; }
    public string? NullValue { get; init; }
    public bool? CrInString { get; init; }
    public bool? TruncString { get; init; }
    public bool? CtrlChars { get; init; }
    public bool? IgnoreZero { get; init; }
    public bool? TimeExtraZeros { get; init; }
    public short? Y2Base { get; init; }
    public bool? FillRecord { get; init; }
    public string? Compress { get; init; }
    public bool? IncludeHeader { get; init; }
    public bool? LfInString { get; init; }
    public string? DateStyle { get; init; }
    public string? DateDelim { get; init; }
    public string? TimeDelim { get; init; }
    public string? BoolStyle { get; init; }
    public string? Format { get; init; }
    public int? SocketBufSize { get; init; }
    public string? RecordDelim { get; init; }
    public long? MaxRows { get; init; }
    public bool? RequireQuotes { get; init; }
    public string? RecordLength { get; init; }
    public string? DateTimeDelim { get; init; }
    public string? RejectFile { get; init; }
}
