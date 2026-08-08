namespace JustyBase.Ai.Embedded.Download;

/// <summary>
/// Describes a downloadable model (FIM or chat). On Windows/Linux the <c>FileName</c> +
/// <c>DownloadUri</c> GGUF pair is used (llama.cpp llama-server). On Apple Silicon the
/// <c>MlxRepoId</c> Hugging Face MLX snapshot is downloaded and served by <c>mlx_lm.server</c>.
/// </summary>
public sealed record ModelDescriptor(
    string Id,
    string DisplayName,
    string FileName,
    Uri DownloadUri,
    string ApproxSizeLabel,
    Uri SourceModelUrl,
    string Notes,
    long ApproxBytes,
    string Family = "Qwen (recommended)",
    bool RequiresLicenseAcceptance = false,
    string? LicenseName = null,
    Uri? LicenseUrl = null,
    string? LicenseSummary = null,
    string? MlxRepoId = null,
    string? MlxSizeLabel = null,
    long MlxApproxBytes = 0L);

public interface IModelCatalog
{
    IReadOnlyList<ModelDescriptor> Models { get; }
    ModelDescriptor Resolve(string? modelId);
}
