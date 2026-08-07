namespace JustyBase.Ai.Embedded.Settings;

/// <summary>
/// All embedded-FIM related settings in one place. Hosts map their own configuration
/// (AppOptions / ApplicationConfig) onto this POCO through <see cref="IFimSettingsStore"/>.
/// </summary>
public sealed class FimSettings
{
    public bool EnableFimAi { get; set; }

    public string FimModelId { get; set; } = "qwen2.5-coder-3b";

    public int FimDebounceMs { get; set; } = 600;

    public int FimMaxTokens { get; set; } = 50;

    public int FimMaxPromptTokens { get; set; } = 1536;

    public double FimPrefixPercentage { get; set; } = 0.65;

    public double FimSuffixPercentage { get; set; } = 0.35;

    public string FimPreset { get; set; } = "Medium";

    public bool FimSchemaContext { get; set; }

    public int FimSchemaContextMaxTokens { get; set; } = 256;

    public int FimGpuLayers { get; set; } = 99;

    public int FimCtxSize { get; set; } = 4096;

    public bool FimPreferVulkan { get; set; } = true;
}

/// <summary>
/// Live access to the host's embedded-FIM settings. <see cref="Settings"/> must reflect
/// the current host configuration; <see cref="Update"/> persists mutations back.
/// </summary>
public interface IFimSettingsStore
{
    FimSettings Settings { get; }

    void Update(Action<FimSettings> mutate);
}
