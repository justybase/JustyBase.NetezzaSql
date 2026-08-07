using JustyBase.Ai.Models;

namespace JustyBase.Ai.Ports;

/// <summary>
/// All AI-chat related settings in one place. Hosts map their own configuration
/// (AppOptions / ApplicationConfig) onto this POCO through <see cref="IChatSettingsStore"/>.
/// </summary>
public sealed class ChatSettings
{
    public bool EnableAiChat { get; set; }

    /// <summary>
    /// Host session collection. The store maps this onto the host config by reference —
    /// mutations made inside <see cref="IChatSettingsStore.Update"/> target the same list the
    /// host persists, so the store's Apply step must keep the reference intact.
    /// </summary>
    public List<ChatSession> ChatSessions { get; set; } = [];

    public string AiChatBackendId { get; set; } = "codex";

    public string AiChatOpenAiCompatibleEndpoint { get; set; } = "http://localhost:1234/v1";

    public string? AiChatOpenAiCompatibleApiKey { get; set; }

    public string AiChatDefaultModel { get; set; } = "gpt-5.6-luna";

    public string AiChatDefaultReasoningEffort { get; set; } = "low";

    public string AiChatDefaultMode { get; set; } = "expert";

    public bool AiChatAutoConnect { get; set; }

    public int AiChatHistoryLimit { get; set; } = 10;

    public string AiChatSystemPromptOverride { get; set; } = string.Empty;

    public double AiChatTemperature { get; set; } = 0.7;

    public int AiChatMaxTokens { get; set; } = 2048;

    public int AiChatRequestTimeoutMs { get; set; } = 60000;

    public int AiChatMaxRetries { get; set; } = 1;

    public string AiChatPreset { get; set; } = "balanced";

    public bool AiChatPresetIsCustom { get; set; }

    public bool EnableEmbeddedChatAi { get; set; }

    public string EmbeddedChatModelId { get; set; } = "qwen3.5-4b";

    public int EmbeddedChatGpuLayers { get; set; } = 99;

    public int EmbeddedChatCtxSize { get; set; } = 4096;

    public List<string> EmbeddedChatAcceptedLicenseModelIds { get; set; } = [];

    public bool LlamaServerPreferVulkan { get; set; } = true;
}

/// <summary>
/// Live access to the host's chat settings. <see cref="Settings"/> must reflect the
/// current host configuration; <see cref="Update"/> persists mutations back.
/// </summary>
public interface IChatSettingsStore
{
    ChatSettings Settings { get; }

    void Update(Action<ChatSettings> mutate);
}
