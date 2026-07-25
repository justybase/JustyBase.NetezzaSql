namespace JustyBase.Netezza.Metadata;

/// <summary>
/// Prefetch stages for Netezza metadata (Lite METADATA_CACHE_CONTRACT order).
/// Hosts should run stages in enum order after connect.
/// </summary>
public enum MetadataPrefetchStage
{
    /// <summary>Load database names.</summary>
    Databases = 0,
    /// <summary>Load schemas within databases.</summary>
    Schemas = 1,
    /// <summary>Load tables, views, and external tables.</summary>
    Objects = 2,
    /// <summary>Load stored procedures.</summary>
    Procedures = 3,
    /// <summary>Hydrate columns (may be deferred when object count is large).</summary>
    Columns = 4
}

/// <summary>
/// Helpers describing freshness / lazy-column policy for completion quality.
/// </summary>
public static class MetadataPrefetchContract
{
    /// <summary>Default TTL for cached table metadata (Lite uses hours; desktop uses 30m by default).</summary>
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(30);

    /// <summary>When object count reaches this, hosts should skip full-column hydration.</summary>
    public const int LazyColumnsObjectThreshold = 500;

    /// <summary>Prefetch stages in Lite contract order.</summary>
    public static IReadOnlyList<MetadataPrefetchStage> OrderedStages { get; } =
    [
        MetadataPrefetchStage.Databases,
        MetadataPrefetchStage.Schemas,
        MetadataPrefetchStage.Objects,
        MetadataPrefetchStage.Procedures,
        MetadataPrefetchStage.Columns
    ];

    /// <summary>Returns whether hosts should skip eager column hydration.</summary>
    public static bool ShouldDeferColumnHydration(int tableLikeObjectCount)
        => tableLikeObjectCount >= LazyColumnsObjectThreshold;
}
