using JustyBase.Ai.Embedded.Settings;
using JustyBase.Ai.Models;
using JustyBase.Ai.Ports;
using JustyBase.Ai.Services;

namespace JustyBase.Ai.Tests;

public sealed class ChatSettingsTests
{
    [Fact]
    public void TryMigrateLegacyBackend_Ollama_MigratesBackendAndKeepsConfiguredEndpoint()
    {
        // The endpoint default is already "http://localhost:1234/v1"; only an explicitly
        // empty endpoint is replaced with the ollama default.
        var settings = new ChatSettings { AiChatBackendId = "ollama" };

        var migrated = LocalChatService.TryMigrateLegacyBackend(settings);

        Assert.True(migrated);
        Assert.Equal("openai-compatible", settings.AiChatBackendId);
        Assert.Equal("http://localhost:1234/v1", settings.AiChatOpenAiCompatibleEndpoint);
    }

    [Fact]
    public void TryMigrateLegacyBackend_Ollama_WithEmptyEndpoint_SetsOllamaDefault()
    {
        var settings = new ChatSettings { AiChatBackendId = "ollama", AiChatOpenAiCompatibleEndpoint = "" };

        var migrated = LocalChatService.TryMigrateLegacyBackend(settings);

        Assert.True(migrated);
        Assert.Equal("openai-compatible", settings.AiChatBackendId);
        Assert.Equal("http://localhost:11434/v1", settings.AiChatOpenAiCompatibleEndpoint);
    }

    [Fact]
    public void TryMigrateLegacyBackend_Ollama_KeepsCustomEndpoint()
    {
        var settings = new ChatSettings { AiChatBackendId = "ollama", AiChatOpenAiCompatibleEndpoint = "http://custom:8080/v1" };

        var migrated = LocalChatService.TryMigrateLegacyBackend(settings);

        Assert.True(migrated);
        Assert.Equal("openai-compatible", settings.AiChatBackendId);
        Assert.Equal("http://custom:8080/v1", settings.AiChatOpenAiCompatibleEndpoint);
    }

    [Fact]
    public void TryMigrateLegacyBackend_LmStudio_MigratesToOpenAiCompatible()
    {
        var settings = new ChatSettings { AiChatBackendId = "lmstudio" };

        var migrated = LocalChatService.TryMigrateLegacyBackend(settings);

        Assert.True(migrated);
        Assert.Equal("openai-compatible", settings.AiChatBackendId);
        Assert.Equal("http://localhost:1234/v1", settings.AiChatOpenAiCompatibleEndpoint);
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("openai-compatible")]
    [InlineData("embedded")]
    [InlineData(null)]
    [InlineData("")]
    public void TryMigrateLegacyBackend_NonLegacy_DoesNotMutate(string? backendId)
    {
        var settings = new ChatSettings { AiChatBackendId = backendId ?? "codex" };

        var migrated = LocalChatService.TryMigrateLegacyBackend(settings);

        Assert.False(migrated);
        Assert.Equal(backendId ?? "codex", settings.AiChatBackendId);
        Assert.Equal("http://localhost:1234/v1", settings.AiChatOpenAiCompatibleEndpoint);
    }

    [Fact]
    public void ChatSettings_Defaults_MatchHostDefaults()
    {
        var settings = new ChatSettings();

        Assert.Equal("codex", settings.AiChatBackendId);
        Assert.Equal("http://localhost:1234/v1", settings.AiChatOpenAiCompatibleEndpoint);
        Assert.Equal("gpt-5.6-luna", settings.AiChatDefaultModel);
        Assert.Equal("low", settings.AiChatDefaultReasoningEffort);
        Assert.Equal("expert", settings.AiChatDefaultMode);
        Assert.Equal(0.7, settings.AiChatTemperature);
        Assert.Equal(2048, settings.AiChatMaxTokens);
        Assert.Equal(60000, settings.AiChatRequestTimeoutMs);
        Assert.Equal(1, settings.AiChatMaxRetries);
        Assert.Empty(settings.ChatSessions);
        Assert.True(settings.LlamaServerPreferVulkan);
    }

    [Fact]
    public void ChatSession_ToString_ReturnsTitle()
    {
        Assert.Equal("My session", new ChatSession { Title = "My session" }.ToString());
        Assert.Equal("New Chat", new ChatSession { Title = "" }.ToString());
        Assert.Equal("New Chat", new ChatSession { Title = "  " }.ToString());
    }
}

public sealed class FimSettingsTests
{
    [Fact]
    public void FimSettings_Defaults_MatchHostDefaults()
    {
        var settings = new FimSettings();

        Assert.Equal("qwen2.5-coder-3b", settings.FimModelId);
        Assert.Equal(600, settings.FimDebounceMs);
        Assert.Equal(50, settings.FimMaxTokens);
        Assert.Equal(1536, settings.FimMaxPromptTokens);
        Assert.Equal(0.65, settings.FimPrefixPercentage);
        Assert.Equal(0.35, settings.FimSuffixPercentage);
        Assert.Equal("Medium", settings.FimPreset);
        Assert.Equal(99, settings.FimGpuLayers);
        Assert.Equal(4096, settings.FimCtxSize);
        Assert.True(settings.FimPreferVulkan);
    }
}
