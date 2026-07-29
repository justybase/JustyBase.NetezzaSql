namespace JustyBase.NetezzaSqlParser.Authoring;

/// <summary>
/// Why lint was requested. Live typing may skip work for huge scripts;
/// save/manual always run.
/// </summary>
public enum SqlLintInvocation
{
    /// <summary>Debounced typing / document change.</summary>
    Live = 0,

    /// <summary>Document save.</summary>
    Save = 1,

    /// <summary>Explicit refresh (e.g. rule toggle).</summary>
    Manual = 2,
}
