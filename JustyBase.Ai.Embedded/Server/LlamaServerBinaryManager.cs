using JustyBase.Ai.Embedded.Abstractions;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;

namespace JustyBase.Ai.Embedded.Server;

/// <summary>
/// Downloads and caches the platform-native llama.cpp <c>llama-server</c> binary.
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

    /// <summary>llama-server for the currently selected variant (Vulkan or CPU).</summary>
    public string BinaryPath => Path.Combine(BinaryDirectory, BinaryVariant, ExecutableName);

    private static string ExecutableName => OperatingSystem.IsWindows() ? "llama-server.exe" : "llama-server";

    // Since the b10xxx-era Windows releases llama-server.exe is a small launcher stub, require
    // the implementation DLL and ggml-base.dll there. Unix bundles use different shared-library
    // names, so the executable is the portable completeness check for those archives.
    public bool IsBinaryPresent =>
        File.Exists(BinaryPath)
        && new FileInfo(BinaryPath).Length > 0
        && (!OperatingSystem.IsWindows()
            || (File.Exists(Path.Combine(VariantDirectory, "ggml-base.dll"))
                && File.Exists(Path.Combine(VariantDirectory, "llama-server-impl.dll"))
                && new FileInfo(Path.Combine(VariantDirectory, "llama-server-impl.dll")).Length > 1_000_000));

    public string BinaryVariant => _preferVulkan() && SupportsVulkanAsset() ? "vulkan" : "avx2";

    private static bool SupportsVulkanAsset()
        => RuntimeInformation.ProcessArchitecture == Architecture.X64
            && (OperatingSystem.IsWindows() || OperatingSystem.IsLinux());

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

    /// <summary>Downloads and extracts the native llama-server for the current variant when missing.</summary>
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
        var asset = ResolveReleaseAsset(tag, variant);
        var archiveUri = new Uri(
            $"https://github.com/ggml-org/llama.cpp/releases/download/{tag}/{asset.FileName}");

        var archivePath = Path.Combine(BinaryDirectory, asset.FileName);
        progress?.Report(new FimModelProgress(0, $"Downloading llama-server ({variant})…"));

        try
        {
            long total = 0;
            long copied = 0;
            using (var response = await _httpClient.GetAsync(archiveUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException(
                        $"llama-server download failed: {(int)response.StatusCode} {response.ReasonPhrase} ({archiveUri}). " +
                        "Check the llama.cpp release tag (JUSTYBASE_LLAMA_TAG) or your network.");
                }

                total = response.Content.Headers.ContentLength ?? 0L;
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var target = new FileStream(
                    archivePath,
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
            await ExtractArchiveAsync(archivePath, asset, cancellationToken).ConfigureAwait(false);
            MakeExecutable(BinaryPath);

            if (!IsBinaryPresent)
            {
                throw new InvalidOperationException($"Extracted {asset.ExecutableName} is missing or incomplete.");
            }

            progress?.Report(new FimModelProgress(1.0, "llama-server ready."));
        }
        finally
        {
            try { File.Delete(archivePath); } catch { /* best effort */ }
        }
    }

    private sealed record ReleaseAsset(string FileName, string ExecutableName, bool IsZip);

    private static ReleaseAsset ResolveReleaseAsset(string tag, string variant)
    {
        var architecture = RuntimeInformation.ProcessArchitecture;
        var isVulkan = string.Equals(variant, "vulkan", StringComparison.OrdinalIgnoreCase);

        if (OperatingSystem.IsWindows())
        {
            if (architecture != Architecture.X64)
            {
                throw new PlatformNotSupportedException(
                    $"The bundled llama-server currently supports Windows x64 only (found {architecture}).");
            }

            var flavor = isVulkan ? "vulkan" : "cpu";
            return new ReleaseAsset($"llama-{tag}-bin-win-{flavor}-x64.zip", "llama-server.exe", IsZip: true);
        }

        if (OperatingSystem.IsLinux())
        {
            var architectureName = architecture switch
            {
                Architecture.X64 => "x64",
                Architecture.Arm64 => "arm64",
                _ => throw new PlatformNotSupportedException(
                    $"The bundled llama-server currently supports Linux x64 and arm64 (found {architecture}).")
            };
            var flavor = isVulkan ? "vulkan-" : string.Empty;
            return new ReleaseAsset(
                $"llama-{tag}-bin-ubuntu-{flavor}{architectureName}.tar.gz",
                "llama-server",
                IsZip: false);
        }

        if (OperatingSystem.IsMacOS())
        {
            var architectureName = architecture switch
            {
                Architecture.X64 => "x64",
                Architecture.Arm64 => "arm64",
                _ => throw new PlatformNotSupportedException(
                    $"The bundled llama-server currently supports macOS x64 and arm64 (found {architecture}).")
            };
            return new ReleaseAsset(
                $"llama-{tag}-bin-macos-{architectureName}.tar.gz",
                "llama-server",
                IsZip: false);
        }

        throw new PlatformNotSupportedException("The bundled llama-server is not available for this operating system.");
    }

    private async Task ExtractArchiveAsync(
        string archivePath,
        ReleaseAsset asset,
        CancellationToken cancellationToken)
    {
        if (asset.IsZip)
        {
            using var archive = ZipFile.OpenRead(archivePath);
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fileName = Path.GetFileName(entry.FullName);
                if (string.IsNullOrEmpty(fileName) || entry.FullName.EndsWith("/", StringComparison.Ordinal))
                {
                    continue;
                }

                entry.ExtractToFile(Path.Combine(VariantDirectory, fileName), overwrite: true);
            }

            return;
        }

        await using var file = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var tar = new TarReader(gzip, leaveOpen: false);
        TarEntry? tarEntry;
        while ((tarEntry = await tar.GetNextEntryAsync().ConfigureAwait(false)) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (tarEntry.EntryType != TarEntryType.RegularFile)
            {
                continue;
            }

            var fileName = Path.GetFileName(tarEntry.Name);
            if (string.IsNullOrEmpty(fileName) || tarEntry.DataStream is null)
            {
                continue;
            }

            var outputPath = Path.Combine(VariantDirectory, fileName);
            await using var output = new FileStream(
                outputPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 128 * 1024,
                useAsync: true);
            await tarEntry.DataStream.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void MakeExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
#pragma warning disable CA1031
        catch
#pragma warning restore CA1031
        {
            // A read-only filesystem may preserve the executable bit from the tar archive.
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
