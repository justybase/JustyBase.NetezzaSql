using JustyBase.Ai.Embedded.Abstractions;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace JustyBase.Ai.Embedded.Server;

/// <summary>
/// One llama.cpp <c>llama-server</c> subprocess hosting a single GGUF model on 127.0.0.1.
/// </summary>
public sealed class LlamaServerInstance : ILlamaServerInstance
{
    private readonly string _binaryPath;
    private readonly string _modelPath;
    private readonly int _gpuLayers;
    private readonly uint _contextSize;
    private Process? _process;
    private CancellationTokenSource? _startCts;
    private bool _disposed;

    public LlamaServerInstance(string binaryPath, string modelPath, int gpuLayers, uint contextSize)
    {
        _binaryPath = binaryPath ?? throw new ArgumentNullException(nameof(binaryPath));
        _modelPath = modelPath ?? throw new ArgumentNullException(nameof(modelPath));
        _gpuLayers = Math.Clamp(gpuLayers, 0, 999);
        _contextSize = Math.Clamp(contextSize, 512, 131_072);
        Port = FindFreePort();
        Endpoint = new Uri($"http://127.0.0.1:{Port}");
    }

    public int Port { get; }
    public Uri Endpoint { get; }
    public bool IsRunning => _process is { HasExited: false };
    public string? LastError { get; private set; }
    public string LogFilePath { get; private set; } = string.Empty;

    /// <summary>Starts the server and waits until /health reports ready (model load can take a while).</summary>
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
            Path.GetDirectoryName(_binaryPath) ?? string.Empty,
            $"llama-server-{Port}.log");

        progress?.Report(new FimModelProgress(0.5, $"Starting llama-server on port {Port}…"));

        var psi = new ProcessStartInfo(_binaryPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(_binaryPath) ?? string.Empty,
        };
        psi.ArgumentList.Add("-m");
        psi.ArgumentList.Add(_modelPath);
        psi.ArgumentList.Add("--host");
        psi.ArgumentList.Add("127.0.0.1");
        psi.ArgumentList.Add("--port");
        psi.ArgumentList.Add(Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        psi.ArgumentList.Add("--n-gpu-layers");
        psi.ArgumentList.Add(_gpuLayers.ToString(System.Globalization.CultureInfo.InvariantCulture));
        psi.ArgumentList.Add("--ctx-size");
        psi.ArgumentList.Add(_contextSize.ToString(System.Globalization.CultureInfo.InvariantCulture));
        psi.ArgumentList.Add("--no-webui");

        try
        {
            var process = Process.Start(psi);
            if (process is null)
            {
                LastError = "Failed to start llama-server process.";
                return false;
            }

            _process = process;
            _ = Task.Run(() => PumpProcessOutput(process, LogFilePath), CancellationToken.None);
        }
        catch (Exception ex)
        {
            LastError = $"Failed to start llama-server: {ex.Message}";
            _process = null;
            return false;
        }

        using var health = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var deadline = DateTime.UtcNow.AddMinutes(8);
        var startCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _startCts = startCts;
        try
        {
            while (DateTime.UtcNow < deadline)
            {
                startCts.Token.ThrowIfCancellationRequested();
                if (_process is not { HasExited: false })
                {
                    LastError = $"llama-server exited early (port {Port}). See {LogFilePath}.";
                    return false;
                }

                try
                {
                    using var resp = await health.GetAsync(new Uri(Endpoint, "/health"), startCts.Token).ConfigureAwait(false);
                    if (resp.IsSuccessStatusCode)
                    {
                        progress?.Report(new FimModelProgress(1.0, "llama-server ready."));
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
            // Caller cancelled the start (shutdown / model switch). Never mask a user
            // cancellation as "llama-server failed to start".
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

        LastError = "llama-server did not become ready in time.";
        return false;
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

        // Drain both streams — llama.cpp logs to stderr; leaving it unread can fill the
        // 4KB pipe buffer and block the child before /health ever becomes reachable.
        await Task.WhenAll(
            Pump(process.StandardOutput, Append),
            Pump(process.StandardError, Append)).ConfigureAwait(false);

        try
        {
            File.WriteAllText(logPath, sb.ToString());
        }
        catch
        {
            // logging is best-effort
        }
    }

    public static int FindFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        return port;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        // Cancel any in-flight StartAsync health poll so shutdown never waits out the deadline.
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
