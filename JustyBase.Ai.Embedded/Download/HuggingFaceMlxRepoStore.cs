using JustyBase.Ai.Embedded.Abstractions;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JustyBase.Ai.Embedded.Download;

/// <summary>
/// Downloads the currently selected MLX model snapshot (a folder of safetensors/config/
/// tokenizer files) from a Hugging Face <c>mlx-community</c> repo into
/// %LOCALAPPDATA%/JustyBase/models/&lt;model-id&gt;/. Served by <c>mlx_lm.server</c> on Apple Silicon.
/// </summary>
public sealed class HuggingFaceMlxRepoStore : IModelStore, IDisposable
{
    private const string PartialSuffix = ".mlx-partial";
    private const long MinModelBytes = 1_000_000;

    private readonly IModelCatalog _catalog;
    private readonly Func<string?> _getSelectedModelId;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public HuggingFaceMlxRepoStore(
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

    /// <summary>MLX models are downloaded as directories, so there is no single file name.</summary>
    public string ModelFileName => string.Empty;

    public string LocalModelPath => Path.Combine(ModelsDirectory, CurrentModel.Id);

    private string PartialModelPath => LocalModelPath + PartialSuffix;

    /// <summary>Hugging Face repo id hosting the selected model's MLX snapshot.</summary>
    public string RepoId =>
        CurrentModel.MlxRepoId
        ?? throw new InvalidOperationException(
            $"Model '{CurrentModel.Id}' has no MLX snapshot configured. MLX is only available on Apple Silicon.");

    public bool IsModelPresent
    {
        get
        {
            var dir = LocalModelPath;
            if (!Directory.Exists(dir) || !File.Exists(Path.Combine(dir, "config.json")))
            {
                return false;
            }

            try
            {
                long total = 0;
                foreach (var file in Directory.EnumerateFiles(dir, "*.safetensors"))
                {
                    total += new FileInfo(file).Length;
                    if (total > MinModelBytes)
                    {
                        return true;
                    }
                }

                foreach (var file in Directory.EnumerateFiles(dir, "*.npz"))
                {
                    total += new FileInfo(file).Length;
                    if (total > MinModelBytes)
                    {
                        return true;
                    }
                }

                return false;
            }
#pragma warning disable CA1031
            catch
#pragma warning restore CA1031
            {
                return false;
            }
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
        var client = new HttpClient { Timeout = TimeSpan.FromHours(6) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("JustyBase", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        return client;
    }

    public async Task EnsureModelAsync(IProgress<FimModelProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var model = CurrentModel;
        var repo = RepoId;
        EnsureModelsDirectory();
        var targetDir = LocalModelPath;

        if (IsModelPresent)
        {
            progress?.Report(new FimModelProgress(1.0, $"{model.DisplayName} (MLX) already present."));
            return;
        }

        progress?.Report(new FimModelProgress(0, "Waiting for download slot…"));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var partialDir = PartialModelPath;
        try
        {
            if (IsModelPresent)
            {
                progress?.Report(new FimModelProgress(1.0, $"{model.DisplayName} (MLX) already present."));
                return;
            }

            if (Directory.Exists(partialDir))
            {
                Directory.Delete(partialDir, recursive: true);
            }

            Directory.CreateDirectory(partialDir);

            var entries = await ListFilesAsync(repo, cancellationToken).ConfigureAwait(false);
            var files = entries.Where(e => string.Equals(e.Type, "file", StringComparison.OrdinalIgnoreCase)).ToList();
            if (files.Count == 0)
            {
                throw new InvalidOperationException($"Hugging Face repo '{repo}' contains no downloadable files.");
            }

            var totalBytes = files.Sum(f => Math.Max(0L, f.Size));
            long copiedTotal = 0;
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = SanitizeRelativePath(file.Path);
                var target = Path.Combine(partialDir, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target) ?? partialDir);

                progress?.Report(new FimModelProgress(
                    EstimateFraction(copiedTotal, totalBytes),
                    $"Downloading MLX model… {relative}"));

                copiedTotal += await DownloadFileAsync(
                    repo,
                    file.Path,
                    target,
                    totalBytes > 0 ? totalBytes : model.MlxApproxBytes,
                    copiedTotal,
                    model,
                    progress,
                    cancellationToken).ConfigureAwait(false);
            }

            if (Directory.Exists(targetDir))
            {
                Directory.Delete(targetDir, recursive: true);
            }

            Directory.Move(partialDir, targetDir);
            progress?.Report(new FimModelProgress(1.0, $"{model.DisplayName} (MLX) download complete."));
        }
        catch (OperationCanceledException)
        {
            TryDeleteDirectory(partialDir);
            progress?.Report(new FimModelProgress(0, "Download cancelled."));
            throw;
        }
#pragma warning disable CA1031
        catch
#pragma warning restore CA1031
        {
            TryDeleteDirectory(partialDir);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<HfTreeEntry>> ListFilesAsync(string repo, CancellationToken cancellationToken)
    {
        var uri = new Uri($"https://huggingface.co/api/models/{EscapeSegment(repo)}/tree/main?recursive=true");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Failed to list Hugging Face repo '{repo}': {(int)response.StatusCode} {response.ReasonPhrase}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var entries = await JsonSerializer.DeserializeAsync(
            stream,
            MlxDownloadJsonContext.Default.ListHfTreeEntry,
            cancellationToken).ConfigureAwait(false);
        return entries ?? [];
    }

    private async Task<long> DownloadFileAsync(
        string repo,
        string path,
        string targetPath,
        long totalBytes,
        long copiedTotal,
        ModelDescriptor model,
        IProgress<FimModelProgress>? progress,
        CancellationToken cancellationToken)
    {
        var tempPath = targetPath + ".partial";
        var uri = BuildResolveUri(repo, path);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        long copied = 0;
        using (var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"Download failed: {(int)response.StatusCode} {response.ReasonPhrase} ({uri}).");
            }

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                var localStream = new FileStream(
                    tempPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 128 * 1024,
                    useAsync: true);
                await using (localStream.ConfigureAwait(false))
                {
                    var buffer = new byte[128 * 1024];
                    int read;
                    var lastFlush = DateTime.UtcNow;
                    while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        await localStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                        copied += read;
                        ReportProgress(progress, model, copiedTotal + copied, totalBytes);

                        if ((DateTime.UtcNow - lastFlush).TotalSeconds >= 2)
                        {
                            await localStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                            lastFlush = DateTime.UtcNow;
                        }
                    }

                    await localStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
            }
        }

        if (new FileInfo(tempPath).Length < 1_000)
        {
            TryDeleteFile(tempPath);
            throw new InvalidOperationException($"Downloaded '{path}' looks empty — check network access to Hugging Face.");
        }

        File.Move(tempPath, targetPath, overwrite: true);
        return copied;
    }

    private static void ReportProgress(
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
            progress.Report(new FimModelProgress(
                fraction,
                $"Downloading {model.Id} (MLX)… {copied / (1024d * 1024d):0.#} / {total / (1024d * 1024d):0.#} MB"));
            return;
        }

        var mb = copied / (1024d * 1024d);
        var soft = Math.Clamp(0.05 + (mb / (mb + 500.0)) * 0.85, 0.05, 0.9);
        progress.Report(new FimModelProgress(soft, $"Downloading {model.Id} (MLX)… {mb:0.#} MB"));
    }

    private static double EstimateFraction(long copied, long total)
        => total > 0 ? Math.Clamp(copied / (double)total, 0, 0.99) : 0;

    private static Uri BuildResolveUri(string repo, string path)
    {
        var escapedPath = string.Join('/', path.Split('/').Select(EscapeSegment));
        return new Uri($"https://huggingface.co/{EscapeSegment(repo)}/resolve/main/{escapedPath}?download=true");
    }

    private static string EscapeSegment(string segment) => Uri.EscapeDataString(segment);

    private static string SanitizeRelativePath(string path)
    {
        var parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var safe = parts
            .Where(p => p is not (".." or "."))
            .Select(p => p.Trim())
            .ToArray();
        return string.Join(Path.DirectorySeparatorChar, safe);
    }

    public bool TryDeleteCurrentModel()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _gate.Wait();
        try
        {
            var deleted = TryDeleteDirectory(PartialModelPath);
            deleted |= TryDeleteDirectory(LocalModelPath);
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
            return TryDeleteDirectory(PartialModelPath);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool TryDeleteDirectory(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return false;
            }

            Directory.Delete(path, recursive: true);
            return true;
        }
#pragma warning disable CA1031
        catch
#pragma warning restore CA1031
        {
            return false;
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

internal sealed class HfTreeEntry
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; init; }
}

[JsonSerializable(typeof(List<HfTreeEntry>))]
internal sealed partial class MlxDownloadJsonContext : JsonSerializerContext
{
}
