namespace JustyBase.NetezzaSqlParser.Completion;

/// <summary>
/// Neutral completion trigger kind shared by UI hosts and the headless orchestrator.
/// It carries no editor-specific numeric constants.
/// </summary>
public enum CompletionTriggerKind
{
    /// <summary>Passive/automatic request such as typing a character or whitespace.</summary>
    Automatic,

    /// <summary>Explicit request such as Ctrl+Space or a dot invocation.</summary>
    Explicit,
}

/// <summary>
/// Shared trigger rules for classic completion lists. The caller owns the UI
/// event and decides when to invoke the headless orchestrator.
/// </summary>
public static class CompletionGate
{
    /// <summary>Returns true when the trigger must not open completion.</summary>
    public static bool ShouldSuppressTrigger(char? triggerChar)
        => triggerChar is not null && char.IsWhiteSpace(triggerChar.Value);

    /// <summary>Returns true for an explicit completion request such as Ctrl+Space.</summary>
    public static bool IsExplicitTrigger(char? triggerChar)
        => triggerChar is null;
}
