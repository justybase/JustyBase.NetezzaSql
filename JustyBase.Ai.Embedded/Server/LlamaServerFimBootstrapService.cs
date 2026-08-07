using JustyBase.Ai.Embedded.Abstractions;
using JustyBase.Ai.Embedded.Download;
using JustyBase.Ai.Embedded.Prompting;
using JustyBase.Ai.Embedded.Server;
using System.Diagnostics;

namespace JustyBase.Ai.Embedded.Server;

/// <summary>Starts on-demand GGUF download / llama-server start with progress callbacks.</summary>
public interface IFimModelBootstrapService
{
    string ModelsDirectory { get; }
    bool IsSelectedModelPresent { get; }
    string SelectedModelLocalPath { get; }
    string SelectedModelDiskStatus { get; }
    string EnsureModelsDirectory();
    Task EnsureReadyAsync(IProgress<FimModelProgress>? progress = null, CancellationToken cancellationToken = default);
    Task DeleteSelectedModelAsync(CancellationToken cancellationToken = default);
    Task ReloadModelAsync(CancellationToken cancellationToken = default);
    Task StopServerAsync(CancellationToken cancellationToken = default);
    Task<FimSpeedTestReport> RunSpeedTestAsync(
        int maxTokens,
        int maxPromptTokens,
        double prefixPercentage,
        double suffixPercentage,
        IProgress<FimModelProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed record FimSpeedTestReport(
    string ModelName,
    long ElapsedMs,
    double TokensPerSecond,
    bool Succeeded);

/// <summary>
/// Server-based FIM bootstrap: downloads the selected GGUF and starts the dedicated
/// llama-server FIM subprocess (separate model from the AI chat server).
/// </summary>
public sealed class LlamaServerFimBootstrapService : IFimModelBootstrapService
{
    private readonly ICompletionProvider _provider;
    private readonly IModelStore _store;
    private readonly LlamaServerManager _serverManager;
    private readonly Action? _notifyModelReady;
    private int _busy;

    public LlamaServerFimBootstrapService(
        ICompletionProvider provider,
        IModelStore store,
        LlamaServerManager serverManager,
        Action? notifyModelReady = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _serverManager = serverManager ?? throw new ArgumentNullException(nameof(serverManager));
        _notifyModelReady = notifyModelReady;
    }

    public string ModelsDirectory => _store.ModelsDirectory;
    public bool IsSelectedModelPresent => _store.IsModelPresent;
    public string SelectedModelLocalPath => _store.LocalModelPath;
    public string EnsureModelsDirectory() => _store.EnsureModelsDirectory();

    public string SelectedModelDiskStatus
    {
        get
        {
            var model = _store.CurrentModel;
            if (!_store.IsModelPresent)
            {
                return $"{model.DisplayName}: not downloaded.";
            }

            try
            {
                var mb = new FileInfo(_store.LocalModelPath).Length / (1024d * 1024d);
                var server = _serverManager.FimServer;
                var state = server is { IsRunning: true }
                    ? $"server on port {server.Port}"
                    : "server not running";
                return $"{model.DisplayName}: on disk ({mb:0.#} MB), {state}.";
            }
#pragma warning disable CA1031
            catch
#pragma warning restore CA1031
            {
                return $"{model.DisplayName}: on disk.";
            }
        }
    }

    public async Task EnsureReadyAsync(IProgress<FimModelProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            throw new InvalidOperationException("A FIM model download/load is already in progress.");
        }

        try
        {
            await _provider.EnsureReadyAsync(progress, cancellationToken).ConfigureAwait(false);
            _notifyModelReady?.Invoke();
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    public async Task DeleteSelectedModelAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            throw new InvalidOperationException("A FIM model download/load is already in progress.");
        }

        try
        {
            await _serverManager.StopServerAsync(LlamaServerRole.Fim, cancellationToken).ConfigureAwait(false);
            if (!_store.TryDeleteCurrentModel())
            {
                throw new InvalidOperationException("No local model file to delete for the selected model.");
            }
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    public async Task ReloadModelAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            throw new InvalidOperationException("A FIM model download/load is already in progress.");
        }

        try
        {
            await _serverManager.StopServerAsync(LlamaServerRole.Fim, cancellationToken).ConfigureAwait(false);
            if (_store.IsModelPresent)
            {
                await _provider.EnsureReadyAsync(progress: null, cancellationToken).ConfigureAwait(false);
                _notifyModelReady?.Invoke();
            }
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    public async Task StopServerAsync(CancellationToken cancellationToken = default)
    {
        await _serverManager.StopServerAsync(LlamaServerRole.Fim, cancellationToken).ConfigureAwait(false);
    }

    public async Task<FimSpeedTestReport> RunSpeedTestAsync(
        int maxTokens,
        int maxPromptTokens,
        double prefixPercentage,
        double suffixPercentage,
        IProgress<FimModelProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!_store.IsModelPresent)
        {
            throw new InvalidOperationException("Download / prepare the selected model before running the speed test.");
        }

        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            throw new InvalidOperationException("A FIM model download/load is already in progress.");
        }

        try
        {
            await _provider.EnsureReadyAsync(progress, cancellationToken).ConfigureAwait(false);
            var (prefixLimit, suffixLimit) = FimPresets.ResolveCharBudgets(maxPromptTokens, prefixPercentage, suffixPercentage);
            var prefix = new string('x', prefixLimit);
            var suffix = new string('y', suffixLimit);

            var sw = Stopwatch.StartNew();
            var suggestion = await _provider.CompleteAsync(
                new CompletionRequest(
                    prefix,
                    suffix,
                    MaxTokens: FimContextExtractor.ClampMaxTokens(maxTokens),
                    Temperature: 0.2f,
                    TopP: 0.9f),
                cancellationToken).ConfigureAwait(false);
            sw.Stop();

            if (suggestion is null)
            {
                return new FimSpeedTestReport(_store.CurrentModel.DisplayName, sw.ElapsedMilliseconds, 0, false);
            }

            var tokens = Math.Max(1, suggestion.Text.Length / FimPresets.ApproxCharsPerToken);
            var tokensPerSecond = sw.Elapsed.TotalSeconds > 0
                ? tokens / sw.Elapsed.TotalSeconds
                : 0;
            return new FimSpeedTestReport(_store.CurrentModel.DisplayName, sw.ElapsedMilliseconds, tokensPerSecond, true);
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }
}
