using JustyBase.Ai.Embedded.Abstractions;
using JustyBase.Ai.Embedded.Download;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JustyBase.Ai.Embedded.Server;

/// <summary>
/// <see cref="ICompletionProvider"/> backed by a bundled llama.cpp llama-server using the
/// native /completion endpoint with input_prefix/input_suffix (llama.cpp applies the model's
/// FIM template, e.g. Qwen2.5-Coder, CodeGemma, StarCoder2, Codestral).
/// </summary>
public sealed class LlamaServerFimProvider : ICompletionProvider, IDisposable
{
    private readonly LlamaServerManager _serverManager;
    private readonly IModelStore _modelStore;
    private readonly Func<int> _getGpuLayers;
    private readonly Func<uint> _getContextSize;
    private readonly HttpClient _http;

    public LlamaServerFimProvider(
        LlamaServerManager serverManager,
        IModelStore modelStore,
        Func<int> getGpuLayers,
        Func<uint> getContextSize,
        HttpClient? httpClient = null)
    {
        _serverManager = serverManager ?? throw new ArgumentNullException(nameof(serverManager));
        _modelStore = modelStore ?? throw new ArgumentNullException(nameof(modelStore));
        _getGpuLayers = getGpuLayers ?? (() => 0);
        _getContextSize = getContextSize ?? (() => 4096);
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
    }

    public string Id => "llamaserver-fim";
    public string DisplayName => "Embedded FIM (llama-server)";
    public bool IsAvailable => _modelStore.IsModelPresent;
    public bool IsReady => _serverManager.FimServer is { IsRunning: true };

    public async Task EnsureReadyAsync(IProgress<FimModelProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        await _modelStore.EnsureModelAsync(progress, cancellationToken).ConfigureAwait(false);
        var instance = await _serverManager.GetOrStartServerAsync(
            LlamaServerRole.Fim,
            _modelStore.LocalModelPath,
            _getGpuLayers(),
            _getContextSize(),
            progress,
            cancellationToken).ConfigureAwait(false);
        _ = instance; // endpoint is resolved per-request below so model switches take effect immediately
    }

    public async Task<CompletionSuggestion?> CompleteAsync(CompletionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var instance = _serverManager.FimServer;
        if (instance is not { IsRunning: true })
        {
            return null;
        }

        var body = new LlamaCompletionRequest
        {
            InputPrefix = request.Prefix,
            InputSuffix = request.Suffix,
            NPredict = Math.Clamp(request.MaxTokens, 1, 512),
            Temperature = request.Temperature,
            TopP = request.TopP,
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(instance.Endpoint, "/completion"))
        {
            Content = JsonContent.Create(body, LlamaServerJsonContext.Default.LlamaCompletionRequest),
        };

        using var response = await _http.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var payload = await response.Content.ReadFromJsonAsync(
            LlamaServerJsonContext.Default.LlamaCompletionResponse,
            cancellationToken).ConfigureAwait(false);

        var text = payload?.Content;
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var cleaned = SanitizeCompletion(text);
        return string.IsNullOrEmpty(cleaned) ? null : new CompletionSuggestion(cleaned);
    }

    private static string SanitizeCompletion(string text)
    {
        var t = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        var blank = t.IndexOf("\n\n", StringComparison.Ordinal);
        if (blank >= 0)
        {
            t = t[..blank];
        }

        return t.TrimEnd();
    }

    public void Dispose() => _http.Dispose();
}

internal sealed class LlamaCompletionRequest
{
    [JsonPropertyName("input_prefix")]
    public string InputPrefix { get; init; } = string.Empty;

    [JsonPropertyName("input_suffix")]
    public string InputSuffix { get; init; } = string.Empty;

    [JsonPropertyName("n_predict")]
    public int NPredict { get; init; } = 50;

    [JsonPropertyName("temperature")]
    public float Temperature { get; init; } = 0.15f;

    [JsonPropertyName("top_p")]
    public float TopP { get; init; } = 0.9f;

    [JsonPropertyName("cache_prompt")]
    public bool CachePrompt { get; init; } = true;
}

internal sealed class LlamaCompletionResponse
{
    [JsonPropertyName("content")]
    public string? Content { get; init; }

    [JsonPropertyName("tokens_predicted")]
    public int TokensPredicted { get; init; }

    [JsonPropertyName("timings")]
    public LlamaCompletionTimings? Timings { get; init; }
}

internal sealed class LlamaCompletionTimings
{
    [JsonPropertyName("predicted_per_second")]
    public double PredictedPerSecond { get; init; }
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(LlamaCompletionRequest))]
[JsonSerializable(typeof(LlamaCompletionResponse))]
internal sealed partial class LlamaServerJsonContext : JsonSerializerContext
{
}
