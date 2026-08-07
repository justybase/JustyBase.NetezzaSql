using JustyBase.Ai.Embedded.Abstractions;

namespace JustyBase.Ai.Embedded.Server;

/// <summary>
/// A single llama.cpp <c>llama-server</c> subprocess hosting a GGUF model on 127.0.0.1.
/// Abstraction over <see cref="LlamaServerInstance"/> so server lifecycle logic
/// (see <see cref="LlamaServerManager"/>) is testable without spawning real processes.
/// </summary>
public interface ILlamaServerInstance : IAsyncDisposable
{
    int Port { get; }
    Uri Endpoint { get; }
    bool IsRunning { get; }
    string? LastError { get; }
    string LogFilePath { get; }

    /// <summary>Starts the server and waits until /health reports ready (model load can take a while).</summary>
    Task<bool> StartAsync(IProgress<FimModelProgress>? progress = null, CancellationToken cancellationToken = default);
}
