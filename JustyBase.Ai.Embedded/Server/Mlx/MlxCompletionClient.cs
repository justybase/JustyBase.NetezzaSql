using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace JustyBase.Ai.Embedded.Server;

/// <summary>
/// Minimal client for mlx_lm.server's OpenAI-compatible <c>/v1/completions</c> endpoint (raw text
/// completion without a chat template). Used by the MLX FIM provider and the git-commit service on
/// Apple Silicon, where the llama.cpp native <c>/completion</c> endpoint does not exist.
/// </summary>
public sealed class MlxCompletionClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public MlxCompletionClient(HttpClient? httpClient = null)
    {
        _ownsHttp = httpClient is null;
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
    }

    /// <summary>Posts a raw completion. Returns the generated text, or null on failure/empty.</summary>
    public async Task<string?> CompleteAsync(
        Uri endpoint,
        string prompt,
        int maxTokens,
        float temperature = 0.2f,
        float topP = 0.9f,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrEmpty(prompt);

        var body = new MlxCompletionRequest
        {
            Prompt = prompt,
            MaxTokens = Math.Clamp(maxTokens, 1, 512),
            Temperature = temperature,
            TopP = topP,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint, "/v1/completions"))
        {
            Content = JsonContent.Create(body, MlxJsonContext.Default.MlxCompletionRequest),
        };

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var payload = await response.Content.ReadFromJsonAsync(
            MlxJsonContext.Default.MlxCompletionResponse,
            cancellationToken).ConfigureAwait(false);

        var text = payload?.Choices is { Count: > 0 } choices ? choices[0].Text : null;
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    public void Dispose()
    {
        if (_ownsHttp)
        {
            _http.Dispose();
        }
    }
}

public sealed class MlxCompletionRequest
{
    [JsonPropertyName("prompt")]
    public string Prompt { get; init; } = string.Empty;

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; init; } = 50;

    [JsonPropertyName("temperature")]
    public float Temperature { get; init; } = 0.2f;

    [JsonPropertyName("top_p")]
    public float TopP { get; init; } = 0.9f;
}

public sealed class MlxCompletionResponse
{
    [JsonPropertyName("choices")]
    public List<MlxCompletionChoice> Choices { get; init; } = [];
}

public sealed class MlxCompletionChoice
{
    [JsonPropertyName("text")]
    public string? Text { get; init; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; init; }
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(MlxCompletionRequest))]
[JsonSerializable(typeof(MlxCompletionResponse))]
public sealed partial class MlxJsonContext : JsonSerializerContext
{
}