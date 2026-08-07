using JustyBase.Ai.Embedded.Abstractions;

namespace JustyBase.Ai.Embedded.Server;

/// <summary>
/// Downloads and caches the bundled llama.cpp <c>llama-server.exe</c>.
/// Abstraction over <see cref="LlamaServerBinaryManager"/> so <see cref="LlamaServerManager"/>
/// lifecycle logic is testable without touching the network or the binary cache.
/// </summary>
public interface ILlamaServerBinary
{
    string BinaryPath { get; }
    bool IsBinaryPresent { get; }
    string BinaryVariant { get; }

    /// <summary>Downloads and extracts llama-server.exe for the current variant when missing. Safe to call repeatedly.</summary>
    Task EnsureBinaryAsync(IProgress<FimModelProgress>? progress = null, CancellationToken cancellationToken = default);
}
