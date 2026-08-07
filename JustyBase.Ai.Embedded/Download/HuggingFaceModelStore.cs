using JustyBase.Ai.Embedded.Abstractions;
using System.Net.Http.Headers;

namespace JustyBase.Ai.Embedded.Download;

public interface IModelStore
{
    ModelDescriptor CurrentModel { get; }
    string ModelsDirectory { get; }
    string ModelFileName { get; }
    string LocalModelPath { get; }
    bool IsModelPresent { get; }
    /// <summary>Creates the models directory if missing. Returns the directory path.</summary>
    string EnsureModelsDirectory();
    Task EnsureModelAsync(IProgress<FimModelProgress>? progress = null, CancellationToken cancellationToken = default);
    /// <summary>Deletes the selected GGUF (and any .partial). Returns false if nothing was on disk.</summary>
    bool TryDeleteCurrentModel();
    /// <summary>Deletes only the in-progress .partial file for the selected model (if any).</summary>
    bool TryDeletePartialDownload();
}

/// <summary>
/// Downloads the currently selected GGUF into %LOCALAPPDATA%/JustyBase/models/.
/// Selection is resolved on each call so preferences can switch models without restarts.
/// </summary>
public sealed class HuggingFaceModelStore : IModelStore, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly IModelCatalog _catalog;
    private readonly Func<string?> _getSelectedModelId;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public HuggingFaceModelStore(
        IModelCatalog catalog,
        Func<string?> getSelectedModelId,
        HttpClient? httpClient = null,
        string? modelsDirectory = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _getSelectedModelId = getSelectedModelId ?? throw new ArgumentNullException(nameof(getSelectedModelId));
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? CreateDefaultHttpClient();
        ModelsDirectory = modelsDirectory ?? GetDefaultModelsDirectory();
        EnsureModelsDirectory();
    }

    public string ModelsDirectory { get; }

    public ModelDescriptor CurrentModel => _catalog.Resolve(_getSelectedModelId());

    public string ModelFileName => CurrentModel.FileName;

    public string LocalModelPath => Path.Combine(ModelsDirectory, ModelFileName);

    private string PartialModelPath => LocalModelPath + ".partial";

    public bool IsModelPresent
    {
        get
        {
            var path = LocalModelPath;
            return File.Exists(path) && new FileInfo(path).Length > 1_000_000;
        }
    }

    public static string GetDefaultModelsDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "JustyBase",
            "models");

    public string EnsureModelsDirectory()
    {
        Directory.CreateDirectory(ModelsDirectory);
        return ModelsDirectory;
    }

    private static HttpClient CreateDefaultHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromHours(6),
        };
        // Hugging Face often stalls or rejects clients with no User-Agent.
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("JustyBase", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        return client;
    }

    public async Task EnsureModelAsync(IProgress<FimModelProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var model = CurrentModel;
        EnsureModelsDirectory();
        var localPath = Path.Combine(ModelsDirectory, model.FileName);

        if (File.Exists(localPath) && new FileInfo(localPath).Length > 1_000_000)
        {
            progress?.Report(new FimModelProgress(1.0, $"{model.DisplayName} already present."));
            return;
        }

        progress?.Report(new FimModelProgress(0, "Waiting for download slot…"));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var tempPath = localPath + ".partial";
        try
        {
            EnsureModelsDirectory();

            if (File.Exists(localPath) && new FileInfo(localPath).Length > 1_000_000)
            {
                progress?.Report(new FimModelProgress(1.0, $"{model.DisplayName} already present."));
                return;
            }

            // Fresh download — discard any leftover partial from a previous cancel/crash.
            TryDeleteFile(tempPath);

            var expectedTotal = model.ApproxBytes > 0 ? model.ApproxBytes : 0L;
            var downloadUri = EnsureDownloadQuery(model.DownloadUri);

            progress?.Report(new FimModelProgress(
                0,
                $"Connecting to download host for {model.DisplayName}…"));

            using var request = new HttpRequestMessage(HttpMethod.Get, downloadUri);
            long total = 0;
            long copied = 0;
            using (var response = await _httpClient.SendAsync(
                       request,
                       HttpCompletionOption.ResponseHeadersRead,
                       cancellationToken).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException(
                        $"Download failed: {(int)response.StatusCode} {response.ReasonPhrase} ({downloadUri}).");
                }

                var contentLength = response.Content.Headers.ContentLength;
                total = contentLength is > 0 ? contentLength.Value : expectedTotal;
                progress?.Report(new FimModelProgress(
                    0.01,
                    total > 0
                        ? $"Connected. Saving to {tempPath} ({total / (1024d * 1024d):0.#} MB)…"
                        : $"Connected. Saving to {tempPath}…"));

                var remote = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using (remote.ConfigureAwait(false))
                {
                    var localStream = new FileStream(
                        tempPath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None,
                        bufferSize: 1024 * 128,
                        useAsync: true);
                    await using (localStream.ConfigureAwait(false))
                    {
                        var buffer = new byte[1024 * 128];
                        int read;
                        var lastFlushUtc = DateTime.UtcNow;
                        while ((read = await remote.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
                        {
                            await localStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                            copied += read;
                            ReportDownloadProgress(progress, model, copied, total);

                            // Periodic flush so a hung transfer still leaves a visible growing .partial on disk.
                            if ((DateTime.UtcNow - lastFlushUtc).TotalSeconds >= 2)
                            {
                                await localStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                                lastFlushUtc = DateTime.UtcNow;
                            }
                        }

                        await localStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (total > 0 && copied < total)
            {
                TryDeleteFile(tempPath);
                throw new InvalidOperationException(
                    $"Download finished early: {copied} of {total} bytes received. The transfer was interrupted — please try again.");
            }

            if (copiedFileLooksEmpty(tempPath))
            {
                TryDeleteFile(tempPath);
                throw new InvalidOperationException(
                    "Download finished with an empty file. Check network access to Hugging Face and try again.");
            }

            if (File.Exists(localPath))
            {
                File.Delete(localPath);
            }

            File.Move(tempPath, localPath);
            progress?.Report(new FimModelProgress(1.0, $"{model.DisplayName} download complete."));
        }
        catch (OperationCanceledException)
        {
            TryDeleteFile(tempPath);
            progress?.Report(new FimModelProgress(0, "Download cancelled."));
            throw;
        }
#pragma warning disable CA1031
        catch
#pragma warning restore CA1031
        {
            TryDeleteFile(tempPath);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool copiedFileLooksEmpty(string path)
    {
        try
        {
            return !File.Exists(path) || new FileInfo(path).Length < 1_000;
        }
#pragma warning disable CA1031
        catch
#pragma warning restore CA1031
        {
            return true;
        }
    }

    private static Uri EnsureDownloadQuery(Uri uri)
    {
        // Hugging Face CDN prefers explicit download flag for large binaries.
        if (uri.Query.Contains("download=", StringComparison.OrdinalIgnoreCase))
        {
            return uri;
        }

        var builder = new UriBuilder(uri);
        builder.Query = string.IsNullOrEmpty(builder.Query)
            ? "download=true"
            : builder.Query.TrimStart('?') + "&download=true";
        return builder.Uri;
    }

    private static void ReportDownloadProgress(
        IProgress<FimModelProgress>? progress,
        ModelDescriptor model,
        long copied,
        long total)
    {
        if (progress is null)
        {
            return;
        }

        if (total > 0)
        {
            var fraction = Math.Clamp(copied / (double)total, 0, 0.99);
            var copiedMb = copied / (1024d * 1024d);
            var totalMb = total / (1024d * 1024d);
            progress.Report(new FimModelProgress(
                fraction,
                $"Downloading {model.Id}… {copiedMb:0.#} / {totalMb:0.#} MB"));
            return;
        }

        var mb = copied / (1024d * 1024d);
        var softFraction = Math.Clamp(0.05 + (mb / (mb + 500.0)) * 0.85, 0.05, 0.9);
        progress.Report(new FimModelProgress(softFraction, $"Downloading {model.Id}… {mb:0.#} MB"));
    }

    public bool TryDeleteCurrentModel()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _gate.Wait();
        try
        {
            var deleted = TryDeleteFile(PartialModelPath);
            deleted |= TryDeleteFile(LocalModelPath);
            return deleted;
        }
        finally
        {
            _gate.Release();
        }
    }

    public bool TryDeletePartialDownload()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _gate.Wait();
        try
        {
            return TryDeleteFile(PartialModelPath);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool TryDeleteFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            File.Delete(path);
            return true;
        }
#pragma warning disable CA1031
        catch
#pragma warning restore CA1031
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Dispose();
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
