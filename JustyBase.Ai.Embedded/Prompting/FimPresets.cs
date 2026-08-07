namespace JustyBase.Ai.Embedded.Prompting;

/// <summary>Named FIM quality/speed presets. Individual options remain editable (→ Custom).</summary>
public static class FimPresets
{
    public const string Small = "Small";
    public const string Medium = "Medium";
    public const string Large = "Large";
    public const string Custom = "Custom";

    /// <summary>~chars per token heuristic for turning prompt-token budgets into document windows.</summary>
    public const int ApproxCharsPerToken = 4;

    public static IReadOnlyList<FimPresetDefinition> All { get; } =
    [
        new(
            Id: Small,
            DisplayName: "Small",
            MaxPromptTokens: 512,
            PrefixPercentage: 0.60,
            SuffixPercentage: 0.40,
            MaxGenerationTokens: 30,
            ModelId: "qwen2.5-coder-1.5b",
            Notes: "Fast / low VRAM — Qwen2.5-Coder 1.5B, short context."),
        new(
            Id: Medium,
            DisplayName: "Medium",
            MaxPromptTokens: 1536,
            PrefixPercentage: 0.65,
            SuffixPercentage: 0.35,
            MaxGenerationTokens: 50,
            ModelId: "qwen2.5-coder-3b",
            Notes: "Balanced — Qwen2.5-Coder 3B (default)."),
        new(
            Id: Large,
            DisplayName: "Large",
            MaxPromptTokens: 4096,
            PrefixPercentage: 0.70,
            SuffixPercentage: 0.30,
            MaxGenerationTokens: 80,
            ModelId: "qwen2.5-coder-7b",
            Notes: "Highest quality context — Qwen 7B by default; pick 14B manually if VRAM allows."),
    ];

    public static FimPresetDefinition Get(string? id) =>
        All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? All.First(p => p.Id == Medium);

    public static string Normalize(string? id)
    {
        if (string.Equals(id, Custom, StringComparison.OrdinalIgnoreCase))
        {
            return Custom;
        }

        if (string.Equals(id, Small, StringComparison.OrdinalIgnoreCase))
        {
            return Small;
        }

        if (string.Equals(id, Large, StringComparison.OrdinalIgnoreCase))
        {
            return Large;
        }

        return Medium;
    }

    public static (int PrefixChars, int SuffixChars) ResolveCharBudgets(
        int maxPromptTokens,
        double prefixPercentage,
        double suffixPercentage)
    {
        var promptTokens = Math.Clamp(maxPromptTokens <= 0 ? 1536 : maxPromptTokens, 128, 8192);
        var prefixPct = ClampPct(prefixPercentage, 0.60);
        var suffixPct = ClampPct(suffixPercentage, 0.40);
        var sum = prefixPct + suffixPct;
        if (sum > 1.0)
        {
            prefixPct /= sum;
            suffixPct /= sum;
        }

        var totalChars = promptTokens * ApproxCharsPerToken;
        var prefix = Math.Max(32, (int)Math.Round(totalChars * prefixPct));
        var suffix = Math.Max(16, (int)Math.Round(totalChars * suffixPct));
        return (prefix, suffix);
    }

    private static double ClampPct(double value, double fallback)
    {
        if (double.IsNaN(value) || value <= 0)
        {
            return fallback;
        }

        return Math.Clamp(value, 0.05, 0.95);
    }
}

public sealed record FimPresetDefinition(
    string Id,
    string DisplayName,
    int MaxPromptTokens,
    double PrefixPercentage,
    double SuffixPercentage,
    int MaxGenerationTokens,
    string ModelId,
    string Notes);
