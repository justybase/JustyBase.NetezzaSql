using JustyBase.Ai.Embedded.Abstractions;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace JustyBase.Ai.Embedded.Server;

/// <summary>
/// One <c>mlx_lm.server</c> subprocess (launched through <c>uv tool run</c>) hosting a single MLX
/// model snapshot on 127.0.0.1. Exposes the OpenAI-compatible endpoints used by the chat, FIM and
/// git-commit clients. Runs only on Apple Silicon (unified memory).
/// </summary>
public sealed class MlxServerInstance : ILlamaServerInstance
{
    // First "uv tool run" installs Python + mlx-lm into the uv cache and can take several minutes,
    // well beyond the model load time alone.
    private static readonly TimeSpan StartupDeadline = TimeSpan.FromMinutes(15);

    private readonly string _uvPath;
    private readonly string _modelPath;
    private Process? _process;
    private CancellationTokenSource? _startCts;
    private bool _disposed;

    public MlxServerInstance(string uvPath, string modelPath)
    {
        _uvPath = uvPath ?? throw new ArgumentNullException(nameof(uvPath));
        _modelPath = modelPath ?? throw new ArgumentNullException(nameof(modelPath));
        Port = LlamaServerInstance.FindFreePort();
        Endpoint = new Uri($"http://127.0.0.1:{Port}");
    }

    public int Port { get; }
    public Uri Endpoint { get; }
    public bool IsRunning => _process is { HasExited: false };
    public string? LastError { get; private set; }
    public string LogFilePath { get; private set; } = string.Empty;

    public async Task<bool> StartAsync(
        IProgress<FimModelProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            return true;
        }

        if (_disposed)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        LogFilePath = Path.Combine(
            Path.GetDirectoryName(_uvPath) ?? string.Empty,
            $"mlx-server-{Port}.log");

        progress?.Report(new FimModelProgress(
            0.5,
            "Starting mlx_lm.server — the first run installs the MLX runtime and may take a few minutes…"));

        var psi = new ProcessStartInfo(_uvPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = _modelPath,
        };
        foreach (var argument in BuildArguments(_modelPath, Port))
        {
            psi.ArgumentList.Add(argument);
        }

        try
        {
            var process = Process.Start(psi);
            if (process is null)
            {
                LastError = "Failed to start mlx_lm.server process.";
                return false;
            }

            _process = process;
            _ = Task.Run(() => PumpProcessOutput(process, LogFilePath), CancellationToken.None);
        }
        catch (Exception ex)
        {
            LastError = $"Failed to start mlx_lm.server: {ex.Message}";
            _process = null;
            return false;
        }

        using var health = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var deadline = DateTime.UtcNow + StartupDeadline;
        var startCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _startCts = startCts;
        try
        {
            while (DateTime.UtcNow < deadline)
            {
                startCts.Token.ThrowIfCancellationRequested();
                if (_process is not { HasExited: false })
                {
                    LastError = $"mlx_lm.server exited early (port {Port}). See {LogFilePath}.";
                    return false;
                }

                try
                {
                    using var resp = await health.GetAsync(new Uri(Endpoint, "/health"), startCts.Token).ConfigureAwait(false);
                    if (resp.IsSuccessStatusCode)
                    {
                        progress?.Report(new FimModelProgress(1.0, "mlx_lm.server ready."));
                        return true;
                    }
                }
                catch
                {
                    // not up yet
                }

                await Task.Delay(750, startCts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        finally
        {
            if (ReferenceEquals(_startCts, startCts))
            {
                _startCts = null;
            }

            startCts.Dispose();
        }

        LastError = "mlx_lm.server did not become ready in time.";
        return false;
    }

    /// <summary>Builds the <c>uv tool run</c> command line. Exposed for tests.</summary>
    internal static IReadOnlyList<string> BuildArguments(string modelPath, int port)
    {
        return
        [
            "tool",
            "run",
            "--from",
            "mlx-lm",
            "mlx_lm.server",
            "--model",
            modelPath,
            "--host",
            "127.0.0.1",
            "--port",
            port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--temp",
            "0",
            "--max-tokens",
            "512",
            "--log-level",
            "warning",
        ];
    }

    private static async Task PumpProcessOutput(Process process, string logPath)
    {
        var sb = new StringBuilder();
        var gate = new object();
        void Append(string line)
        {
            lock (gate)
            {
                if (sb.Length > 64_000)
                {
                    sb.Clear();
                }

                sb.AppendLine(line);
            }
        }

        static async Task Pump(StreamReader reader, Action<string> append)
        {
            try
            {
                while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
                {
                    append(line);
                }
            }
            catch
            {
                // pipe closed
            }
        }

        await Task.WhenAll(
            Pump(process.StandardOutput, Append),
            Pump(process.StandardError, Append)).ConfigureAwait(false);

        try
        {
            File.WriteAllText(logPath, sb.ToString());
        }
        catch
        {
            // best effort
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _startCts?.Cancel();
        _startCts?.Dispose();
        _startCts = null;

        var process = _process;
        _process = null;
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().ConfigureAwait(false);
            }

            process.Dispose();
        }
        catch
        {
            // already gone
        }
    }
}