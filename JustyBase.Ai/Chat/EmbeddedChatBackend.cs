using JustyBase.Ai.Embedded.Download;
using JustyBase.Ai.Embedded.Server;
using JustyBase.Ai.Ports;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace JustyBase.Ai.Chat;

/// <summary>
/// "Embedded (local)" AI chat backend: a bundled llama.cpp llama-server subprocess hosting the
/// selected GGUF chat model. The server exposes an OpenAI-compatible endpoint, so the whole
/// existing chat pipeline (including tool calling / agent loop) works unchanged.
/// </summary>
public sealed class EmbeddedChatBackend : ILocalChatBackend
{
    /// <summary>Keyed-service key for the embedded chat model store (see host DI registration).</summary>
    public const string ChatModelStoreKey = "chat";

    private readonly IChatSettingsStore _settingsStore;
    private readonly LlamaServerManager _serverManager;
    private readonly IModelStore _chatModelStore;

    public EmbeddedChatBackend(
        IChatSettingsStore settingsStore,
        LlamaServerManager serverManager,
        [FromKeyedServices(ChatModelStoreKey)] IModelStore chatModelStore)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _serverManager = serverManager ?? throw new ArgumentNullException(nameof(serverManager));
        _chatModelStore = chatModelStore ?? throw new ArgumentNullException(nameof(chatModelStore));
    }

    public string Id => "embedded";
    public string DisplayName => "Embedded (local)";

    public Uri Endpoint
    {
        get => _serverManager.ChatServer?.Endpoint ?? new Uri("http://127.0.0.1:0");
        set => _ = value; // endpoint is managed by the llama-server process
    }

    /// <summary>Executes model tool calls (approval-gated). Wired by LocalChatService.</summary>
    public Func<string, string, Task<string>>? ToolExecutor { get; set; }

    /// <summary>Human-readable reason when <see cref="PingAsync"/> returns false.</summary>
    public string? LastError { get; private set; }

    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        try
        {
            // The EnableEmbeddedChatAi master switch gates the whole backend: do not download
            // the binary or spawn a server unless the user opted in.
            if (!_settingsStore.Settings.EnableEmbeddedChatAi)
            {
                LastError = "Embedded (local) is disabled — enable 'Embedded AI (Chat)' in Preferences and prepare a model there first.";
                return false;
            }

            if (!_chatModelStore.IsModelPresent)
            {
                LastError = "No embedded chat model downloaded — prepare one in Preferences → Embedded AI (Chat).";
                return false;
            }

            var server = _serverManager.ChatServer;
            if (server is { IsRunning: true })
            {
                return await PingServerAsync(server, ct);
            }

            var settings = _settingsStore.Settings;
            var instance = await _serverManager.GetOrStartServerAsync(
                LlamaServerRole.Chat,
                _chatModelStore.LocalModelPath,
                ResolveGpuLayers(settings),
                (uint)Math.Clamp(settings.EmbeddedChatCtxSize > 0 ? settings.EmbeddedChatCtxSize : 4096, 512, 131_072),
                progress: null,
                ct).ConfigureAwait(false);
            return await PingServerAsync(instance, ct);
        }
        catch (Exception ex)
        {
            LastError = $"Embedded llama-server failed to start: {ex.Message}";
            return false;
        }
    }

    private static async Task<bool> PingServerAsync(ILlamaServerInstance server, CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            using var resp = await http.GetAsync(new Uri(server.Endpoint, "/health"), ct);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<string>> ListModelsAsync(CancellationToken ct = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        return [_chatModelStore.CurrentModel.Id];
    }

    public IChatClient CreateChatClient(string modelId, bool enableFunctionInvocation = true)
    {
        var server = _serverManager.ChatServer;
        if (server is null)
        {
            throw new InvalidOperationException("Embedded llama-server is not running.");
        }

        return new OpenAiCompatibleChatClient(
            server.Endpoint,
            modelId,
            apiKey: null,
            enableFunctionInvocation ? ToolExecutor : null,
            _sharedHttp,
            sendReasoningEffort: true);
    }

    // One shared HttpClient for all chat streams (singleton lifetime).
    private static readonly HttpClient _sharedHttp = new() { Timeout = TimeSpan.FromMinutes(10) };

    private static int ResolveGpuLayers(ChatSettings settings)
    {
        if (!settings.LlamaServerPreferVulkan)
        {
            return 0;
        }

        // Negative = auto: llama-server offloads as many layers as fit in VRAM.
        var layers = settings.EmbeddedChatGpuLayers;
        return layers < 0 ? -1 : Math.Clamp(layers, 0, 999);
    }
}
