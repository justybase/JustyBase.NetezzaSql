using JustyBase.Ai.Embedded.Abstractions;
using JustyBase.Ai.Embedded.Download;

namespace JustyBase.Ai.Embedded.Server;

/// <summary>
/// <see cref="ICompletionProvider"/> backed by <c>mlx_lm.server</c> on Apple Silicon. Builds the
/// fill-in-the-middle prompt from the model family's FIM special tokens and posts it to the
/// OpenAI-compatible <c>/v1/completions</c> endpoint (mlx has no llama.cpp-style native endpoint).
/// MLX runs exclusively on the unified GPU — there is intentionally no CPU fallback here.
/// </summary>
public sealed class MlxFimProvider : ICompletionProvider, IDisposable
{
    private readonly LlamaServerManager _serverManager;
    private readonly IModelStore _modelStore;
    private readonly MlxCompletionClient _completions;
    private readonly bool _ownsCompletions;

    public MlxFimProvider(
        LlamaServerManager serverManager,
        IModelStore modelStore,
        MlxCompletionClient? completions = null)
    {
        _serverManager = serverManager ?? throw new ArgumentNullException(nameof(serverManager));
        _modelStore = modelStore ?? throw new ArgumentNullException(nameof(modelStore));
        _ownsCompletions = completions is null;
        _completions = completions ?? new MlxCompletionClient();
    }

    public string Id => "mlx-fim";
    public string DisplayName => "Embedded FIM (MLX)";
    public bool IsAvailable => _modelStore.IsModelPresent;
    public bool IsReady => _serverManager.FimServer is { IsRunning: true };

    public async Task EnsureReadyAsync(IProgress<FimModelProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        await _modelStore.EnsureModelAsync(progress, cancellationToken).ConfigureAwait(false);
        var instance = await _serverManager.GetOrStartServerAsync(
            LlamaServerRole.Fim,
            _modelStore.LocalModelPath,
            gpuLayers: 0, // ignored by MLX (single unified GPU); also suppresses any llama CPU retry path
            contextSize: FimContextSize,
            progress,
            cancellationToken).ConfigureAwait(false);
        _ = instance;
    }

    public async Task<CompletionSuggestion?> CompleteAsync(CompletionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var instance = _serverManager.FimServer;
        if (instance is not { IsRunning: true })
        {
            return null;
        }

        var fim = FimTemplateTokens.ForFamily(_modelStore.CurrentModel.Family);
        var prompt = string.Concat(fim.Prefix, request.Prefix, fim.Suffix, request.Suffix, fim.Middle);

        var raw = await _completions.CompleteAsync(
            instance.Endpoint,
            prompt,
            maxTokens: Math.Clamp(request.MaxTokens, 1, 512),
            temperature: request.Temperature,
            topP: request.TopP,
            cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var cleaned = SanitizeCompletion(raw);
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

    // mlx_lm.server has no --ctx-size knob; the context window is whatever the model supports.
    private const uint FimContextSize = 4096;

    public void Dispose()
    {
        if (_ownsCompletions)
        {
            _completions.Dispose();
        }
    }
}