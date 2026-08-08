using JustyBase.Ai.Embedded.Abstractions;

namespace JustyBase.Ai.Embedded.Server;

/// <summary>Chat and FIM server roles for <see cref="LlamaServerManager"/>.</summary>
public enum LlamaServerRole
{
    Chat,
    Fim,
}

/// <summary>
/// Owns up to two llama-server subprocesses (one for the AI chat model, one for the FIM model).
/// Instances are reused while the requested model/parameters match.
/// </summary>
public sealed class LlamaServerManager : IAsyncDisposable, IDisposable
{
    private readonly ILlamaServerBinary _binary;
    private readonly Func<string, string, int, uint, ILlamaServerInstance> _instanceFactory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;
    private ILlamaServerInstance? _chatServer;
    private ILlamaServerInstance? _fimServer;
    private string? _chatModelPath;
    private string? _fimModelPath;
    private int _chatGpuLayers;
    private uint _chatContextSize;
    private int _fimGpuLayers;
    private uint _fimContextSize;
    private string? _chatBinaryVariant;
    private string? _fimBinaryVariant;

    public LlamaServerManager(LlamaServerBinaryManager binary)
        : this(binary, static (path, model, gpu, ctx) => new LlamaServerInstance(path, model, gpu, ctx))
    {
    }

    /// <summary>
    /// Apple Silicon variant: provisions the MLX runtime (<c>uv</c>) and launches <c>mlx_lm.server</c>.
    /// Unlike llama.cpp there is no CPU fallback and no GPU-layer knob — MLX always runs on the
    /// unified GPU, so switching models must unload the previous one first.
    /// </summary>
    public LlamaServerManager(MlxServerRuntime mlxRuntime)
        : this(mlxRuntime, static (binary, model, _, _) => new MlxServerInstance(binary, model))
    {
    }

    /// <summary>Test seam: injects a binary provider and an instance factory (no real processes).</summary>
    internal LlamaServerManager(
        ILlamaServerBinary binary,
        Func<string, string, int, uint, ILlamaServerInstance> instanceFactory)
    {
        _binary = binary ?? throw new ArgumentNullException(nameof(binary));
        _instanceFactory = instanceFactory ?? throw new ArgumentNullException(nameof(instanceFactory));
    }

    public ILlamaServerInstance? ChatServer => _chatServer;
    public ILlamaServerInstance? FimServer => _fimServer;

    /// <summary>Gets (or starts) the server for the given role, downloading the binary first.</summary>
    public async Task<ILlamaServerInstance> GetOrStartServerAsync(
        LlamaServerRole role,
        string modelPath,
        int gpuLayers,
        uint contextSize,
        IProgress<FimModelProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(modelPath);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = role == LlamaServerRole.Chat ? _chatServer : _fimServer;
            var currentPath = role == LlamaServerRole.Chat ? _chatModelPath : _fimModelPath;
            var currentGpu = role == LlamaServerRole.Chat ? _chatGpuLayers : _fimGpuLayers;
            var currentCtx = role == LlamaServerRole.Chat ? _chatContextSize : _fimContextSize;
            var currentVariant = role == LlamaServerRole.Chat ? _chatBinaryVariant : _fimBinaryVariant;
            var binaryVariant = _binary.BinaryVariant;
            // MLX (Apple Silicon) runs on the single unified GPU: a replacement model can never
            // share memory with the old one, so the previous server must be stopped before load.
            var isMlx = string.Equals(binaryVariant, "mlx", StringComparison.OrdinalIgnoreCase);

            if (current is { IsRunning: true }
                && string.Equals(currentPath, modelPath, StringComparison.OrdinalIgnoreCase)
                && currentGpu == gpuLayers
                && currentCtx == contextSize
                && string.Equals(currentVariant, binaryVariant, StringComparison.OrdinalIgnoreCase))
            {
                return current;
            }

            if (!_binary.IsBinaryPresent)
            {
                await _binary.EnsureBinaryAsync(progress, cancellationToken).ConfigureAwait(false);
            }

            // When the replacement also uses GPU layers, the old server must be stopped
            // BEFORE the new model is loaded: two models cannot share VRAM, and a start
            // that fails on "out of GPU memory" would otherwise keep the old model loaded
            // and make the switch impossible. CPU-only replacements start first (no
            // resource contention) so a failed start keeps the old server running.
            if ((gpuLayers > 0 || isMlx) && current is { IsRunning: true })
            {
                await StopServerCoreAsync(role).ConfigureAwait(false);
            }

            // Start the replacement on a fresh instance (it picks its own free port) BEFORE
            // stopping the old one. A failed start disposes only the new instance and leaves
            // the previous (possibly healthy) server running — no rollback needed.
            ILlamaServerInstance? instance = null;
            try
            {
#pragma warning disable CA2000 // Ownership transfers to _chatServer/_fimServer (disposed in StopServerCoreAsync/DisposeAsync).
                instance = _instanceFactory(_binary.BinaryPath, modelPath, gpuLayers, contextSize);
#pragma warning restore CA2000
                var started = await instance.StartAsync(progress, cancellationToken).ConfigureAwait(false);
                if (!started && gpuLayers != 0 && !isMlx)
                {
                    // GPU (Vulkan) start failed — commonly out-of-device-memory on a low-VRAM
                    // adapter. Retry once on CPU so the backend still works instead of failing.
                    // Also covers the auto (negative) mode when llama.cpp cannot fit the model.
                    progress?.Report(new FimModelProgress(0.5, "GPU start failed — retrying on CPU…"));
                    await instance.DisposeAsync().ConfigureAwait(false);
#pragma warning disable CA2000 // Ownership transfers to _chatServer/_fimServer (disposed in StopServerCoreAsync/DisposeAsync).
                    instance = _instanceFactory(_binary.BinaryPath, modelPath, 0, contextSize);
#pragma warning restore CA2000
                    started = await instance.StartAsync(progress, cancellationToken).ConfigureAwait(false);
                }

                if (!started)
                {
                    throw new InvalidOperationException(
                        $"llama-server failed to start: {instance.LastError ?? "unknown error"}");
                }
            }
            catch
            {
                if (instance is not null)
                {
                    await instance.DisposeAsync().ConfigureAwait(false);
                }

                throw;
            }

            // Replacement is healthy — now the old instance (if any) can be disposed. If
            // disposing it throws, never leak the new (running) instance.
            try
            {
                await StopServerCoreAsync(role).ConfigureAwait(false);
            }
            catch
            {
                if (instance is not null)
                {
                    await instance.DisposeAsync().ConfigureAwait(false);
                }

                throw;
            }

            if (role == LlamaServerRole.Chat)
            {
                _chatServer = instance;
                _chatModelPath = modelPath;
                _chatGpuLayers = gpuLayers;
                _chatContextSize = contextSize;
                _chatBinaryVariant = binaryVariant;
            }
            else
            {
                _fimServer = instance;
                _fimModelPath = modelPath;
                _fimGpuLayers = gpuLayers;
                _fimContextSize = contextSize;
                _fimBinaryVariant = binaryVariant;
            }

            var running = role == LlamaServerRole.Chat ? _chatServer! : _fimServer!;
            instance = null;
            return running;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopServerAsync(LlamaServerRole role, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopServerCoreAsync(role).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task StopServerCoreAsync(LlamaServerRole role)
    {
        if (role == LlamaServerRole.Chat)
        {
            if (_chatServer is not null)
            {
                await _chatServer.DisposeAsync().ConfigureAwait(false);
                _chatServer = null;
            }

            _chatModelPath = null;
            _chatGpuLayers = 0;
            _chatContextSize = 0;
            _chatBinaryVariant = null;
        }
        else
        {
            if (_fimServer is not null)
            {
                await _fimServer.DisposeAsync().ConfigureAwait(false);
                _fimServer = null;
            }

            _fimModelPath = null;
            _fimGpuLayers = 0;
            _fimContextSize = 0;
            _fimBinaryVariant = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_chatServer is not null)
            {
                await _chatServer.DisposeAsync().ConfigureAwait(false);
                _chatServer = null;
            }

            if (_fimServer is not null)
            {
                await _fimServer.DisposeAsync().ConfigureAwait(false);
                _fimServer = null;
            }
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}
