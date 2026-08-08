using JustyBase.Ai.Embedded.Abstractions;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http.Headers;

namespace JustyBase.Ai.Embedded.Server;

/// <summary>
/// Apple Silicon runtime provider for the embedded AI backends. Instead of a llama.cpp binary
/// it provisions the <c>uv</c> launcher (astral-sh) which installs and runs Apple's
/// <c>mlx-lm</c> <c>mlx_lm.server</c> OpenAI-compatible HTTP server on the first use.
/// </summary>
public sealed class MlxServerRuntime : ILlamaServerBinary
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public MlxServerRuntime(HttpClient? httpClient = null)
    {
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? CreateDefaultHttpClient();
        RuntimeDirectory = DefaultRuntimeDirectory();
        EnsureRuntimeDirectory();
    }

    public string RuntimeDirectory { get; }

    /// <summary>Path to the <c>uv</c> launcher executable.</summary>
    public string BinaryPath => Path.Combine(RuntimeDirectory, "uv");

    public bool IsBinaryPresent => File.Exists(BinaryPath);

    public string BinaryVariant => "mlx";

    public static string DefaultRuntimeDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "JustyBase",
            "mlx-runtime");

    public string EnsureRuntimeDirectory()
    {
        Directory.CreateDirectory(RuntimeDirectory);
        return RuntimeDirectory;
    }

    private static HttpClient CreateDefaultHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromHours(2) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("JustyBase", "1.0"));
        return client;
    }

    /// <summary>
    /// Downloads and extracts the uv launcher when missing. First <c>uv tool run</c> after this
    /// installs Python + mlx-lm into the uv cache (a few minutes), which MlxServerInstance accounts
    /// for with a generous startup deadline.
    /// </summary>
    public async Task EnsureBinaryAsync(
        IProgress<FimModelProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (IsBinaryPresent)
        {
            progress?.Report(new FimModelProgress(1.0, "MLX runtime (uv) already present."));
            return;
        }

        EnsureRuntimeDirectory();
        var asset = "uv-aarch64-apple-darwin.tar.gz";
        var uri = new Uri($"https://github.com/astral-sh/uv/releases/latest/download/{asset}");
        var tgzPath = Path.Combine(RuntimeDirectory, asset);

        progress?.Report(new FimModelProgress(0, "Downloading the MLX runtime (uv)…"));
        try
        {
            using (var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException(
                        $"uv download failed: {(int)response.StatusCode} {response.ReasonPhrase} ({uri}).");
                }

                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var target = new FileStream(
                    tgzPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 128 * 1024,
                    useAsync: true);
                await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
                await target.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new FimModelProgress(0.8, "Extracting uv…"));

            await using (var file = new FileStream(tgzPath, FileMode.Open, FileAccess.Read))
            await using (var gzip = new GZipStream(file, CompressionMode.Decompress))
            using (var tar = new TarReader(gzip, leaveOpen: false))
            {
                TarEntry? entry;
                while ((entry = await tar.GetNextEntryAsync().ConfigureAwait(false)) is not null)
                {
                    if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.SymbolicLink))
                    {
                        continue;
                    }

                    if (entry.Name.EndsWith("/uv", StringComparison.Ordinal)
                        || entry.Name.EndsWith("/uv.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        entry.ExtractToFile(BinaryPath, overwrite: true);
                        break;
                    }
                }
            }

            if (!File.Exists(BinaryPath))
            {
                throw new InvalidOperationException($"uv executable not found inside {asset}.");
            }

            MakeExecutable(BinaryPath);
            progress?.Report(new FimModelProgress(1.0, "MLX runtime (uv) ready."));
        }
        finally
        {
            try { File.Delete(tgzPath); } catch { /* best effort */ }
        }
    }

    private static void MakeExecutable(string path)
    {
        try
        {
            var psi = new ProcessStartInfo("/bin/chmod") { UseShellExecute = false, CreateNoWindow = true };
            psi.ArgumentList.Add("+x");
            psi.ArgumentList.Add(path);
            using var process = Process.Start(psi);
            process?.WaitForExit(10_000);
        }
#pragma warning disable CA1031
        catch
#pragma warning restore CA1031
        {
            // macOS typically sources uv from the extracted archive which keeps the exec bit.
        }
    }

    public void Dispose() => _httpClient.Dispose();
}