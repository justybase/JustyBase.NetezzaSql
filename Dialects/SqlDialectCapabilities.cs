namespace JustyBase.NetezzaSqlParser.Dialects;

/// <summary>Observable grammar capabilities for the supported SQL dialects.</summary>
public sealed record SqlDialectCapabilities(
    bool SupportsMerge,
    bool SupportsFetchFirst,
    bool SupportsAnsiOffsetFetch,
    bool SupportsLimit);

public static class SqlDialectCapabilitiesCatalog
{
    public static SqlDialectCapabilities For(SqlDialect dialect) => dialect switch
    {
        SqlDialect.Oracle => new(
            SupportsMerge: true,
            SupportsFetchFirst: true,
            SupportsAnsiOffsetFetch: true,
            SupportsLimit: false),
        SqlDialect.Db2 => new(
            SupportsMerge: true,
            SupportsFetchFirst: true,
            SupportsAnsiOffsetFetch: true,
            SupportsLimit: false),
        _ => new(
            SupportsMerge: true,
            SupportsFetchFirst: true,
            SupportsAnsiOffsetFetch: true,
            SupportsLimit: true),
    };
}
