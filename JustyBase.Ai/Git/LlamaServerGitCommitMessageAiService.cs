using JustyBase.Ai.Embedded.Download;
using JustyBase.Ai.Embedded.Server;
using JustyBase.Ai.Embedded.Settings;
using JustyBase.Ai.Git;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JustyBase.Ai.Git;

/// <summary>
/// Uses the bundled FIM llama-server with plain text completion (no FIM tokens) to draft
/// git commit messages. The FIM catalog model is base/non-instruct, so prompts must be
/// few-shot continuation - not chat "rules" lists (see <see cref="GitCommitPromptBuilder"/>).
/// </summary>
public sealed class LlamaServerGitCommitMessageAiService : JustyBase.Ai.Git.IGitCommitMessageAiService
{
    private readonly LlamaServerManager _serverManager;
    private readonly IModelStore _fimStore;
    private readonly IFimSettingsStore _settingsStore;
    private readonly HttpClient _http;

    public LlamaServerGitCommitMessageAiService(
        LlamaServerManager serverManager,
        IModelStore fimStore,
        IFimSettingsStore settingsStore,
        HttpClient? httpClient = null)
    {
        _serverManager = serverManager ?? throw new ArgumentNullException(nameof(serverManager));
        _fimStore = fimStore ?? throw new ArgumentNullException(nameof(fimStore));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    }

    public bool IsAvailable => _settingsStore.Settings.EnableFimAi;

    public async Task<string?> GenerateAsync(string changeContext, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(changeContext))
        {
            return null;
        }

        if (!_fimStore.IsModelPresent)
        {
            return null;
        }

        // GetOrStartServerAsync reuses a running server only when it serves the same model;
        // otherwise it restarts with the currently selected model. This avoids posting the
        // prompt to a stale server that still hosts a different FIM model.
        var settings = _settingsStore.Settings;
        ILlamaServerInstance server;
        try
        {
            server = await _serverManager.GetOrStartServerAsync(
                LlamaServerRole.Fim,
                _fimStore.LocalModelPath,
                settings.FimPreferVulkan
                    ? Math.Clamp(settings.FimGpuLayers < 0 ? 99 : settings.FimGpuLayers, 0, 999)
                    : 0,
                (uint)Math.Clamp(settings.FimCtxSize > 0 ? settings.FimCtxSize : 4096, 512, 131_072),
                progress: null,
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }

        if (server is not { IsRunning: true })
        {
            return null;
        }

        var prompt = GitCommitPromptBuilder.Build(changeContext);

        // MLX (Apple Silicon) serves the OpenAI /v1/completions endpoint instead of llama.cpp's
        // native /completion, so the request and response shapes differ by backend.
        if (server is MlxServerInstance)
        {
            using var mlx = new MlxCompletionClient(_http);
            var mlxRaw = await mlx.CompleteAsync(
                server.Endpoint,
                prompt,
                maxTokens: 96,
                temperature: 0.2f,
                topP: 0.9f,
                cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(mlxRaw))
            {
                return null;
            }

            var mlxCleaned = GitCommitPromptBuilder.CleanMessage(mlxRaw);
            return string.IsNullOrWhiteSpace(mlxCleaned) ? null : mlxCleaned;
        }

        var body = new LlamaGitCompletionRequest
        {
            Prompt = prompt,
            NPredict = 96,
            Temperature = 0.2f,
            TopP = 0.9f,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(server.Endpoint, "/completion"))
        {
            Content = JsonContent.Create(body, GitLlamaJsonContext.Default.LlamaGitCompletionRequest),
        };

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var payload = await response.Content.ReadFromJsonAsync(
            GitLlamaJsonContext.Default.LlamaGitCompletionResponse,
            cancellationToken).ConfigureAwait(false);

        var raw = payload?.Content;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var cleaned = GitCommitPromptBuilder.CleanMessage(raw);
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }
}

internal sealed class LlamaGitCompletionRequest
{
    [JsonPropertyName("prompt")]
    public string Prompt { get; init; } = string.Empty;

    [JsonPropertyName("n_predict")]
    public int NPredict { get; init; } = 96;

    [JsonPropertyName("temperature")]
    public float Temperature { get; init; } = 0.2f;

    [JsonPropertyName("top_p")]
    public float TopP { get; init; } = 0.9f;
}

internal sealed class LlamaGitCompletionResponse
{
    [JsonPropertyName("content")]
    public string? Content { get; init; }
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(LlamaGitCompletionRequest))]
[JsonSerializable(typeof(LlamaGitCompletionResponse))]
internal sealed partial class GitLlamaJsonContext : JsonSerializerContext
{
}
