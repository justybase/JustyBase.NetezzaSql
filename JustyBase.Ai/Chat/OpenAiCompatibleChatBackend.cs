using JustyBase.Ai.Ports;
using Microsoft.Extensions.AI;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace JustyBase.Ai.Chat;

/// <summary>
/// Single OpenAI-compatible backend with a user-configurable base URL + optional API key.
/// Replaces the former separate Ollama and LM Studio backends (both speak OpenAI /v1).
/// </summary>
public sealed class OpenAiCompatibleChatBackend : ILocalChatBackend
{
    private readonly IChatSettingsStore _settingsStore;

    public OpenAiCompatibleChatBackend(IChatSettingsStore settingsStore)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
    }

    public string Id => "openai-compatible";
    public string DisplayName => "OpenAI Compatible";

    public Uri Endpoint
    {
        get
        {
            var configured = _settingsStore.Settings.AiChatOpenAiCompatibleEndpoint;
            return Uri.TryCreate(configured, UriKind.Absolute, out var uri)
                ? uri
                : new Uri("http://localhost:1234/v1");
        }
        set => _settingsStore.Update(s => s.AiChatOpenAiCompatibleEndpoint = value?.AbsoluteUri ?? string.Empty);
    }

    private string? ApiKey => _settingsStore.Settings.AiChatOpenAiCompatibleApiKey;

    /// <summary>Executes model tool calls (approval-gated). Wired by LocalChatService.</summary>
    public Func<string, string, Task<string>>? ToolExecutor { get; set; }

    // One shared HttpClient for all chat streams of this backend (singleton lifetime);
    // the per-request clients only borrow it.
    private readonly HttpClient _sharedHttp = new() { Timeout = TimeSpan.FromMinutes(10) };

    private Uri ModelsUri => new($"{Endpoint.AbsoluteUri.TrimEnd('/')}/models");

    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            ApplyAuth(http);
            var resp = await http.GetAsync(ModelsUri, ct);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<string>> ListModelsAsync(CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            ApplyAuth(http);
            var resp = await http.GetFromJsonAsync(
                ModelsUri,
                OpenAiCompatModelsJsonContext.Default.OpenAiModelsResponse,
                ct);
            return resp?.Data?.Select(m => m.Id).Where(n => !string.IsNullOrWhiteSpace(n)).ToList() ?? [];
        }
        catch
        {
            return [];
        }
    }

    public IChatClient CreateChatClient(string modelId, bool enableFunctionInvocation = true)
    {
        return new OpenAiCompatibleChatClient(
            Endpoint,
            modelId,
            ApiKey,
            enableFunctionInvocation ? ToolExecutor : null,
            _sharedHttp);
    }

    private void ApplyAuth(HttpClient http)
    {
        if (!string.IsNullOrWhiteSpace(ApiKey))
        {
            http.DefaultRequestHeaders.Authorization = new("Bearer", ApiKey);
        }
    }
}

internal sealed class OpenAiModelsResponse
{
    [JsonPropertyName("data")]
    public List<OpenAiModelEntry>? Data { get; set; }
}

internal sealed class OpenAiModelEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
}

[JsonSerializable(typeof(OpenAiModelsResponse))]
internal sealed partial class OpenAiCompatModelsJsonContext : JsonSerializerContext
{
}
