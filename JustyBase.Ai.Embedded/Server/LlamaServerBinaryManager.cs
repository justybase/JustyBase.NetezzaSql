using JustyBase.Ai.Embedded.Abstractions;
using System.IO.Compression;
using System.Net.Http.Headers;

namespace JustyBase.Ai.Embedded.Server;

/// <summary>
/// Downloads and caches the llama.cpp <c>llama-server.exe</c> binary.
///
/// NOTE: the release tag/URLs must be verified at build time — llama.cpp moved its release
/// distribution off GitHub; the URL template below is best-effort. Override the tag with the
/// <c>JUSTYBASE_LLAMA_TAG</c> environment variable if a different release is required.
/// </summary>
public sealed class LlamaServerBinaryManager : ILlamaServerBinary
{
    /// <summary>
    /// llama.cpp release tag. Must support the model architectures in the embedded catalogs
    /// (qwen35 / gemma4 / devstral2) — b4796 is too old and cannot load Qwen 3.5. The CPU
    /// build's zip asset was also renamed from "avx2" to "cpu" in the b10xxx-era releases.
    /// </summary>
    private const string DefaultTag = "b10295";

    private readonly Func<bool> _preferVulkan;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public LlamaServerBinaryManager(Func<bool> preferVulkan, HttpClient? httpClient = null)
    {
        _preferVulkan = preferVulkan ?? (() => true);
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? CreateDefaultHttpClient();
        BinaryDirectory = DefaultBinaryDirectory();
        EnsureBinaryDirectory();
    }

    public string BinaryDirectory { get; }

    /// <summary>llama-server.exe for the currently selected variant (vulkan or avx2).</summary>
    public string BinaryPath => Path.Combine(BinaryDirectory, BinaryVariant, "llama-server.exe");

    // Since the b10xxx-era releases llama-server.exe is a small launcher stub — the real
    // implementation ships in llama-server-impl.dll — so presence alone is not enough to
    // prove a complete extraction. Require the implementation DLL (and the ggml-base.dll
    // it depends on) to be on disk so partial/broken installs are repaired automatically.
    public bool IsBinaryPresent =>
        File.Exists(BinaryPath)
        && File.Exists(Path.Combine(VariantDirectory, "ggml-base.dll"))
        && File.Exists(Path.Combine(VariantDirectory, "llama-server-impl.dll"))
        && new FileInfo(Path.Combine(VariantDirectory, "llama-server-impl.dll")).Length > 1_000_000;

    public string BinaryVariant => _preferVulkan() ? "vulkan" : "avx2";

    public static string DefaultBinaryDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "JustyBase",
            "llama-server");

    public string EnsureBinaryDirectory()
    {
        Directory.CreateDirectory(BinaryDirectory);
        return BinaryDirectory;
    }

    private string VariantDirectory => Path.Combine(BinaryDirectory, BinaryVariant);

    private static HttpClient CreateDefaultHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromHours(2) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("JustyBase", "1.0"));
        return client;
    }

    /// <summary>Downloads and extracts llama-server.exe for the current variant when missing. Safe to call repeatedly.</summary>
    public async Task EnsureBinaryAsync(
        IProgress<FimModelProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (IsBinaryPresent)
        {
            progress?.Report(new FimModelProgress(1.0, $"llama-server ({BinaryVariant}) already present."));
            return;
        }

        EnsureBinaryDirectory();
        Directory.CreateDirectory(VariantDirectory);
        var tag = Environment.GetEnvironmentVariable("JUSTYBASE_LLAMA_TAG");
        if (string.IsNullOrWhiteSpace(tag))
        {
            tag = DefaultTag;
        }

        var variant = BinaryVariant;
        var zipVariant = variant.Equals("vulkan", StringComparison.OrdinalIgnoreCase) ? "vulkan" : "cpu";
        var zipUri = new Uri(
            $"https://github.com/ggml-org/llama.cpp/releases/download/{tag}/llama-{tag}-bin-win-{zipVariant}-x64.zip");

        var zipPath = Path.Combine(BinaryDirectory, $"llama-{tag}-bin-win-{zipVariant}-x64.zip");
        progress?.Report(new FimModelProgress(0, $"Downloading llama-server ({variant})…"));

        try
        {
            long total = 0;
            long copied = 0;
            using (var response = await _httpClient.GetAsync(zipUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException(
                        $"llama-server download failed: {(int)response.StatusCode} {response.ReasonPhrase} ({zipUri}). " +
                        "Check the llama.cpp release tag (JUSTYBASE_LLAMA_TAG) or your network.");
                }

                total = response.Content.Headers.ContentLength ?? 0L;
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var target = new FileStream(
                    zipPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 1024 * 128,
                    useAsync: true);
                var buffer = new byte[1024 * 128];
                int read;
                while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    copied += read;
                    if (total > 0)
                    {
                        progress?.Report(new FimModelProgress(
                            Math.Clamp(copied / (double)total, 0, 0.9),
                            $"Downloading llama-server… {copied / (1024d * 1024d):0.#} / {total / (1024d * 1024d):0.#} MB"));
                    }
                }

                await target.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (total > 0 && copied < total)
            {
                throw new InvalidOperationException(
                    $"llama-server download finished early: {copied} of {total} bytes received. The transfer was interrupted — please try again.");
            }

            progress?.Report(new FimModelProgress(0.95, "Extracting llama-server…"));
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                var exeEntry = archive.Entries.FirstOrDefault(e =>
                    e.FullName.EndsWith("llama-server.exe", StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException("llama-server.exe not found in the downloaded archive.");

                exeEntry.ExtractToFile(BinaryPath, overwrite: true);

                foreach (var entry in archive.Entries)
                {
                    if (entry.FullName.EndsWith("/", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(entry.FullName, exeEntry.FullName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    entry.ExtractToFile(
                        Path.Combine(VariantDirectory, Path.GetFileName(entry.FullName)),
                        overwrite: true);
                }
            }

            if (!IsBinaryPresent)
            {
                throw new InvalidOperationException("Extracted llama-server.exe is missing or empty.");
            }

            progress?.Report(new FimModelProgress(1.0, "llama-server ready."));
        }
        finally
        {
            try { File.Delete(zipPath); } catch { /* best effort */ }
        }
    }

    public void Dispose() => _httpClient.Dispose();
}
