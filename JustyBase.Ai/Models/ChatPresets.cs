namespace JustyBase.Ai.Models;

/// <summary>Named AI chat preset choice shown in the settings UI.</summary>
public sealed record ChatPresetChoice(string Id, string DisplayName, string Description);

/// <summary>Chat mode choice shown in the settings UI.</summary>
public sealed record ChatModeChoice(string Id, string DisplayName, string Description);

/// <summary>
/// Shared AI chat preset catalog (balanced / precise / creative / custom) and the
/// default mode choices. Host settings panels render these without duplicating data.
/// </summary>
public static class ChatPresets
{
    public static readonly ChatPresetChoice Balanced = new("balanced", "Balanced", "General-purpose — temp 0.7, 2048 tokens. Good default.");
    public static readonly ChatPresetChoice Precise = new("precise", "Precise", "Deterministic answers — temp 0.2, 4096 tokens. Best for SQL fixes.");
    public static readonly ChatPresetChoice Creative = new("creative", "Creative", "More exploratory — temp 1.1, 2048 tokens. Best for brainstorming.");
    public static readonly ChatPresetChoice Custom = new("custom", "Custom", "User-tuned values (no longer matches a named preset).");

    public static readonly IReadOnlyList<ChatPresetChoice> All = [Balanced, Precise, Creative, Custom];

    public static readonly IReadOnlyList<ChatModeChoice> AllModes =
    [
        new("expert", "Expert", "Full-featured SQL assistant with schema tools."),
        new("sqlfix", "SQL Fix", "Automated diagnostics fixer - read, fix, recheck."),
        new("simple", "Simple", "Plain chat - no tools, no schema."),
    ];
}
